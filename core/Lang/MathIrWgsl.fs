namespace Server.Lang

// ---------------------------------------------------------------------------
// MathIrWgsl.fs — emit a WGSL function that evaluates a MathIR expression.
// Used by the field-slice overlay to evaluate per-block SDFs directly on
// the GPU, mirroring the kernel's `decodeRegEvalF32` semantics.
//
// Shape of the emitted source: one `fn eval_<nodeId>(p: vec3<f32>) -> f32`
// per "function-entry" node (the root, plus every RemapAxes/RemapAffine
// child target). Inside a function body the DAG reachable from its entry
// is emitted as `let n_<id>: f32 = <expr>;` lines in post-order; shared
// subexpressions naturally collapse to one binding via a visited set.
//
// RemapAxes/RemapAffine cross function boundaries: at the call site we
// build the new (x, y, z) and call `eval_<targetId>(vec3<f32>(...))`.
// That target's function body is emitted recursively, so deeper sub-
// functions appear before their callers in the final WGSL — WGSL requires
// definitions in order of first use.
//
// Intrinsic support is intentionally narrow: field-line preview currently
// needs `CurveDistanceAlong` for line-segment guide curves. Other intrinsic
// shapes emit a `1.0e10` sentinel until their evaluators are ported.
//
// The caller must provide a `slots: Slots` storage binding with shape
// `struct Slots { v: array<f32> }`. Slot reads emit `slots.v[<id>]`.
// ---------------------------------------------------------------------------

module MathIrWgsl =

    open MathIr
    open System.Text
    open System.Collections.Generic

    /// MathIR axis numbering — must match the `Axis` enum: 0 = X, 1 = Y, 2 = Z.
    let private axisExpr (op: int) : string =
        match op with
        | 0 -> "p.x"
        | 1 -> "p.y"
        | _ -> "p.z"

    /// Order matches `MathIr.Unary` (and `kernel/.../math_eval.zig:evalUnaryPoint`).
    /// WGSL has no `recip`, `square`, or `not` — synthesise them.
    let private unaryExpr (op: int) (a: string) : string =
        match op with
        | 0  -> sprintf "(-%s)" a                       // neg
        | 1  -> sprintf "abs(%s)" a                     // abs
        | 2  -> sprintf "(1.0 / %s)" a                  // recip
        | 3  -> sprintf "(%s * %s)" a a                 // square
        | 4  -> sprintf "sqrt(%s)" a                    // sqrt
        | 5  -> sprintf "floor(%s)" a                   // floor
        | 6  -> sprintf "ceil(%s)" a                    // ceil
        | 7  -> sprintf "floor(%s + 0.5)" a             // round
        | 8  -> sprintf "sin(%s)" a                     // sin
        | 9  -> sprintf "cos(%s)" a                     // cos
        | 10 -> sprintf "tan(%s)" a                     // tan
        | 11 -> sprintf "asin(%s)" a                    // asin
        | 12 -> sprintf "acos(%s)" a                    // acos
        | 13 -> sprintf "atan(%s)" a                    // atan
        | 14 -> sprintf "exp(%s)" a                     // exp
        | 15 -> sprintf "log(%s)" a                     // ln
        | 16 -> sprintf "select(1.0, 0.0, %s != 0.0)" a // not (true→0)
        | _  -> "0.0"

    /// Order matches `MathIr.Binary`. `compare` returns -1/0/1; `mod` is
    /// Euclidean remainder (kernel's `remEuclid`).
    let private binaryExpr (op: int) (a: string) (b: string) : string =
        match op with
        | 0  -> sprintf "(%s + %s)" a b
        | 1  -> sprintf "(%s - %s)" a b
        | 2  -> sprintf "(%s * %s)" a b
        | 3  -> sprintf "(%s / %s)" a b
        | 4  -> sprintf "atan2(%s, %s)" a b
        | 5  -> sprintf "min(%s, %s)" a b
        | 6  -> sprintf "max(%s, %s)" a b
        | 7  -> sprintf "pow(%s, %s)" a b
        | 8  -> sprintf "select(select(0.0, 1.0, (%s) > (%s)), -1.0, (%s) < (%s))" a b a b
        | 9  -> sprintf "((%s) - floor((%s) / (%s)) * (%s))" a a b b
        | 10 -> sprintf "select(%s, %s, (%s) != 0.0)" a b a   // and: a == 0 ? a : b
        | 11 -> sprintf "select(%s, %s, (%s) == 0.0)" a b a   // or:  a != 0 ? a : b
        | _  -> "0.0"

    /// WGSL is strict about literal types — emit "1.0" not "1" so the value
    /// parses as f32. `%g` strips trailing zeros and uses exponential form for
    /// very large/small numbers; both forms WGSL accepts as float.
    let private constExpr (v: float) : string =
        let s = sprintf "%g" v
        let hasFloatMarker =
            s.Contains "." || s.Contains "e" || s.Contains "E"
            || s.Contains "inf" || s.Contains "Inf" || s.Contains "nan" || s.Contains "NaN"
        if hasFloatMarker then s else s + ".0"

    let private planePointExpr (plane: Plane) : string =
        match plane with
        | Plane.XY -> "p.xy"
        | Plane.XZ -> "vec2<f32>(p.x, p.z)"
        | Plane.YZ -> "p.yz"
        | _ -> "p.xy"

    let private planeAxisExpr (plane: Plane) (axisX: string) (axisY: string) (axisZ: string) : string =
        match plane with
        | Plane.XY -> sprintf "vec2<f32>(%s, %s)" axisX axisY
        | Plane.XZ -> sprintf "vec2<f32>(%s, %s)" axisX axisZ
        | Plane.YZ -> sprintf "vec2<f32>(%s, %s)" axisY axisZ
        | _ -> sprintf "vec2<f32>(%s, %s)" axisX axisY

    /// Emit a combined WGSL block evaluating multiple roots that share one
    /// `MathIR`. Each entry becomes `fn <name>(p) -> f32` dispatching to its
    /// node's `eval_<id>`. Sub-functions are deduplicated across all entries,
    /// so multiple blocks pulling from the same IR don't redeclare shared
    /// `eval_*` helpers. WGSL doesn't allow forward references — sub-funcs
    /// are emitted before any caller and entry wrappers come last.
    let emitMany (ir: MathIR) (entries: (string * Expr) list) : string =
        let emittedFuncs = HashSet<int>()
        // Function sources in WGSL-valid order — innermost callee first, since
        // WGSL refuses to forward-reference a function. Filled depth-first
        // by `emitFunc`: sub-functions complete (and Add) before their caller.
        let funcSources = ResizeArray<string>()

        let rec emitFunc (funcRootId: int) =
            if not (emittedFuncs.Add funcRootId) then () else
            let body = StringBuilder()
            let emittedNodes = HashSet<int>()

            let rec emitNode (id: int) : string =
                if not (emittedNodes.Add id) then sprintf "n_%d" id
                else
                    let node = ir.Nodes.[id]
                    let intrinsicExpr (intrinsicId: int) =
                        if intrinsicId < 0 || intrinsicId >= ir.Intrinsics.Count then "1.0e10"
                        else
                            let intrinsic = ir.Intrinsics.[intrinsicId]
                            match intrinsic.Kind with
                            | IntrinsicKind.CurveDistanceAlong
                            | _ ->
                                // Primitives are subtree nodes now — the
                                // intrinsic's `PrimitiveStart`/`Count`
                                // fields are reused as a `NodeRefs`
                                // window. We support only line-segment
                                // primitives in WGSL today; mirror the
                                // legacy single-line-segment case.
                                if intrinsic.PrimitiveCount <> 1
                                   || intrinsic.Ax < 0
                                   || intrinsic.Ay < 0
                                   || intrinsic.Az < 0 then
                                    "1.0e10"
                                else
                                    let primStart = intrinsic.PrimitiveStart
                                    if primStart < 0 || primStart >= ir.NodeRefs.Count then
                                        "1.0e10"
                                    else
                                        let primNodeId = ir.NodeRefs.[primStart]
                                        if primNodeId < 0 || primNodeId >= ir.Nodes.Count then
                                            "1.0e10"
                                        else
                                            let primNode = ir.Nodes.[primNodeId]
                                            match primNode.Kind with
                                            | NodeKind.LineSegment ->
                                                let segStart = primNode.A
                                                let p0x = emitNode ir.NodeRefs.[segStart + 0]
                                                let p0y = emitNode ir.NodeRefs.[segStart + 1]
                                                let p1x = emitNode ir.NodeRefs.[segStart + 2]
                                                let p1y = emitNode ir.NodeRefs.[segStart + 3]
                                                let axisX = emitNode intrinsic.Ax
                                                let axisY = emitNode intrinsic.Ay
                                                let axisZ = emitNode intrinsic.Az
                                                let axis2 = planeAxisExpr intrinsic.Plane axisX axisY axisZ
                                                let q = planePointExpr intrinsic.Plane
                                                let sign = if intrinsic.Flip then "-" else ""
                                                sprintf
                                                    """(let_axis_line_distance(%s, %s, vec2<f32>(%s, %s), vec2<f32>(%s, %s)) * %s1.0)"""
                                                    q axis2 p0x p0y p1x p1y sign
                                            | _ -> "1.0e10"
                    let expr =
                        match node.Kind with
                        | NodeKind.Var -> axisExpr node.Op
                        | NodeKind.Slot -> sprintf "slots.v[%d]" node.Op
                        | NodeKind.Const -> constExpr node.Value
                        | NodeKind.UnaryK ->
                            let aName = emitNode node.A
                            unaryExpr node.Op aName
                        | NodeKind.BinaryK ->
                            let aName = emitNode node.A
                            let bName = emitNode node.B
                            binaryExpr node.Op aName bName
                        | NodeKind.RemapAxes ->
                            let xExpr = emitNode node.B
                            let yExpr = emitNode node.C
                            let zExpr = emitNode node.D
                            emitFunc node.A
                            sprintf "eval_%d(vec3<f32>(%s, %s, %s))" node.A xExpr yExpr zExpr
                        | NodeKind.RemapAffine ->
                            let af = ir.Affines.[node.B]
                            let m00 = emitNode af.M00.Id
                            let m01 = emitNode af.M01.Id
                            let m02 = emitNode af.M02.Id
                            let m03 = emitNode af.M03.Id
                            let m10 = emitNode af.M10.Id
                            let m11 = emitNode af.M11.Id
                            let m12 = emitNode af.M12.Id
                            let m13 = emitNode af.M13.Id
                            let m20 = emitNode af.M20.Id
                            let m21 = emitNode af.M21.Id
                            let m22 = emitNode af.M22.Id
                            let m23 = emitNode af.M23.Id
                            emitFunc node.A
                            let newX = sprintf "(%s * p.x + %s * p.y + %s * p.z + %s)" m00 m01 m02 m03
                            let newY = sprintf "(%s * p.x + %s * p.y + %s * p.z + %s)" m10 m11 m12 m13
                            let newZ = sprintf "(%s * p.x + %s * p.y + %s * p.z + %s)" m20 m21 m22 m23
                            sprintf "eval_%d(vec3<f32>(%s, %s, %s))" node.A newX newY newZ
                        | NodeKind.Intrinsic ->
                            intrinsicExpr node.A
                        | NodeKind.Fold ->
                            // Variadic fold over `NodeRefs[A..A+B]`. WGSL has
                            // no variadic ops, so we expand into a chain of
                            // binary ops: `min(c0, min(c1, c2))` for Min/Max,
                            // `(c0 + (c1 + c2))` for Sum.
                            let start = node.A
                            let count = node.B
                            if count <= 0 then "0.0"
                            else
                                let childIds =
                                    [ for k in 0 .. count - 1 -> ir.NodeRefs.[start + k] ]
                                let childNames = childIds |> List.map emitNode
                                let fop : FoldOp = enum node.Op
                                match fop with
                                | FoldOp.Sum ->
                                    childNames
                                    |> List.reduce (fun acc n -> sprintf "(%s + %s)" acc n)
                                | _ ->
                                    let opName =
                                        match fop with
                                        | FoldOp.Max -> "max"
                                        | _          -> "min"
                                    let rec fold = function
                                        | [] -> "0.0"
                                        | [ x ] -> x
                                        | x :: rest -> sprintf "%s(%s, %s)" opName x (fold rest)
                                    fold childNames
                        | NodeKind.LineSegment ->
                            // Children: p0x, p0y, p1x, p1y (4 child node refs).
                            // Plane is `Op / 2`; we project the 3D point onto
                            // it and call the `segment_distance` helper below.
                            let plane : Plane = enum (node.Op / 2)
                            let start = node.A
                            let p0x = emitNode ir.NodeRefs.[start + 0]
                            let p0y = emitNode ir.NodeRefs.[start + 1]
                            let p1x = emitNode ir.NodeRefs.[start + 2]
                            let p1y = emitNode ir.NodeRefs.[start + 3]
                            let q = planePointExpr plane
                            sprintf "segment_distance(%s, vec2<f32>(%s, %s), vec2<f32>(%s, %s))"
                                q p0x p0y p1x p1y
                        | NodeKind.Circle ->
                            // Children: cx, cy, r (3 child node refs).
                            let plane : Plane = enum (node.Op / 2)
                            let start = node.A
                            let cx = emitNode ir.NodeRefs.[start + 0]
                            let cy = emitNode ir.NodeRefs.[start + 1]
                            let r  = emitNode ir.NodeRefs.[start + 2]
                            let q = planePointExpr plane
                            sprintf "circle_curve_distance(%s, vec2<f32>(%s, %s), %s)" q cx cy r
                        | NodeKind.BezierCubic ->
                            // Children: p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y.
                            let plane : Plane = enum (node.Op / 2)
                            let start = node.A
                            let p0x = emitNode ir.NodeRefs.[start + 0]
                            let p0y = emitNode ir.NodeRefs.[start + 1]
                            let p1x = emitNode ir.NodeRefs.[start + 2]
                            let p1y = emitNode ir.NodeRefs.[start + 3]
                            let p2x = emitNode ir.NodeRefs.[start + 4]
                            let p2y = emitNode ir.NodeRefs.[start + 5]
                            let p3x = emitNode ir.NodeRefs.[start + 6]
                            let p3y = emitNode ir.NodeRefs.[start + 7]
                            let q = planePointExpr plane
                            sprintf "bezier_cubic_distance(%s, vec2<f32>(%s, %s), vec2<f32>(%s, %s), vec2<f32>(%s, %s), vec2<f32>(%s, %s))"
                                q p0x p0y p1x p1y p2x p2y p3x p3y
                        | NodeKind.BezierQuadratic
                        | NodeKind.ArcCenter ->
                            // Not yet implemented in WGSL — keep the same
                            // out-of-range sentinel the legacy intrinsic
                            // path used so the surface degenerates.
                            "1.0e10"
                        | _ -> "0.0"
                    body.AppendLine(sprintf "  let n_%d: f32 = %s;" id expr) |> ignore
                    sprintf "n_%d" id

            let resultName = emitNode funcRootId
            body.AppendLine(sprintf "  return %s;" resultName) |> ignore
            let func =
                sprintf "fn eval_%d(p: vec3<f32>) -> f32 {\n%s}\n" funcRootId (body.ToString())
            funcSources.Add func

        for (_, root) in entries do emitFunc root.Id

        let sb = StringBuilder()
        sb.AppendLine "fn cross2(a: vec2<f32>, b: vec2<f32>) -> f32 {" |> ignore
        sb.AppendLine "  return a.x * b.y - a.y * b.x;" |> ignore
        sb.AppendLine "}" |> ignore
        sb.AppendLine "fn endpoint_axis_fallback(q: vec2<f32>, dir: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {" |> ignore
        sb.AppendLine "  let da = a - q;" |> ignore
        sb.AppendLine "  let db = b - q;" |> ignore
        sb.AppendLine "  let sa = dot(da, dir);" |> ignore
        sb.AppendLine "  let sb = dot(db, dir);" |> ignore
        sb.AppendLine "  let pa = abs(cross2(da, dir));" |> ignore
        sb.AppendLine "  let pb = abs(cross2(db, dir));" |> ignore
        sb.AppendLine "  return select(sb, sa, pa <= pb);" |> ignore
        sb.AppendLine "}" |> ignore
        sb.AppendLine "fn let_axis_line_distance(q: vec2<f32>, axis: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {" |> ignore
        sb.AppendLine "  let axis_len = length(axis);" |> ignore
        sb.AppendLine "  if (axis_len < 1.0e-6) { return 1.0e10; }" |> ignore
        sb.AppendLine "  let dir = axis / axis_len;" |> ignore
        sb.AppendLine "  let e = b - a;" |> ignore
        sb.AppendLine "  let den = cross2(dir, e);" |> ignore
        sb.AppendLine "  if (abs(den) < 1.0e-6) { return endpoint_axis_fallback(q, dir, a, b); }" |> ignore
        sb.AppendLine "  let aq = a - q;" |> ignore
        sb.AppendLine "  let s = cross2(aq, e) / den;" |> ignore
        sb.AppendLine "  return s;" |> ignore
        sb.AppendLine "}" |> ignore
        // Distance to a 2D line segment — mirrors `segDist` in math_eval.zig.
        sb.AppendLine "fn segment_distance(q: vec2<f32>, a: vec2<f32>, b: vec2<f32>) -> f32 {" |> ignore
        sb.AppendLine "  let e = b - a;" |> ignore
        sb.AppendLine "  let w = q - a;" |> ignore
        sb.AppendLine "  let t = clamp(dot(w, e) / (dot(e, e) + 1.0e-20), 0.0, 1.0);" |> ignore
        sb.AppendLine "  return length(w - e * t);" |> ignore
        sb.AppendLine "}" |> ignore
        // Unsigned distance to a circle (its perimeter, not its disk).
        // Mirrors `circleCurveDist` in math_eval.zig.
        sb.AppendLine "fn circle_curve_distance(q: vec2<f32>, c: vec2<f32>, r: f32) -> f32 {" |> ignore
        sb.AppendLine "  return abs(length(q - c) - r);" |> ignore
        sb.AppendLine "}" |> ignore
        // Unsigned distance from a 2D point `q` to a cubic Bézier curve
        // defined by control points p0..p3. Iterative Newton on the
        // parameter t — mirrors `cubicCurveDist` in math_eval.zig.
        sb.AppendLine "fn bezier_cubic_distance(q: vec2<f32>, p0: vec2<f32>, p1: vec2<f32>, p2: vec2<f32>, p3: vec2<f32>) -> f32 {" |> ignore
        sb.AppendLine "  var best: f32 = min(length(p0 - q), length(p3 - q));" |> ignore
        sb.AppendLine "  for (var seed: i32 = 0; seed < 9; seed = seed + 1) {" |> ignore
        sb.AppendLine "    var t: f32 = f32(seed) * 0.125;" |> ignore
        sb.AppendLine "    for (var i: i32 = 0; i < 12; i = i + 1) {" |> ignore
        sb.AppendLine "      let u: f32 = 1.0 - t;" |> ignore
        sb.AppendLine "      let b0: f32 = u * u * u;" |> ignore
        sb.AppendLine "      let b1: f32 = 3.0 * u * u * t;" |> ignore
        sb.AppendLine "      let b2: f32 = 3.0 * u * t * t;" |> ignore
        sb.AppendLine "      let b3: f32 = t * t * t;" |> ignore
        sb.AppendLine "      let c: vec2<f32> = b0 * p0 + b1 * p1 + b2 * p2 + b3 * p3;" |> ignore
        sb.AppendLine "      let d1: vec2<f32> = 3.0 * u * u * (p1 - p0) + 6.0 * u * t * (p2 - p1) + 3.0 * t * t * (p3 - p2);" |> ignore
        sb.AppendLine "      let d2: vec2<f32> = 6.0 * u * (p2 - 2.0 * p1 + p0) + 6.0 * t * (p3 - 2.0 * p2 + p1);" |> ignore
        sb.AppendLine "      let v: vec2<f32> = c - q;" |> ignore
        sb.AppendLine "      let g: f32 = dot(v, d1);" |> ignore
        sb.AppendLine "      let gp: f32 = dot(d1, d1) + dot(v, d2);" |> ignore
        sb.AppendLine "      let den: f32 = select(gp, sign(gp) * 1.0e-9, abs(gp) < 1.0e-9);" |> ignore
        sb.AppendLine "      t = clamp(t - g / den, 0.0, 1.0);" |> ignore
        sb.AppendLine "    }" |> ignore
        sb.AppendLine "    let u: f32 = 1.0 - t;" |> ignore
        sb.AppendLine "    let b0: f32 = u * u * u;" |> ignore
        sb.AppendLine "    let b1: f32 = 3.0 * u * u * t;" |> ignore
        sb.AppendLine "    let b2: f32 = 3.0 * u * t * t;" |> ignore
        sb.AppendLine "    let b3: f32 = t * t * t;" |> ignore
        sb.AppendLine "    let c: vec2<f32> = b0 * p0 + b1 * p1 + b2 * p2 + b3 * p3;" |> ignore
        sb.AppendLine "    best = min(best, length(c - q));" |> ignore
        sb.AppendLine "  }" |> ignore
        sb.AppendLine "  return best;" |> ignore
        sb.AppendLine "}" |> ignore
        for src in funcSources do sb.Append src |> ignore
        for (entryName, root) in entries do
            sb.AppendLine(sprintf "fn %s(p: vec3<f32>) -> f32 {" entryName) |> ignore
            sb.AppendLine(sprintf "  return eval_%d(p);" root.Id) |> ignore
            sb.AppendLine "}" |> ignore
        sb.ToString()

    /// Convenience wrapper around `emitMany` for the single-entry case.
    let emit (ir: MathIR) (root: Expr) (entryName: string) : string =
        emitMany ir [ (entryName, root) ]
