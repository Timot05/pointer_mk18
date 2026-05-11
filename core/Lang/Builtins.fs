namespace Server.Lang

// ---------------------------------------------------------------------------
// Builtins.fs — small subset of pointer_mk19/lib/field_builder.ml.
//
// Each `@name(...)` call from the DSL routes through `dispatch`. Builders
// produce MathIr expressions directly (mk19's field_builder produces a
// Field_ir tree which then lowers to math_ir; we skip the intermediate).
//
// First-cut catalog: sphere, box, cylinder, translate, union, subtract,
// intersect, thicken. Enough to evaluate small demo programs end-to-end.
// `rotate` is wired but errors at call time pending a clean affine builder.
// ---------------------------------------------------------------------------

module Builtins =

    open Token
    open Value

    type ArgValue =
        | APos of Value
        | ANamed of string * Value

    let private positionals (args: ArgValue list) : Value list =
        args |> List.choose (function APos v -> Some v | _ -> None)

    let private namedMap (args: ArgValue list) : Map<string, Value> =
        args
        |> List.choose (function ANamed(n, v) -> Some(n, v) | _ -> None)
        |> Map.ofList

    /// Positional first, fall back to a named arg of the same role.
    let private pickArg (pos: Value list) (named: Map<string, Value>) (idx: int) (name: string) : Value option =
        if idx < List.length pos then Some pos.[idx]
        else Map.tryFind name named

    let private requireArg (span: Span) (pos: Value list) (named: Map<string, Value>) (idx: int) (name: string) : Result<Value, EvalError> =
        match pickArg pos named idx name with
        | Some v -> Ok v
        | None -> evalError span (sprintf "missing argument '%s'" name)

    /// Coerce a Value into a MathIr.Expr. Numbers become Const nodes;
    /// existing fields pass through. Anything else errors.
    let private toExprArg (ir: MathIr.MathIR) (span: Span) (v: Value) : Result<MathIr.Expr, EvalError> =
        match v with
        | VField e -> Ok e
        | VNumber n -> Ok (ir.Constant n)
        | _ -> evalError span "expected number or field"

    let private toNumberArg (span: Span) (v: Value) : Result<float, EvalError> =
        match v with
        | VNumber n -> Ok n
        | _ -> evalError span "expected number"

    // -- Geometric primitives ----------------------------------------------------

    let private sphereImpl (ctx: EvalContext) (span: Span) (radius: MathIr.Expr) : MathIr.Expr =
        let ir = ctx.Ir
        let x = ir.X()
        let y = ir.Y()
        let z = ir.Z()
        let xx = ir.Unary(MathIr.Unary.Square, x)
        let yy = ir.Unary(MathIr.Unary.Square, y)
        let zz = ir.Unary(MathIr.Unary.Square, z)
        let xy = ir.Binary(MathIr.Binary.Add, xx, yy)
        let sum = ir.Binary(MathIr.Binary.Add, xy, zz)
        let mag = ir.Unary(MathIr.Unary.Sqrt, sum)
        ir.Binary(MathIr.Binary.Sub, mag, radius)

    /// Box SDF: outside = ||max(|p| - half, 0)||;
    ///          inside  = min(max(bx, max(by, bz)), 0)
    let private boxImpl (ctx: EvalContext) (hx: MathIr.Expr) (hy: MathIr.Expr) (hz: MathIr.Expr) : MathIr.Expr =
        let ir = ctx.Ir
        let zero = ir.Constant 0.0
        let bx = ir.Binary(MathIr.Binary.Sub, ir.Unary(MathIr.Unary.Abs, ir.X()), hx)
        let by = ir.Binary(MathIr.Binary.Sub, ir.Unary(MathIr.Unary.Abs, ir.Y()), hy)
        let bz = ir.Binary(MathIr.Binary.Sub, ir.Unary(MathIr.Unary.Abs, ir.Z()), hz)
        // outside
        let cx = ir.Binary(MathIr.Binary.Max, bx, zero)
        let cy = ir.Binary(MathIr.Binary.Max, by, zero)
        let cz = ir.Binary(MathIr.Binary.Max, bz, zero)
        let cxx = ir.Unary(MathIr.Unary.Square, cx)
        let cyy = ir.Unary(MathIr.Unary.Square, cy)
        let czz = ir.Unary(MathIr.Unary.Square, cz)
        let cxy = ir.Binary(MathIr.Binary.Add, cxx, cyy)
        let csum = ir.Binary(MathIr.Binary.Add, cxy, czz)
        let outside = ir.Unary(MathIr.Unary.Sqrt, csum)
        // inside (fully negative when all bx, by, bz are negative)
        let mxy = ir.Binary(MathIr.Binary.Max, by, bz)
        let mxyz = ir.Binary(MathIr.Binary.Max, bx, mxy)
        let inside = ir.Binary(MathIr.Binary.Min, mxyz, zero)
        ir.Binary(MathIr.Binary.Add, outside, inside)

    /// Cylinder along Y axis: max(|x,z|-r, |y|-h/2)
    let private cylinderImpl (ctx: EvalContext) (radius: MathIr.Expr) (height: MathIr.Expr) : MathIr.Expr =
        let ir = ctx.Ir
        let xx = ir.Unary(MathIr.Unary.Square, ir.X())
        let zz = ir.Unary(MathIr.Unary.Square, ir.Z())
        let xz = ir.Binary(MathIr.Binary.Add, xx, zz)
        let radial = ir.Binary(MathIr.Binary.Sub, ir.Unary(MathIr.Unary.Sqrt, xz), radius)
        let two = ir.Constant 2.0
        let halfH = ir.Binary(MathIr.Binary.Div, height, two)
        let axial = ir.Binary(MathIr.Binary.Sub, ir.Unary(MathIr.Unary.Abs, ir.Y()), halfH)
        ir.Binary(MathIr.Binary.Max, radial, axial)

    let private translateImpl (ctx: EvalContext) (tx: MathIr.Expr) (ty: MathIr.Expr) (tz: MathIr.Expr) (target: MathIr.Expr) : MathIr.Expr =
        let ir = ctx.Ir
        let xMinus = ir.Binary(MathIr.Binary.Sub, ir.X(), tx)
        let yMinus = ir.Binary(MathIr.Binary.Sub, ir.Y(), ty)
        let zMinus = ir.Binary(MathIr.Binary.Sub, ir.Z(), tz)
        ir.RemapAxes(target, xMinus, yMinus, zMinus)

    let private unionImpl (ctx: EvalContext) (a: MathIr.Expr) (b: MathIr.Expr) : MathIr.Expr =
        ctx.Ir.Binary(MathIr.Binary.Min, a, b)

    let private intersectImpl (ctx: EvalContext) (a: MathIr.Expr) (b: MathIr.Expr) : MathIr.Expr =
        ctx.Ir.Binary(MathIr.Binary.Max, a, b)

    let private subtractImpl (ctx: EvalContext) (a: MathIr.Expr) (b: MathIr.Expr) : MathIr.Expr =
        let ir = ctx.Ir
        let negB = ir.Unary(MathIr.Unary.Neg, b)
        ir.Binary(MathIr.Binary.Max, a, negB)

    /// Shifts the iso-surface outward by `amount` (the iso level rises so
    /// where sdf == amount becomes the new surface).
    let private thickenImpl (ctx: EvalContext) (amount: MathIr.Expr) (target: MathIr.Expr) : MathIr.Expr =
        ctx.Ir.Binary(MathIr.Binary.Sub, target, amount)

    // -- Dispatch ----------------------------------------------------------------

    let private sphereCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        requireArg span pos named 0 "radius" >>= fun rv ->
            toExprArg ctx.Ir span rv >>= fun r ->
                Ok (VField (sphereImpl ctx span r))

    let private boxCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        requireArg span pos named 0 "width" >>= fun wv ->
        requireArg span pos named 1 "height" >>= fun hv ->
        requireArg span pos named 2 "depth" >>= fun dv ->
            toExprArg ctx.Ir span wv >>= fun w ->
            toExprArg ctx.Ir span hv >>= fun h ->
            toExprArg ctx.Ir span dv >>= fun d ->
                let two = ctx.Ir.Constant 2.0
                let hx = ctx.Ir.Binary(MathIr.Binary.Div, w, two)
                let hy = ctx.Ir.Binary(MathIr.Binary.Div, h, two)
                let hz = ctx.Ir.Binary(MathIr.Binary.Div, d, two)
                Ok (VField (boxImpl ctx hx hy hz))

    let private cylinderCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        requireArg span pos named 0 "radius" >>= fun rv ->
        requireArg span pos named 1 "height" >>= fun hv ->
            toExprArg ctx.Ir span rv >>= fun r ->
            toExprArg ctx.Ir span hv >>= fun h ->
                Ok (VField (cylinderImpl ctx r h))

    let private translateCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        requireArg span pos named 0 "x" >>= fun xv ->
        requireArg span pos named 1 "y" >>= fun yv ->
        requireArg span pos named 2 "z" >>= fun zv ->
        requireArg span pos named 3 "field" >>= fun fv ->
            toExprArg ctx.Ir span xv >>= fun tx ->
            toExprArg ctx.Ir span yv >>= fun ty ->
            toExprArg ctx.Ir span zv >>= fun tz ->
            (match fv with
             | VField e -> Ok e
             | _ -> evalError span "translate: 4th argument must be a field") >>= fun target ->
                Ok (VField (translateImpl ctx tx ty tz target))

    let private booleanCall name impl (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        requireArg span pos named 0 "a" >>= fun av ->
        requireArg span pos named 1 "b" >>= fun bv ->
            toExprArg ctx.Ir span av >>= fun a ->
            toExprArg ctx.Ir span bv >>= fun b ->
                Ok (VField (impl ctx a b))

    let private thickenCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        requireArg span pos named 0 "amount" >>= fun av ->
        requireArg span pos named 1 "field" >>= fun fv ->
            toExprArg ctx.Ir span av >>= fun amount ->
            (match fv with
             | VField e -> Ok e
             | _ -> evalError span "thicken: 2nd argument must be a field") >>= fun target ->
                Ok (VField (thickenImpl ctx amount target))

    let private rotateCall (ctx: EvalContext) (span: Span) (_args: ArgValue list) =
        evalError span "@rotate is not yet implemented in this round"

    // -- from_sketch -------------------------------------------------------------
    //
    // Lower a `VSketch` to a `VField` by emitting one MathIR primitive per
    // line/circle entity (point coords backed by Const nodes — the kernel
    // reads them via `nodeValue`/`nodePoint2`) and wrapping with a
    // SketchPath intrinsic. Arcs and beziers are skipped this round; the
    // kernel's `evalSketchDistance` doesn't support arcs, so emitting them
    // would just NaN-pollute the field.

    let private planeMap (p: Server.SketchPlane) : MathIr.Plane =
        match p with
        | Server.XY -> MathIr.Plane.XY
        | Server.XZ -> MathIr.Plane.XZ
        | Server.YZ -> MathIr.Plane.YZ

    let private fromSketchImpl
            (ctx: EvalContext)
            (span: Span)
            (sv: SketchValue) : Result<MathIr.Expr, EvalError> =
        let ir = ctx.Ir
        // Resolve point ids → (x_node_id, y_node_id). Each REPoint becomes
        // two Const nodes; the SlotPoint2 holds those node ids. The
        // kernel-side `nodePoint2` reads `ir.nodes[id].value`.
        let pointTable =
            sv.Sketch.Entities
            |> List.choose (function
                | Server.REPoint(id, x, y) ->
                    let pt = ir.Point2((ir.Constant x).Id, (ir.Constant y).Id)
                    Some (id, pt)
                | _ -> None)
            |> Map.ofList

        // First primitive id is allocated as we push them; remember the
        // start so the SketchPath intrinsic can range-encode them.
        let primitiveStart = ir.Primitives.Count

        let mutable count = 0
        for entity in sv.Sketch.Entities do
            match entity with
            | Server.RELine(_, startId, endId) ->
                match Map.tryFind startId pointTable, Map.tryFind endId pointTable with
                | Some s, Some e ->
                    ir.LineSegment(s, e) |> ignore
                    count <- count + 1
                | _ -> ()
            | Server.RECircle(_, centerId, radius) ->
                match Map.tryFind centerId pointTable with
                | Some c ->
                    let rExpr = ir.Constant radius
                    ir.Circle(c, rExpr.Id) |> ignore
                    count <- count + 1
                | None -> ()
            | _ -> ()

        if count = 0 then
            evalError span "@from_sketch: sketch produced no renderable primitives"
        else
            // Emit a closed SketchPath spanning the just-pushed range. The
            // kernel resolves "inside" by sampling winding angle; closed
            // ⇒ negative inside.
            Ok (ir.SketchPath(planeMap sv.Plane, primitiveStart, count, true, false))

    let private fromSketchCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        requireArg span pos named 0 "sketch" >>= fun sv ->
            match sv with
            | VSketch s ->
                fromSketchImpl ctx span s
                |> Result.map VField
            | _ -> evalError span "@from_sketch: argument must be a sketch"

    // -- wing_remap_preview ------------------------------------------------------
    //
    // Experimental wing parameterization probe. It consumes two XY sketch
    // blocks, extracts the first line from each as leading/trailing guide
    // curves, emits CurveDistanceAlong for both, and remaps a unit
    // chord/span strip through those coordinate fields. The result is not a
    // closed solid; it is an inspectable field for validating the remap path.

    type private LineGuide =
        { Primitive: int
          X0: float
          Y0: float
          X1: float
          Y1: float
          MinY: float
          MaxY: float }

    let private lineXAtY (g: LineGuide) (y: float) : float =
        let dy = g.Y1 - g.Y0
        if abs dy < 1.0e-9 then g.X0
        else
            let t = (y - g.Y0) / dy
            g.X0 + t * (g.X1 - g.X0)

    let private lineXAtYExpr (ir: MathIr.MathIR) (g: LineGuide) (y: MathIr.Expr) : MathIr.Expr =
        let dy = g.Y1 - g.Y0
        if abs dy < 1.0e-9 then ir.Constant g.X0
        else
            let slope = (g.X1 - g.X0) / dy
            ir.Binary(
                MathIr.Binary.Add,
                ir.Constant g.X0,
                ir.Binary(
                    MathIr.Binary.Mul,
                    ir.Binary(MathIr.Binary.Sub, y, ir.Constant g.Y0),
                    ir.Constant slope))

    let private tryPointCoords (sketch: Server.ActionSketch) pointId =
        sketch.Entities
        |> List.tryPick (function
            | Server.REPoint(id, x, y) when id = pointId -> Some(x, y)
            | _ -> None)

    let private emitFirstLineGuide
            (ir: MathIr.MathIR)
            (span: Span)
            (name: string)
            (sv: SketchValue) : Result<LineGuide, EvalError> =
        match sv.Plane with
        | Server.XY ->
            match sv.Sketch.Entities |> List.tryPick (function Server.RELine(_, a, b) -> Some(a, b) | _ -> None) with
            | None -> evalError span (sprintf "@wing_remap_preview: %s sketch must contain a line" name)
            | Some(a, b) ->
                match tryPointCoords sv.Sketch a, tryPointCoords sv.Sketch b with
                | Some(x0, y0), Some(x1, y1) ->
                    let p0 = ir.Point2((ir.Constant x0).Id, (ir.Constant y0).Id)
                    let p1 = ir.Point2((ir.Constant x1).Id, (ir.Constant y1).Id)
                    Ok
                        { Primitive = ir.LineSegment(p0, p1)
                          X0 = x0
                          Y0 = y0
                          X1 = x1
                          Y1 = y1
                          MinY = min y0 y1
                          MaxY = max y0 y1 }
                | _ -> evalError span (sprintf "@wing_remap_preview: %s line endpoints are missing" name)
        | _ -> evalError span "@wing_remap_preview: only XY sketches are supported for now"

    let private wingRemapPreviewImpl
            (ctx: EvalContext)
            (span: Span)
            (leading: SketchValue)
            (trailing: SketchValue) : Result<MathIr.Expr, EvalError> =
        let ir = ctx.Ir
        emitFirstLineGuide ir span "leading" leading >>= fun le ->
        emitFirstLineGuide ir span "trailing" trailing >>= fun te ->
            let axisX = ir.Constant 1.0
            let axisY = ir.Constant 0.0
            let axisZ = ir.Constant 0.0
            let dLeading =
                ir.CurveDistanceAlong(MathIr.Plane.XY, le.Primitive, 1, axisX, axisY, axisZ, false)
            let dTrailing =
                ir.CurveDistanceAlong(MathIr.Plane.XY, te.Primitive, 1, axisX, axisY, axisZ, false)
            // Two-body coordinate from the nTop variable-loft recipe. With
            // our CurveDistanceAlong sign convention (`curve_x - sample_x`),
            // A = leading - x and B = trailing - x. `-(A+B)/(B-A)` maps
            // leading edge → -1, mid-chord → 0, trailing edge → +1.
            let y = ir.Y()
            let leadingX = lineXAtYExpr ir le y
            let trailingX = lineXAtYExpr ir te y
            let clearance = ir.Binary(MathIr.Binary.Sub, trailingX, leadingX)
            let midsurface =
                ir.Binary(
                    MathIr.Binary.Sub,
                    ir.Binary(MathIr.Binary.Add, leadingX, trailingX),
                    ir.Binary(MathIr.Binary.Mul, ir.Constant 2.0, ir.X()))
            let twoBody =
                ir.Unary(
                    MathIr.Unary.Neg,
                    ir.Binary(MathIr.Binary.Div, midsurface, clearance))

            let rootY = max le.MinY te.MinY
            let rootChord =
                max (abs (lineXAtY te rootY - lineXAtY le rootY)) 1.0e-6
            let localChord =
                ir.Binary(
                    MathIr.Binary.Max,
                    ir.Unary(MathIr.Unary.Abs, clearance),
                    ir.Constant 1.0e-6)
            let chordScale =
                ir.Binary(MathIr.Binary.Div, localChord, ir.Constant rootChord)
            let scaledZ = ir.Binary(MathIr.Binary.Div, ir.Z(), chordScale)

            // Canonical NACA-style source profile in XZ, with x in [-1, 1]
            // and z as thickness height. Remapping by `twoBody` handles
            // chord taper and sweep; remapping z by local/root chord keeps
            // thickness ratio as the chord changes along the span.
            let sx = ir.X()
            let sz = ir.Z()
            let one = ir.Constant 1.0
            let zero = ir.Constant 0.0
            let cRaw =
                ir.Binary(
                    MathIr.Binary.Mul,
                    ir.Binary(MathIr.Binary.Add, sx, one),
                    ir.Constant 0.5)
            let c =
                ir.Binary(
                    MathIr.Binary.Min,
                    ir.Binary(MathIr.Binary.Max, cRaw, zero),
                    one)
            let c2 = ir.Unary(MathIr.Unary.Square, c)
            let c3 = ir.Binary(MathIr.Binary.Mul, c2, c)
            let c4 = ir.Unary(MathIr.Unary.Square, c2)
            let nacaThicknessShape =
                let term0 = ir.Binary(MathIr.Binary.Mul, ir.Constant 0.2969, ir.Unary(MathIr.Unary.Sqrt, c))
                let term1 = ir.Binary(MathIr.Binary.Mul, ir.Constant -0.1260, c)
                let term2 = ir.Binary(MathIr.Binary.Mul, ir.Constant -0.3516, c2)
                let term3 = ir.Binary(MathIr.Binary.Mul, ir.Constant 0.2843, c3)
                let term4 = ir.Binary(MathIr.Binary.Mul, ir.Constant -0.1015, c4)
                ir.Binary(
                    MathIr.Binary.Add,
                    ir.Binary(MathIr.Binary.Add, term0, term1),
                    ir.Binary(
                        MathIr.Binary.Add,
                        ir.Binary(MathIr.Binary.Add, term2, term3),
                        term4))
            let thickness =
                ir.Binary(
                    MathIr.Binary.Mul,
                    nacaThicknessShape,
                    ir.Constant (5.0 * 0.18 * 2.0))
            let camberShape =
                ir.Binary(
                    MathIr.Binary.Sub,
                    one,
                    ir.Unary(MathIr.Unary.Square, sx))
            let camber =
                ir.Binary(MathIr.Binary.Mul, ir.Constant 0.04, camberShape)
            let chordWindow =
                ir.Binary(MathIr.Binary.Sub, ir.Unary(MathIr.Unary.Abs, sx), one)
            let heightWindow =
                ir.Binary(
                    MathIr.Binary.Sub,
                    ir.Unary(MathIr.Unary.Abs, ir.Binary(MathIr.Binary.Sub, sz, camber)),
                    thickness)
            let sourceProfile = ir.Binary(MathIr.Binary.Max, chordWindow, heightWindow)
            let remappedProfile = ir.RemapAxes(sourceProfile, twoBody, ir.Y(), scaledZ)

            let minY = max le.MinY te.MinY
            let maxY = min le.MaxY te.MaxY
            let centerY = (minY + maxY) * 0.5
            let halfY = max ((maxY - minY) * 0.5) 1.0e-6
            let spanWindow =
                ir.Binary(
                    MathIr.Binary.Sub,
                    ir.Unary(MathIr.Unary.Abs, ir.Binary(MathIr.Binary.Sub, ir.Y(), ir.Constant centerY)),
                    ir.Constant halfY)

            Ok (ir.Binary(MathIr.Binary.Max, remappedProfile, spanWindow))

    let private wingRemapPreviewCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        requireArg span pos named 0 "leading" >>= fun lv ->
        requireArg span pos named 1 "trailing" >>= fun tv ->
            match lv, tv with
            | VSketch leading, VSketch trailing ->
                wingRemapPreviewImpl ctx span leading trailing
                |> Result.map VField
            | _ -> evalError span "@wing_remap_preview: arguments must be sketches"

    /// Dispatch table for `@name(...)` calls (excluding the input/output/view
    /// specials, which the evaluator routes directly to `ctx.Specials`).
    let dispatch
            (ctx: EvalContext)
            (name: string)
            (args: ArgValue list)
            (span: Span) : Result<Value, EvalError> =
        match name with
        | "sphere" -> sphereCall ctx span args
        | "box" -> boxCall ctx span args
        | "cylinder" -> cylinderCall ctx span args
        | "translate" -> translateCall ctx span args
        | "union" -> booleanCall "union" unionImpl ctx span args
        | "subtract" -> booleanCall "subtract" subtractImpl ctx span args
        | "intersect" -> booleanCall "intersect" intersectImpl ctx span args
        | "thicken" -> thickenCall ctx span args
        | "rotate" -> rotateCall ctx span args
        | "from_sketch" -> fromSketchCall ctx span args
        | "wing_remap_preview" -> wingRemapPreviewCall ctx span args
        | _ -> evalError span (sprintf "unknown builtin @%s" name)

    /// Positional arities for the curried `@name` form. `print` / `debug`
    /// are routed by the evaluator through `ctx.Specials`; all other names
    /// go through `dispatch` once saturated. `view` / `input` / `output`
    /// were retired in favour of `let import` / `let pub` declarations.
    let arityOf (name: string) : int option =
        match name with
        | "sphere" -> Some 1
        | "box" -> Some 3
        | "cylinder" -> Some 2
        | "translate" -> Some 4
        | "union" | "subtract" | "intersect" -> Some 2
        | "thicken" -> Some 2
        | "rotate" -> Some 4
        | "from_sketch" -> Some 1
        | "wing_remap_preview" -> Some 2
        | "print" -> Some 2
        | "debug" -> Some 1
        | _ -> None

    /// Names of the specials (handled by `ctx.Specials`, not `dispatch`).
    let isSpecial (name: string) : bool =
        match name with
        | "print" | "debug" -> true
        | _ -> false
