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

    type private Guide =
        { Primitive: MathIr.Expr
          X0: float
          Y0: float
          X1: float
          Y1: float
          MinY: float
          MaxY: float
          // Cubic-bezier control points (cx0, cy0, cx1, cy1) when the
          // guide is a spline primitive; `None` for line primitives.
          Control: (float * float * float * float) option }

    /// CPU chord-x at span-y. For splines we approximate t ≈ (y-y0)/(y1-y0)
    /// and evaluate the cubic — accurate when y is roughly monotonic in t,
    /// which the demo guide control points satisfy.
    let private chordXAtY (g: Guide) (y: float) : float =
        let dy = g.Y1 - g.Y0
        if abs dy < 1.0e-9 then g.X0
        else
            let t = (y - g.Y0) / dy
            match g.Control with
            | None ->
                g.X0 + t * (g.X1 - g.X0)
            | Some (cx0, _cy0, cx1, _cy1) ->
                let u = 1.0 - t
                u*u*u * g.X0 + 3.0*u*u*t * cx0 + 3.0*u*t*t * cx1 + t*t*t * g.X1

    let private chordXAtYExpr (ir: MathIr.MathIR) (g: Guide) (y: MathIr.Expr) : MathIr.Expr =
        let dy = g.Y1 - g.Y0
        if abs dy < 1.0e-9 then ir.Constant g.X0
        else
            let inv = 1.0 / dy
            let tExpr =
                ir.Binary(
                    MathIr.Binary.Mul,
                    ir.Binary(MathIr.Binary.Sub, y, ir.Constant g.Y0),
                    ir.Constant inv)
            match g.Control with
            | None ->
                let slope = (g.X1 - g.X0) * inv
                ir.Binary(
                    MathIr.Binary.Add,
                    ir.Constant g.X0,
                    ir.Binary(
                        MathIr.Binary.Mul,
                        ir.Binary(MathIr.Binary.Sub, y, ir.Constant g.Y0),
                        ir.Constant slope))
            | Some (cx0, _cy0, cx1, _cy1) ->
                // Cubic Bernstein basis: u^3 P0 + 3 u^2 t C0 + 3 u t^2 C1 + t^3 P1.
                let one = ir.Constant 1.0
                let three = ir.Constant 3.0
                let uExpr = ir.Binary(MathIr.Binary.Sub, one, tExpr)
                let mul a b = ir.Binary(MathIr.Binary.Mul, a, b)
                let add a b = ir.Binary(MathIr.Binary.Add, a, b)
                let u2 = mul uExpr uExpr
                let u3 = mul u2 uExpr
                let t2 = mul tExpr tExpr
                let t3 = mul t2 tExpr
                let term0 = mul u3 (ir.Constant g.X0)
                let term1 = mul (mul three (mul u2 tExpr)) (ir.Constant cx0)
                let term2 = mul (mul three (mul uExpr t2)) (ir.Constant cx1)
                let term3 = mul t3 (ir.Constant g.X1)
                add term0 (add term1 (add term2 term3))

    let private readScalar (name: string) (key: string) (pv: PrimitiveValue) (span: Span) : Result<float, EvalError> =
        match Map.tryFind key pv.Fields with
        | Some thunk ->
            match thunk.Value with
            | VNumber n -> Ok n
            | other ->
                evalError span (sprintf "@wing_remap_preview: %s.%s is not a scalar (got %A)" name key other)
        | None ->
            evalError span
                (sprintf "@wing_remap_preview: %s primitive is missing field '%s' (expected a line or spline primitive)"
                    name key)

    /// Read an optional scalar — returns `Some` only if the field exists
    /// and is a `VNumber`. Used for spline control-point fields whose
    /// absence indicates a plain line primitive.
    let private tryReadScalar (key: string) (pv: PrimitiveValue) : float option =
        match Map.tryFind key pv.Fields with
        | Some thunk ->
            match thunk.Value with
            | VNumber n -> Some n
            | _ -> None
        | None -> None

    let private guideFromPrimitive
            (ir: MathIr.MathIR)
            (span: Span)
            (name: string)
            (pv: PrimitiveValue) : Result<Guide, EvalError> =
        readScalar name "x0" pv span >>= fun x0 ->
        readScalar name "y0" pv span >>= fun y0 ->
        readScalar name "x1" pv span >>= fun x1 ->
        readScalar name "y1" pv span >>= fun y1 ->
            let control =
                match tryReadScalar "cx0" pv, tryReadScalar "cy0" pv,
                      tryReadScalar "cx1" pv, tryReadScalar "cy1" pv with
                | Some cx0, Some cy0, Some cx1, Some cy1 -> Some (cx0, cy0, cx1, cy1)
                | _ -> None
            let p0x = ir.Constant x0
            let p0y = ir.Constant y0
            let p1x = ir.Constant x1
            let p1y = ir.Constant y1
            let primitive =
                match control with
                | None ->
                    ir.LineSegmentN(MathIr.Plane.XY, p0x, p0y, p1x, p1y)
                | Some (cx0, cy0, cx1, cy1) ->
                    ir.BezierCubicN(MathIr.Plane.XY,
                                    p0x, p0y,
                                    ir.Constant cx0, ir.Constant cy0,
                                    ir.Constant cx1, ir.Constant cy1,
                                    p1x, p1y)
            Ok
                { Primitive = primitive
                  X0 = x0; Y0 = y0; X1 = x1; Y1 = y1
                  MinY = min y0 y1
                  MaxY = max y0 y1
                  Control = control }

    let private wingRemapPreviewImpl
            (ctx: EvalContext)
            (span: Span)
            (profile: MathIr.Expr)
            (profileChord: float)
            (profileOriginX: float)
            (profileOriginY: float)
            (profileOriginZ: float)
            (leading: PrimitiveValue)
            (trailing: PrimitiveValue) : Result<MathIr.Expr, EvalError> =
        let ir = ctx.Ir
        guideFromPrimitive ir span "leading" leading >>= fun le ->
        guideFromPrimitive ir span "trailing" trailing >>= fun te ->
            // Two-body coordinate from the nTop variable-loft recipe.
            // -(A+B)/(B-A) where A = leading - x, B = trailing - x maps
            // leading edge → -1, mid-chord → 0, trailing edge → +1.
            let y = ir.Y()
            let leadingX = chordXAtYExpr ir le y
            let trailingX = chordXAtYExpr ir te y
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
                max (abs (chordXAtY te rootY - chordXAtY le rootY)) 1.0e-6
            let localChord =
                ir.Binary(
                    MathIr.Binary.Max,
                    ir.Unary(MathIr.Unary.Abs, clearance),
                    ir.Constant 1.0e-6)
            let chordScale =
                ir.Binary(MathIr.Binary.Div, localChord, ir.Constant rootChord)
            let scaledZ = ir.Binary(MathIr.Binary.Div, ir.Z(), chordScale)

            // Build new axes that, when substituted into the input profile
            // via `RemapAxes`, undo the profile's own placement and feed it
            // canonical (sx, sz) coords derived from the wing pipeline.
            //
            // The NACA block computes internally:
            //   sx = (X - ox) * 2 / chord
            //   sz = (Z - oz) * 2 / chord
            // We want sx → twoBody, sz → scaledZ after substitution. So:
            //   newX = ox + twoBody  * (chord / 2)
            //   newZ = oz + scaledZ * (chord / 2)
            //
            // For Y we feed the profile's own origin_y as a constant so its
            // internal `|Y - oy|` span window collapses to 0 (always
            // "inside"). The wing's own span window from the guides handles
            // the Y bound for the final wing.
            let halfChord = profileChord * 0.5
            let newX =
                ir.Binary(
                    MathIr.Binary.Add,
                    ir.Constant profileOriginX,
                    ir.Binary(MathIr.Binary.Mul, twoBody, ir.Constant halfChord))
            let newY = ir.Constant profileOriginY
            let newZ =
                ir.Binary(
                    MathIr.Binary.Add,
                    ir.Constant profileOriginZ,
                    ir.Binary(MathIr.Binary.Mul, scaledZ, ir.Constant halfChord))
            let remappedProfile = ir.RemapAxes(profile, newX, newY, newZ)

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

    // -- naca --------------------------------------------------------------
    //
    // Canonical NACA-style airfoil profile expressed in world XZ coords,
    // centred on (`origin_x`, `origin_y`, `origin_z`) with chord length
    // `chord` (X extent), thickness ratio `thickness`, max camber
    // `camber`, and Y span `span` for the bounded extrusion. Same shape
    // family that the inline NACA inside `wing_remap_preview` uses;
    // factored here so the user can position / size a standalone airfoil
    // block (e.g. for blueprint overlay) without touching the wing-remap
    // pipeline.
    let private nacaProfileExpr
            (ir: MathIr.MathIR)
            (thickness: float)
            (camber: float)
            (chord: float)
            (span: float)
            (originX: float)
            (originY: float)
            (originZ: float) : MathIr.Expr =
        let halfChord = max (chord * 0.5) 1.0e-6
        let invHalfChord = 1.0 / halfChord
        // Map placed world (X, Z) → canonical (sx, sz) with chord ∈ [-1, 1].
        // Y is the extruded direction — the profile is independent of Y
        // and gets bounded below by `spanWindow`.
        let sx =
            ir.Binary(MathIr.Binary.Mul,
                ir.Binary(MathIr.Binary.Sub, ir.X(), ir.Constant originX),
                ir.Constant invHalfChord)
        let sz =
            ir.Binary(MathIr.Binary.Mul,
                ir.Binary(MathIr.Binary.Sub, ir.Z(), ir.Constant originZ),
                ir.Constant invHalfChord)
        let one = ir.Constant 1.0
        let zero = ir.Constant 0.0
        let cRaw =
            ir.Binary(MathIr.Binary.Mul,
                ir.Binary(MathIr.Binary.Add, sx, one),
                ir.Constant 0.5)
        let c =
            ir.Binary(MathIr.Binary.Min,
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
            ir.Binary(MathIr.Binary.Add,
                ir.Binary(MathIr.Binary.Add, term0, term1),
                ir.Binary(MathIr.Binary.Add,
                    ir.Binary(MathIr.Binary.Add, term2, term3), term4))
        let thicknessExpr =
            ir.Binary(MathIr.Binary.Mul,
                nacaThicknessShape,
                ir.Constant (5.0 * thickness * 2.0))
        let camberShape =
            ir.Binary(MathIr.Binary.Sub, one, ir.Unary(MathIr.Unary.Square, sx))
        let camberExpr =
            ir.Binary(MathIr.Binary.Mul, ir.Constant camber, camberShape)
        let chordWindow =
            ir.Binary(MathIr.Binary.Sub, ir.Unary(MathIr.Unary.Abs, sx), one)
        let heightWindow =
            ir.Binary(MathIr.Binary.Sub,
                ir.Unary(MathIr.Unary.Abs, ir.Binary(MathIr.Binary.Sub, sz, camberExpr)),
                thicknessExpr)
        let profile = ir.Binary(MathIr.Binary.Max, chordWindow, heightWindow)
        let halfSpan = max (span * 0.5) 1.0e-6
        let spanWindow =
            ir.Binary(MathIr.Binary.Sub,
                ir.Unary(MathIr.Unary.Abs, ir.Binary(MathIr.Binary.Sub, ir.Y(), ir.Constant originY)),
                ir.Constant halfSpan)
        ir.Binary(MathIr.Binary.Max, profile, spanWindow)

    let private readNumberArg
            (errPrefix: string)
            (span: Span)
            (pos: Value list)
            (named: Map<string, Value>)
            (idx: int)
            (name: string) : Result<float, EvalError> =
        requireArg span pos named idx name >>= fun v ->
            match v with
            | VNumber n -> Ok n
            | other ->
                evalError span
                    (sprintf "%s: argument '%s' must be a scalar (got %A)" errPrefix name other)

    let private nacaCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        let read = readNumberArg "@naca" span pos named
        read 0 "thickness" >>= fun thickness ->
        read 1 "camber" >>= fun camber ->
        read 2 "chord" >>= fun chord ->
        read 3 "span" >>= fun spanLen ->
        read 4 "origin_x" >>= fun ox ->
        read 5 "origin_y" >>= fun oy ->
        read 6 "origin_z" >>= fun oz ->
            Ok (VField (nacaProfileExpr ctx.Ir thickness camber chord spanLen ox oy oz))

    let private wingRemapPreviewCall (ctx: EvalContext) (span: Span) (args: ArgValue list) =
        let pos = positionals args
        let named = namedMap args
        let read = readNumberArg "@wing_remap_preview" span pos named
        requireArg span pos named 0 "profile" >>= fun pv ->
        read 1 "profile_chord" >>= fun pc ->
        read 2 "profile_origin_x" >>= fun pox ->
        read 3 "profile_origin_y" >>= fun poy ->
        read 4 "profile_origin_z" >>= fun poz ->
        requireArg span pos named 5 "leading" >>= fun lv ->
        requireArg span pos named 6 "trailing" >>= fun tv ->
            match pv, lv, tv with
            | VField profile, VPrimitive leading, VPrimitive trailing ->
                wingRemapPreviewImpl ctx span profile pc pox poy poz leading trailing
                |> Result.map VField
            | _ ->
                evalError span
                    "@wing_remap_preview: profile must be a Field; leading/trailing must be line primitives"

    /// Dispatch table for `@name(...)` calls (excluding the input/output/view
    /// specials, which the evaluator routes directly to `ctx.Specials`).
    let dispatch
            (ctx: EvalContext)
            (name: string)
            (args: ArgValue list)
            (span: Span) : Result<Value, EvalError> =
        match name with
        | "wing_remap_preview" -> wingRemapPreviewCall ctx span args
        | "naca" -> nacaCall ctx span args
        | _ -> evalError span (sprintf "unknown builtin @%s" name)

    /// Positional arities for the curried `@name` form. `print` / `debug`
    /// are routed by the evaluator through `ctx.Specials`; all other names
    /// go through `dispatch` once saturated.
    let arityOf (name: string) : int option =
        match name with
        | "wing_remap_preview" -> Some 7
        | "naca" -> Some 7
        | "print" -> Some 2
        | "debug" -> Some 1
        | _ -> None

    /// Names of the specials (handled by `ctx.Specials`, not `dispatch`).
    let isSpecial (name: string) : bool =
        match name with
        | "print" | "debug" -> true
        | _ -> false
