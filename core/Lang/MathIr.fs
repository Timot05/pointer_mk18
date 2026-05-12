namespace Server.Lang

// ---------------------------------------------------------------------------
// Math IR — F# mirror of pointer_mk18/kernel/src/math_ir.zig.
//
// Expression DAG for distance fields. Nodes form a topologically sorted array
// (each node references earlier ones by index). Affines, intrinsics, and
// sketch primitives live in side tables; nodes of kind RemapAffine /
// Intrinsic carry an index into those tables.
//
// Future MathIrCodec.fs serializes this to the wire format scene_decode.zig
// expects; keep enum ordering in lock-step with math_ir.zig so that mapping
// is a direct int cast.
// ---------------------------------------------------------------------------

module MathIr =

    type Axis =
        | X = 0
        | Y = 1
        | Z = 2

    type Plane =
        | XY = 0
        | XZ = 1
        | YZ = 2

    /// Order MUST match math_ir.zig's Unary enum (intFromEnum mapping).
    type Unary =
        | Neg = 0
        | Abs = 1
        | Recip = 2
        | Square = 3
        | Sqrt = 4
        | Floor = 5
        | Ceil = 6
        | Round = 7
        | Sin = 8
        | Cos = 9
        | Tan = 10
        | Asin = 11
        | Acos = 12
        | Atan = 13
        | Exp = 14
        | Ln = 15
        | Not = 16

    /// Order MUST match math_ir.zig's Binary enum.
    type Binary =
        | Add = 0
        | Sub = 1
        | Mul = 2
        | Div = 3
        | Atan2 = 4
        | Min = 5
        | Max = 6
        | Pow = 7
        | Compare = 8
        | Mod = 9
        | And_ = 10
        | Or_ = 11

    type NodeKind =
        | Var = 0
        | Slot = 1
        | Const = 2
        | UnaryK = 3
        | BinaryK = 4
        | RemapAxes = 5
        | RemapAffine = 6
        | Intrinsic = 7
        | Fold = 8
        // ── Sketch primitives as first-class subtree nodes ───────────────
        // Each one carries its coords / radius as child node refs via
        // `NodeRefs[A..A+B]`. The plane is encoded in `Op` (0=xy, 1=xz,
        // 2=yz). `ArcCenter` packs clockwise as the low bit of `Op`:
        // `Op = plane * 2 + (clockwise ? 1 : 0)`.
        | LineSegment      = 9
        | Circle           = 10
        | BezierQuadratic  = 11
        | BezierCubic      = 12
        | ArcCenter        = 13

    /// Variadic-fold operator. Stored as the `Op` field of a `Fold` node;
    /// the children are a `NodeRefs[A..A+B]` window of node ids.
    type FoldOp =
        | Min = 0
        | Max = 1
        | Sum = 2

    /// Intrinsics are kernel-level specialised evaluators. Packaging-only
    /// "min over a curve list" ops (formerly `SketchPath` / `SketchDistance`)
    /// were lowered to AST/MathIR `Fold` + per-primitive subtree nodes in
    /// Phases 2-3; `CurveDistanceAlong` stays as an intrinsic because its
    /// signed-distance-along-an-axis math is genuinely specialised.
    type IntrinsicKind =
        | CurveDistanceAlong = 2

    /// Reference to a node in MathIR.Nodes. -1 means "unset".
    type Expr = { Id: int }

    let nullExpr : Expr = { Id = -1 }

    type Vec2 = { x: float; y: float }
    type Vec3 = { x: float; y: float; z: float }
    type Interval = { Lo: float; Hi: float }
    type Box3 = { Xi: Interval; Yi: Interval; Zi: Interval }
    type Cube = { Center: Vec3; HalfSize: float }

    /// Mirrors math_ir.zig Node. `op` is the int value of the relevant enum
    /// (Axis for Var, slot id for Slot, Unary/Binary for ops, etc.).
    type Node = {
        Kind:  NodeKind
        Op:    int
        A:     int
        B:     int
        C:     int
        D:     int
        Value: float
    }

    let private freshNode kind = {
        Kind = kind; Op = 0; A = -1; B = -1; C = -1; D = -1; Value = 0.0
    }

    /// 3×4 affine: 3 rows × 4 columns (translation in the last column). Each
    /// matrix entry is a node reference, so slot- or constant-valued matrices
    /// fall out for free.
    type Affine3 = {
        M00: Expr; M01: Expr; M02: Expr; M03: Expr
        M10: Expr; M11: Expr; M12: Expr; M13: Expr
        M20: Expr; M21: Expr; M22: Expr; M23: Expr
    }

    // Old slot-backed `SketchPrimitive` record + `SlotPoint2` and the
    // `Primitives` side table were retired in Phase 3. Sketch primitives
    // are now first-class subtree node kinds (LineSegment / Circle /
    // BezierQuadratic / BezierCubic / ArcCenter), with their geometry
    // children packed into `NodeRefs`.

    type Intrinsic = {
        Kind: IntrinsicKind
        Plane: Plane
        PrimitiveStart: int
        PrimitiveCount: int
        Closed: bool
        Flip: bool
        Ox: int; Oy: int; Oz: int
        Ax: int; Ay: int; Az: int
    }

    /// Capacity caps mirror math_ir.zig. Not enforced at build time (we use
    /// ResizeArray); the codec will assert before serializing.
    let MaxNodes = 4096
    let MaxAffines = 256
    let MaxIntrinsics = 512
    let MaxTapeWords = 4096
    let MaxImmediates = 512

    /// Mutable IR builder. One instance per evaluation; the evaluator hands
    /// it to every builtin so they can append nodes / affines / intrinsics.
    type MathIR() =
        let nodes      = ResizeArray<Node>()
        let affines    = ResizeArray<Affine3>()
        let intrinsics = ResizeArray<Intrinsic>()
        /// Packed child-id array. Fold + sketch-primitive node kinds +
        /// CurveDistanceAlong all index into this via `(start, count)`
        /// windows. Replaces the legacy Primitives side table.
        let nodeRefs   = ResizeArray<int>()

        member _.Nodes      = nodes
        member _.Affines    = affines
        member _.Intrinsics = intrinsics
        member _.NodeRefs   = nodeRefs

        member _.Push(n: Node) : Expr =
            let id = nodes.Count
            nodes.Add(n)
            { Id = id }

        member this.Var(axis: Axis) : Expr =
            this.Push { freshNode NodeKind.Var with Op = int axis }

        member this.X() = this.Var Axis.X
        member this.Y() = this.Var Axis.Y
        member this.Z() = this.Var Axis.Z

        member this.SlotE(slotId: int) : Expr =
            this.Push { freshNode NodeKind.Slot with Op = slotId }

        member this.Constant(v: float) : Expr =
            this.Push { freshNode NodeKind.Const with Value = v }

        member this.Unary(op: Unary, a: Expr) : Expr =
            this.Push {
                freshNode NodeKind.UnaryK with Op = int op; A = a.Id
            }

        member this.Binary(op: Binary, a: Expr, b: Expr) : Expr =
            this.Push {
                freshNode NodeKind.BinaryK with Op = int op; A = a.Id; B = b.Id
            }

        member this.RemapAxes(target: Expr, x: Expr, y: Expr, z: Expr) : Expr =
            this.Push {
                freshNode NodeKind.RemapAxes with
                    A = target.Id; B = x.Id; C = y.Id; D = z.Id
            }

        member _.AddAffine(a: Affine3) : int =
            let id = affines.Count
            affines.Add(a)
            id

        member this.RemapAffine(target: Expr, affineId: int) : Expr =
            this.Push {
                freshNode NodeKind.RemapAffine with A = target.Id; B = affineId
            }

        // Sketch primitives now live as `*N` builders below
        // (LineSegmentN / CircleN / etc.) — they emit subtree nodes whose
        // child coords flow through the regular DAG.

        // ---- intrinsics --------------------------------------------------------

        member private _.PushIntrinsic(i: Intrinsic) : int =
            let id = intrinsics.Count
            intrinsics.Add(i)
            id

        /// Signed distance along `axis` to the closest of the given
        /// primitive curves. The primitives are stored as a contiguous
        /// window in `NodeRefs` — the same packing Fold uses — so a
        /// `CurveDistanceAlong` intrinsic carries no special-shape data
        /// beyond what the DAG can reach.
        ///
        /// `Intrinsic.PrimitiveStart` and `PrimitiveCount` are reused
        /// as the `NodeRefs` window. The names are vestigial — semantics
        /// are now "child-node window" — but kept to avoid wire-format
        /// churn.
        member this.CurveDistanceAlong(
                plane: Plane,
                primitives: Expr list,
                axisX: Expr,
                axisY: Expr,
                axisZ: Expr,
                flip: bool) : Expr =
            let start = nodeRefs.Count
            for p in primitives do
                nodeRefs.Add p.Id
            let count = nodeRefs.Count - start
            let i = {
                Kind = IntrinsicKind.CurveDistanceAlong
                Plane = plane
                PrimitiveStart = start
                PrimitiveCount = count
                Closed = false; Flip = flip
                Ox = -1; Oy = -1; Oz = -1
                Ax = axisX.Id; Ay = axisY.Id; Az = axisZ.Id
            }
            let id = this.PushIntrinsic i
            this.Push { freshNode NodeKind.Intrinsic with A = id }

        // ---- fold ---------------------------------------------------------

        /// Variadic fold over `children`. Stores the children's node ids in
        /// the packed `NodeRefs` array and pushes a `Fold` node whose `A`
        /// field names the start index and `B` the count. The runtime
        /// evaluator (F# eval, kernel tape) reads `NodeRefs[A..A+B]` and
        /// folds the children's values with `op`.
        ///
        /// Caller's responsibility to provide a non-empty list — a Fold over
        /// zero children has no well-defined identity here.
        member this.Fold(op: FoldOp, children: Expr list) : Expr =
            let start = nodeRefs.Count
            for child in children do
                nodeRefs.Add child.Id
            let count = nodeRefs.Count - start
            this.Push { freshNode NodeKind.Fold with Op = int op; A = start; B = count }

        // ---- sketch primitives as subtree nodes --------------------------
        //
        // Each primitive's coordinate inputs are stored as child node refs
        // in `NodeRefs[A..A+B]`. The plane is packed into `Op`. The
        // evaluators read each coord through normal node evaluation, so
        // endpoint coords can be any expression (Const, Slot, computed).

        member private this.PushPrimitiveNode
                (kind: NodeKind) (plane: Plane) (op_extra: int) (children: Expr list) : Expr =
            let start = nodeRefs.Count
            for child in children do
                nodeRefs.Add child.Id
            let count = nodeRefs.Count - start
            this.Push {
                freshNode kind with
                    Op = int plane * 2 + op_extra
                    A = start
                    B = count
            }

        /// 2D line segment from `(p0x, p0y)` to `(p1x, p1y)` evaluated on
        /// `plane`. Children order: p0x, p0y, p1x, p1y.
        member this.LineSegmentN(plane: Plane, p0x, p0y, p1x, p1y) : Expr =
            this.PushPrimitiveNode NodeKind.LineSegment plane 0 [ p0x; p0y; p1x; p1y ]

        /// Circle with center `(cx, cy)` and radius `r` on `plane`.
        /// Children order: cx, cy, r.
        member this.CircleN(plane: Plane, cx, cy, r) : Expr =
            this.PushPrimitiveNode NodeKind.Circle plane 0 [ cx; cy; r ]

        /// Quadratic Bézier. Children order: p0x, p0y, p1x, p1y, p2x, p2y.
        member this.BezierQuadraticN(plane: Plane, p0x, p0y, p1x, p1y, p2x, p2y) : Expr =
            this.PushPrimitiveNode NodeKind.BezierQuadratic plane 0
                [ p0x; p0y; p1x; p1y; p2x; p2y ]

        /// Cubic Bézier. Children order: p0x..p3y (8 children).
        member this.BezierCubicN(plane: Plane, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y) : Expr =
            this.PushPrimitiveNode NodeKind.BezierCubic plane 0
                [ p0x; p0y; p1x; p1y; p2x; p2y; p3x; p3y ]

        /// Center-parameterised arc from `(sx, sy)` to `(ex, ey)` about
        /// `(cx, cy)`. `clockwise` is packed into the low bit of `Op`.
        /// Children order: sx, sy, ex, ey, cx, cy.
        member this.ArcCenterN(plane: Plane, sx, sy, ex, ey, cx, cy, clockwise: bool) : Expr =
            let extra = if clockwise then 1 else 0
            this.PushPrimitiveNode NodeKind.ArcCenter plane extra
                [ sx; sy; ex; ey; cx; cy ]
