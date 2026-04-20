namespace Server

// ---------------------------------------------------------------------------
// FieldGrad — forward-mode autodiff evaluator for FieldNode.
//
// A `Grad` is a scalar value plus its three partials (∂/∂x, ∂/∂y, ∂/∂z).
// Evaluating a FieldNode with Grad-valued coordinates yields:
//   .V   = SDF value at the query point
//   .Dx, .Dy, .Dz = spatial gradient components
//
// The surface normal at a query point is the normalised (Dx, Dy, Dz), and
// (V, Dx, Dy, Dz) together give the Hermite data needed for QEF vertex
// placement in dual contouring.
// ---------------------------------------------------------------------------

type Grad = { V: float; Dx: float; Dy: float; Dz: float }

module Grad =
    let zero = { V = 0.0; Dx = 0.0; Dy = 0.0; Dz = 0.0 }

    let constant (v: float) : Grad = { V = v; Dx = 0.0; Dy = 0.0; Dz = 0.0 }

    let neg (g: Grad) : Grad =
        { V = -g.V; Dx = -g.Dx; Dy = -g.Dy; Dz = -g.Dz }

    let add (a: Grad) (b: Grad) : Grad =
        { V = a.V + b.V; Dx = a.Dx + b.Dx; Dy = a.Dy + b.Dy; Dz = a.Dz + b.Dz }

    let sub (a: Grad) (b: Grad) : Grad =
        { V = a.V - b.V; Dx = a.Dx - b.Dx; Dy = a.Dy - b.Dy; Dz = a.Dz - b.Dz }

    /// Product rule: (ab)' = a'b + ab'.
    let mul (a: Grad) (b: Grad) : Grad =
        { V = a.V * b.V
          Dx = a.V * b.Dx + a.Dx * b.V
          Dy = a.V * b.Dy + a.Dy * b.V
          Dz = a.V * b.Dz + a.Dz * b.V }

    let scale (s: float) (g: Grad) : Grad =
        { V = s * g.V; Dx = s * g.Dx; Dy = s * g.Dy; Dz = s * g.Dz }

    /// Square by self-multiply — re-uses product rule.
    let square (g: Grad) : Grad = mul g g

    /// d/dx sqrt(f) = f' / (2 sqrt(f)). Clamps negative inputs to 0 at the
    /// value layer; derivative near 0 is large but finite (we clamp √V to
    /// a tiny epsilon to avoid divide-by-zero).
    let sqrt (g: Grad) : Grad =
        let eps = 1e-12
        let v = System.Math.Sqrt (max g.V eps)
        let k = 1.0 / (2.0 * v)
        { V = (if g.V > 0.0 then System.Math.Sqrt g.V else 0.0)
          Dx = k * g.Dx
          Dy = k * g.Dy
          Dz = k * g.Dz }

    /// d/dx |f| = sign(f) * f'. Subgradient at f=0: pick 0.
    let abs (g: Grad) : Grad =
        let s =
            if g.V > 0.0 then 1.0
            elif g.V < 0.0 then -1.0
            else 0.0
        { V = System.Math.Abs g.V
          Dx = s * g.Dx
          Dy = s * g.Dy
          Dz = s * g.Dz }

    /// min picks the whole Grad of whichever has the smaller value.
    /// At ties, pick `a` (consistent subgradient choice).
    let imin (a: Grad) (b: Grad) : Grad =
        if a.V <= b.V then a else b

    let imax (a: Grad) (b: Grad) : Grad =
        if a.V >= b.V then a else b

    /// max(g, 0) — clamps negative values to 0; derivative is 0 there.
    let clampNonNeg (g: Grad) : Grad =
        if g.V > 0.0 then g else constant 0.0


module FieldGrad =

    let private slotV (slots: SlotTable) (s: Slot) : float = slots.Values.[s]

    /// Smooth-min with forward-mode autodiff.
    /// smin(a, b, k) = min(a, b) - h³ * k / 6
    /// where h = max((k - |a-b|) / k, 0) ∈ [0, 1].
    /// C¹-smooth at a = b (the minus sign kink is cancelled by h³ vanishing).
    let private smoothMin (a: Grad) (b: Grad) (k: float) : Grad =
        if k <= 1e-12 then Grad.imin a b
        else
            let m = Grad.imin a b
            let diffAbs = Grad.abs (Grad.sub a b)
            let h =
                Grad.clampNonNeg
                    (Grad.sub (Grad.constant 1.0) (Grad.scale (1.0 / k) diffAbs))
            let correction = Grad.scale (k / 6.0) (Grad.mul h (Grad.square h))
            Grad.sub m correction

    let rec eval (slots: SlotTable) (point: Grad * Grad * Grad) (node: FieldNode) : Grad =
        let (x, y, z) = point
        match node with
        | FPrimitive prim -> evalPrimitive slots point prim

        | FTranslate(sx, sy, sz, child) ->
            let dx = slotV slots sx
            let dy = slotV slots sy
            let dz = slotV slots sz
            // Shader computes p' = p - (dx, dy, dz). Shift input values;
            // partials w.r.t. world-space coords are unchanged by a constant shift.
            let x' = { x with V = x.V - dx }
            let y' = { y with V = y.V - dy }
            let z' = { z with V = z.V - dz }
            eval slots (x', y', z') child

        | FBoolean(op, kSlot, a, b) ->
            let ga = eval slots point a
            let gb = eval slots point b
            let k = slotV slots kSlot
            match op with
            | BoolUnion -> smoothMin ga gb k
            | BoolIntersect -> Grad.neg (smoothMin (Grad.neg ga) (Grad.neg gb) k)
            | BoolSubtract -> Grad.neg (smoothMin (Grad.neg ga) gb k)

        | FFieldOp(op, vSlot, child) ->
            let v = slotV slots vSlot
            let gc = eval slots point child
            match op with
            | OpThicken -> Grad.sub gc (Grad.constant v)
            | OpShell ->
                // max(child, -(child + v))
                Grad.imax gc (Grad.neg (Grad.add gc (Grad.constant v)))

        | FRotate _ ->
            failwith "FieldGrad.eval: FRotate not implemented yet"

        | FSketch sketch ->
            // 2D sketch SDF: z-independent. We compute the scalar SDF via
            // SketchSdf.evalAt and take numerical central differences in x/y.
            // The sketch arc distance has enough branches that hand-writing
            // the analytic gradient would bloat the code; numerical diff is
            // fine for meshing at MC vertex resolution.
            let (x, y, z) = point
            let px = x.V
            let py = y.V
            let h = 1e-4
            let v   = SketchSdf.evalAt slots sketch (px,     py)
            let vxp = SketchSdf.evalAt slots sketch (px + h, py)
            let vxm = SketchSdf.evalAt slots sketch (px - h, py)
            let vyp = SketchSdf.evalAt slots sketch (px,     py + h)
            let vym = SketchSdf.evalAt slots sketch (px,     py - h)
            let dvdx = (vxp - vxm) / (2.0 * h)
            let dvdy = (vyp - vym) / (2.0 * h)
            // Chain rule: propagate the 2D partials through the input Grads'
            // own gradients (captures any preceding FTranslate / composition).
            { V = v
              Dx = dvdx * x.Dx + dvdy * y.Dx
              Dy = dvdx * x.Dy + dvdy * y.Dy
              Dz = dvdx * x.Dz + dvdy * y.Dz }

    and private evalPrimitive (slots: SlotTable) (point: Grad * Grad * Grad) (prim: Primitive) : Grad =
        let (x, y, z) = point
        match prim with
        | PrimSphere rSlot ->
            let r = slotV slots rSlot
            // length(p) - r = sqrt(x² + y² + z²) - r
            let sumSq = Grad.add (Grad.add (Grad.square x) (Grad.square y)) (Grad.square z)
            Grad.sub (Grad.sqrt sumSq) (Grad.constant r)

        | PrimHalfPlane(axis, offsetSlot, flip) ->
            let off = slotV slots offsetSlot
            let coord =
                match axis with
                | "X" -> x
                | "Y" -> y
                | _ -> z
            let raw = Grad.sub coord (Grad.constant off)
            if flip then Grad.neg raw else raw

        | PrimBox(wSlot, hSlot, dSlot) ->
            let hx = slotV slots wSlot * 0.5
            let hy = slotV slots hSlot * 0.5
            let hz = slotV slots dSlot * 0.5
            let qx = Grad.sub (Grad.abs x) (Grad.constant hx)
            let qy = Grad.sub (Grad.abs y) (Grad.constant hy)
            let qz = Grad.sub (Grad.abs z) (Grad.constant hz)
            let zero = Grad.constant 0.0
            let outside =
                Grad.sqrt
                    (Grad.add
                        (Grad.add (Grad.square (Grad.imax qx zero)) (Grad.square (Grad.imax qy zero)))
                        (Grad.square (Grad.imax qz zero)))
            let inside = Grad.imin (Grad.imax qx (Grad.imax qy qz)) zero
            Grad.add outside inside

        | PrimCylinder(rSlot, hSlot) ->
            let r = slotV slots rSlot
            let halfH = slotV slots hSlot * 0.5
            let dRadial =
                Grad.sub
                    (Grad.sqrt (Grad.add (Grad.square x) (Grad.square y)))
                    (Grad.constant r)
            let dAxial = Grad.sub (Grad.abs z) (Grad.constant halfH)
            if dRadial.V > 0.0 && dAxial.V > 0.0 then
                Grad.sqrt (Grad.add (Grad.square dRadial) (Grad.square dAxial))
            else
                Grad.imax dRadial dAxial

    /// Convenience: seed x, y, z as independent variables and evaluate.
    /// Returns (V, ∂V/∂x, ∂V/∂y, ∂V/∂z) at the query point.
    let evalAt (slots: SlotTable) (x: float, y: float, z: float) (node: FieldNode) : Grad =
        let gx = { V = x; Dx = 1.0; Dy = 0.0; Dz = 0.0 }
        let gy = { V = y; Dx = 0.0; Dy = 1.0; Dz = 0.0 }
        let gz = { V = z; Dx = 0.0; Dy = 0.0; Dz = 1.0 }
        eval slots (gx, gy, gz) node
