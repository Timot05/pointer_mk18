namespace Server

// ---------------------------------------------------------------------------
// FieldInterval — interval-arithmetic evaluator for FieldNode.
//
// Given a 3D box of input intervals (X, Y, Z), returns a conservative
// [Lo, Hi] bound on the SDF value over that box. Used (eventually) to prune
// tiles that are provably outside (Lo > 0) or provably inside (Hi < 0) the
// surface before finer evaluation.
//
// Soundness contract: for every (x, y, z) in the input box, the true SDF
// value at that point must lie inside the returned interval. Unsupported
// constructs return [-infinity, +infinity] (the "I don't know" interval),
// which disables pruning but is always sound.
// ---------------------------------------------------------------------------

/// Closed interval [Lo, Hi] with Lo <= Hi.
type Interval = { Lo: float; Hi: float }

module Interval =
    let single (v: float) : Interval = { Lo = v; Hi = v }

    let make (a: float) (b: float) : Interval =
        if a <= b then { Lo = a; Hi = b } else { Lo = b; Hi = a }

    let unknown : Interval = { Lo = System.Double.NegativeInfinity; Hi = System.Double.PositiveInfinity }

    let contains (i: Interval) (v: float) : bool = v >= i.Lo && v <= i.Hi

    let neg (i: Interval) : Interval = { Lo = -i.Hi; Hi = -i.Lo }

    let add (a: Interval) (b: Interval) : Interval = { Lo = a.Lo + b.Lo; Hi = a.Hi + b.Hi }

    let sub (a: Interval) (b: Interval) : Interval = { Lo = a.Lo - b.Hi; Hi = a.Hi - b.Lo }

    let mul (a: Interval) (b: Interval) : Interval =
        let p1 = a.Lo * b.Lo
        let p2 = a.Lo * b.Hi
        let p3 = a.Hi * b.Lo
        let p4 = a.Hi * b.Hi
        { Lo = min (min p1 p2) (min p3 p4); Hi = max (max p1 p2) (max p3 p4) }

    let imin (a: Interval) (b: Interval) : Interval =
        { Lo = min a.Lo b.Lo; Hi = min a.Hi b.Hi }

    let imax (a: Interval) (b: Interval) : Interval =
        { Lo = max a.Lo b.Lo; Hi = max a.Hi b.Hi }

    let abs (i: Interval) : Interval =
        if i.Lo >= 0.0 then i
        elif i.Hi <= 0.0 then neg i
        else { Lo = 0.0; Hi = max -i.Lo i.Hi }

    /// Interval sqrt. Clamps negative inputs to 0 — callers should only
    /// invoke on intervals known non-negative (e.g. sums of squares).
    let sqrt (i: Interval) : Interval =
        { Lo = System.Math.Sqrt (max i.Lo 0.0)
          Hi = System.Math.Sqrt (max i.Hi 0.0) }

    let square (i: Interval) : Interval =
        if i.Lo >= 0.0 then { Lo = i.Lo * i.Lo; Hi = i.Hi * i.Hi }
        elif i.Hi <= 0.0 then { Lo = i.Hi * i.Hi; Hi = i.Lo * i.Lo }
        else { Lo = 0.0; Hi = max (i.Lo * i.Lo) (i.Hi * i.Hi) }

    /// Width of the interval (Hi - Lo). Useful in tests.
    let width (i: Interval) : float = i.Hi - i.Lo


/// 3D box of intervals — one interval per axis.
/// Field names are XI/YI/ZI to avoid clashing with Vec3 {X;Y;Z}.
type IntervalBox = { XI: Interval; YI: Interval; ZI: Interval }

module IntervalBox =
    let make (x: Interval) (y: Interval) (z: Interval) : IntervalBox =
        { XI = x; YI = y; ZI = z }

    let cube (lo: float) (hi: float) : IntervalBox =
        let i = Interval.make lo hi
        { XI = i; YI = i; ZI = i }


module FieldInterval =

    let private slotV (slots: SlotTable) (s: Slot) : float = slots.Values.[s]

    /// Returns an Interval that conservatively bounds the SDF value of `node`
    /// over the input `box`.
    let rec eval (slots: SlotTable) (box: IntervalBox) (node: FieldNode) : Interval =
        match node with
        | FPrimitive prim -> evalPrimitive slots box prim

        | FTranslate(sx, sy, sz, child) ->
            let dx = slotV slots sx
            let dy = slotV slots sy
            let dz = slotV slots sz
            // Shader computes p' = p - (dx, dy, dz); mirror that here.
            let shifted =
                { XI = Interval.sub box.XI (Interval.single dx)
                  YI = Interval.sub box.YI (Interval.single dy)
                  ZI = Interval.sub box.ZI (Interval.single dz) }
            eval slots shifted child

        | FBoolean(op, radiusSlot, a, b) ->
            let ia = eval slots box a
            let ib = eval slots box b
            let k = slotV slots radiusSlot
            match op with
            | BoolUnion -> smoothMin ia ib k
            | BoolIntersect -> Interval.neg (smoothMin (Interval.neg ia) (Interval.neg ib) k)
            | BoolSubtract -> Interval.neg (smoothMin (Interval.neg ia) ib k)

        | FFieldOp(op, valueSlot, child) ->
            let v = slotV slots valueSlot
            let ic = eval slots box child
            match op with
            | OpThicken -> Interval.sub ic (Interval.single v)
            | OpShell ->
                // max(child, -(child + v))
                Interval.imax ic (Interval.neg (Interval.add ic (Interval.single v)))

        | FRotate _ ->
            // Rotating an axis-aligned interval box does not yield an
            // axis-aligned box; a tight bound requires projecting the 8
            // corners through the rotation. Punt for now — sound but loose.
            Interval.unknown

        | FSketch sketch ->
            // Lipschitz-1 bound: evaluate the scalar 2D SDF at the tile's XY
            // centre, then widen by the XY diagonal to cover the whole tile.
            // Z is irrelevant (sketch is z-independent).
            let cx = (box.XI.Lo + box.XI.Hi) * 0.5
            let cy = (box.YI.Lo + box.YI.Hi) * 0.5
            let v = SketchSdf.evalAt slots sketch (cx, cy)
            let dx = (box.XI.Hi - box.XI.Lo) * 0.5
            let dy = (box.YI.Hi - box.YI.Lo) * 0.5
            let halfDiag = System.Math.Sqrt (dx * dx + dy * dy)
            { Lo = v - halfDiag; Hi = v + halfDiag }

    /// Same as `eval`, but also returns a potentially smaller `FieldNode`
    /// with dominated boolean branches replaced by the surviving child.
    /// Deeper recursions evaluate a smaller tree — the tape-simplification
    /// win from Fidget. Rules use `k` as the smooth-min slop:
    ///   Union:     drop B if A.Hi + k ≤ B.Lo  (A dominates)
    ///              drop A if B.Hi + k ≤ A.Lo
    ///   Intersect: drop B if A.Lo ≥ B.Hi + k
    ///              drop A if B.Lo ≥ A.Hi + k
    ///   Subtract:  drop B if A.Lo + B.Lo ≥ k
    and simplify (slots: SlotTable) (box: IntervalBox) (node: FieldNode) : Interval * FieldNode =
        match node with
        | FPrimitive prim ->
            evalPrimitive slots box prim, node

        | FTranslate(sx, sy, sz, child) ->
            let dx = slotV slots sx
            let dy = slotV slots sy
            let dz = slotV slots sz
            let shifted =
                { XI = Interval.sub box.XI (Interval.single dx)
                  YI = Interval.sub box.YI (Interval.single dy)
                  ZI = Interval.sub box.ZI (Interval.single dz) }
            let (cb, cs) = simplify slots shifted child
            cb, FTranslate(sx, sy, sz, cs)

        | FBoolean(op, kSlot, a, b) ->
            let (ia, sa) = simplify slots box a
            let (ib, sb) = simplify slots box b
            let k = slotV slots kSlot
            match op with
            | BoolUnion ->
                if ia.Hi + k <= ib.Lo then ia, sa
                elif ib.Hi + k <= ia.Lo then ib, sb
                else smoothMin ia ib k, FBoolean(op, kSlot, sa, sb)
            | BoolIntersect ->
                if ia.Lo >= ib.Hi + k then ia, sa
                elif ib.Lo >= ia.Hi + k then ib, sb
                else
                    Interval.neg (smoothMin (Interval.neg ia) (Interval.neg ib) k),
                    FBoolean(op, kSlot, sa, sb)
            | BoolSubtract ->
                if ia.Lo + ib.Lo >= k then ia, sa
                else
                    Interval.neg (smoothMin (Interval.neg ia) ib k),
                    FBoolean(op, kSlot, sa, sb)

        | FFieldOp(op, vSlot, child) ->
            let v = slotV slots vSlot
            let (ic, cs) = simplify slots box child
            let result =
                match op with
                | OpThicken -> Interval.sub ic (Interval.single v)
                | OpShell -> Interval.imax ic (Interval.neg (Interval.add ic (Interval.single v)))
            result, FFieldOp(op, vSlot, cs)

        | FRotate _ | FSketch _ ->
            Interval.unknown, node

    /// Smooth-min with widening for the k/6 dip.
    /// GpuSdf uses: smooth_min(a,b,k) = min(a,b) - h^3 * k / 6, h in [0,1].
    /// So smooth_min(a,b,k) in [min(a,b) - k/6, min(a,b)] when k > 0.
    and private smoothMin (a: Interval) (b: Interval) (k: float) : Interval =
        let sharp = Interval.imin a b
        if k <= 0.0 then sharp
        else { Lo = sharp.Lo - k / 6.0; Hi = sharp.Hi }

    and private evalPrimitive (slots: SlotTable) (box: IntervalBox) (prim: Primitive) : Interval =
        match prim with
        | PrimSphere rSlot ->
            let r = slotV slots rSlot
            // length(p) - r = sqrt(x² + y² + z²) - r
            let sumSq =
                Interval.add
                    (Interval.add (Interval.square box.XI) (Interval.square box.YI))
                    (Interval.square box.ZI)
            Interval.sub (Interval.sqrt sumSq) (Interval.single r)

        | PrimHalfPlane(axis, offsetSlot, flip) ->
            let off = slotV slots offsetSlot
            let coord =
                match axis with
                | "X" -> box.XI
                | "Y" -> box.YI
                | _ -> box.ZI
            let raw = Interval.sub coord (Interval.single off)
            if flip then Interval.neg raw else raw

        | PrimBox(wSlot, hSlot, dSlot) ->
            // GpuSdf.sdf_box:
            //   q = abs(p) - half_size
            //   outside = length(max(q, 0))
            //   inside  = min(max(qx, qy, qz), 0)
            //   return outside + inside
            let hx = slotV slots wSlot * 0.5
            let hy = slotV slots hSlot * 0.5
            let hz = slotV slots dSlot * 0.5
            let qx = Interval.sub (Interval.abs box.XI) (Interval.single hx)
            let qy = Interval.sub (Interval.abs box.YI) (Interval.single hy)
            let qz = Interval.sub (Interval.abs box.ZI) (Interval.single hz)
            let zero = Interval.single 0.0
            let mx = Interval.imax qx zero
            let my = Interval.imax qy zero
            let mz = Interval.imax qz zero
            let outside =
                Interval.sqrt
                    (Interval.add
                        (Interval.add (Interval.square mx) (Interval.square my))
                        (Interval.square mz))
            let inside = Interval.imin (Interval.imax qx (Interval.imax qy qz)) zero
            Interval.add outside inside

        | PrimCylinder(rSlot, hSlot) ->
            // GpuSdf.sdf_cylinder:
            //   d_radial = length(p.xy) - r
            //   d_axial  = |p.z| - h/2
            //   if d_radial > 0 && d_axial > 0:  sqrt(d_radial^2 + d_axial^2)
            //   else:                             max(d_radial, d_axial)
            let r = slotV slots rSlot
            let halfH = slotV slots hSlot * 0.5
            let dRadial =
                Interval.sub
                    (Interval.sqrt (Interval.add (Interval.square box.XI) (Interval.square box.YI)))
                    (Interval.single r)
            let dAxial = Interval.sub (Interval.abs box.ZI) (Interval.single halfH)
            let branch1 =
                Interval.sqrt (Interval.add (Interval.square dRadial) (Interval.square dAxial))
            let branch2 = Interval.imax dRadial dAxial
            // Branch selection by the condition (d_radial > 0 && d_axial > 0):
            //   definitely true  → branch1
            //   definitely false → branch2
            //   ambiguous        → conservative hull of both
            match dRadial.Lo > 0.0 && dAxial.Lo > 0.0,
                  dRadial.Hi <= 0.0 || dAxial.Hi <= 0.0 with
            | true, _  -> branch1
            | _, true  -> branch2
            | _        -> { Lo = min branch1.Lo branch2.Lo; Hi = max branch1.Hi branch2.Hi }
