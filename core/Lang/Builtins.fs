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

    // Geometric primitives — sphere/box/cylinder/translate/union/subtract/
    // intersect/thicken/rotate used to be dispatched via `@`-prefix names
    // through `Builtins.dispatch`. They were retired in Phase 7: every
    // primitive's body is now expressed directly in `BlockSpec.fs` as pure
    // AST (lambdas over `EAxis` / `EBinary` / `ERemapAxes`), so there's no
    // production caller for `@sphere` etc. The only remaining intrinsic in
    // this file is `wing_remap_preview`, which is kept because it walks a
    // `VSketch` payload to emit a `CurveDistanceAlong` intrinsic — work
    // that's not expressible in pure AST today.

    // -- wing_remap_preview ------------------------------------------------------
    //
    // Experimental wing parameterization probe. It consumes two XY sketch
    // blocks, extracts the first line from each as leading/trailing guide
    // curves, emits CurveDistanceAlong for both, and remaps a unit
    // chord/span strip through those coordinate fields. The result is not a
    // closed solid; it is an inspectable field for validating the remap path.

    type private LineGuide =
        { Primitive: MathIr.Expr
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
                    let p0x = ir.Constant x0
                    let p0y = ir.Constant y0
                    let p1x = ir.Constant x1
                    let p1y = ir.Constant y1
                    Ok
                        { Primitive = ir.LineSegmentN(MathIr.Plane.XY, p0x, p0y, p1x, p1y)
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
                ir.CurveDistanceAlong(MathIr.Plane.XY, [ le.Primitive ], axisX, axisY, axisZ, false)
            let dTrailing =
                ir.CurveDistanceAlong(MathIr.Plane.XY, [ te.Primitive ], axisX, axisY, axisZ, false)
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
        | "wing_remap_preview" -> wingRemapPreviewCall ctx span args
        | _ -> evalError span (sprintf "unknown builtin @%s" name)

    /// Positional arities for the curried `@name` form. `print` / `debug`
    /// are routed by the evaluator through `ctx.Specials`; all other names
    /// go through `dispatch` once saturated.
    let arityOf (name: string) : int option =
        match name with
        | "wing_remap_preview" -> Some 2
        | "print" -> Some 2
        | "debug" -> Some 1
        | _ -> None

    /// Names of the specials (handled by `ctx.Specials`, not `dispatch`).
    let isSpecial (name: string) : bool =
        match name with
        | "print" | "debug" -> true
        | _ -> false
