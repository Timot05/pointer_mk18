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
        | "print" -> Some 2
        | "debug" -> Some 1
        | _ -> None

    /// Names of the specials (handled by `ctx.Specials`, not `dispatch`).
    let isSpecial (name: string) : bool =
        match name with
        | "print" | "debug" -> true
        | _ -> false
