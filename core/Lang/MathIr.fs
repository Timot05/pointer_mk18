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

    type PrimitiveKind =
        | LineSegment = 0
        | BezierQuadratic = 1
        | BezierCubic = 2
        | Circle = 3
        | Naca4 = 4
        | ArcCenter = 5

    type IntrinsicKind =
        | SketchDistance = 0
        | SketchPath = 1
        | CurveDistanceAlong = 2

    /// Reference to a node in MathIR.Nodes. -1 means "unset".
    type Expr = { Id: int }

    let nullExpr : Expr = { Id = -1 }

    type Vec2 = { x: float; y: float }
    type Vec3 = { x: float; y: float; z: float }
    type SlotPoint2 = { XSlot: int; YSlot: int }
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

    /// Slot-backed 2D primitive used by sketch intrinsics. Each `XSlot/YSlot`
    /// references a slot id in the host's slot table.
    type SketchPrimitive = {
        Kind: PrimitiveKind
        P0: SlotPoint2
        P1: SlotPoint2
        P2: SlotPoint2
        P3: SlotPoint2
        Radius: int
        Chord: int
        MaxCamber: int
        CamberPos: int
        Thickness: int
        Clockwise: bool
    }

    let private emptySlotPoint = { XSlot = -1; YSlot = -1 }

    let private freshPrimitive kind = {
        Kind = kind
        P0 = emptySlotPoint; P1 = emptySlotPoint
        P2 = emptySlotPoint; P3 = emptySlotPoint
        Radius = -1; Chord = -1
        MaxCamber = -1; CamberPos = -1; Thickness = -1
        Clockwise = false
    }

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
    let MaxPrimitives = 2048
    let MaxTapeWords = 4096
    let MaxImmediates = 512

    /// Mutable IR builder. One instance per evaluation; the evaluator hands
    /// it to every builtin so they can append nodes / affines / intrinsics.
    type MathIR() =
        let nodes      = ResizeArray<Node>()
        let affines    = ResizeArray<Affine3>()
        let intrinsics = ResizeArray<Intrinsic>()
        let primitives = ResizeArray<SketchPrimitive>()

        member _.Nodes      = nodes
        member _.Affines    = affines
        member _.Intrinsics = intrinsics
        member _.Primitives = primitives

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

        // ---- sketch primitives (slot-indexed) ---------------------------------

        member _.Point2(xSlot: int, ySlot: int) : SlotPoint2 =
            { XSlot = xSlot; YSlot = ySlot }

        member private _.PushPrimitive(p: SketchPrimitive) : int =
            let id = primitives.Count
            primitives.Add(p)
            id

        member this.LineSegment(start: SlotPoint2, stop: SlotPoint2) : int =
            this.PushPrimitive {
                freshPrimitive PrimitiveKind.LineSegment with P0 = start; P1 = stop
            }

        member this.BezierQuadratic(p0, p1, p2) : int =
            this.PushPrimitive {
                freshPrimitive PrimitiveKind.BezierQuadratic with
                    P0 = p0; P1 = p1; P2 = p2
            }

        member this.BezierCubic(p0, p1, p2, p3) : int =
            this.PushPrimitive {
                freshPrimitive PrimitiveKind.BezierCubic with
                    P0 = p0; P1 = p1; P2 = p2; P3 = p3
            }

        member this.Circle(center: SlotPoint2, radiusSlot: int) : int =
            this.PushPrimitive {
                freshPrimitive PrimitiveKind.Circle with P0 = center; Radius = radiusSlot
            }

        member this.ArcCenter(start, stop, center, clockwise) : int =
            this.PushPrimitive {
                freshPrimitive PrimitiveKind.ArcCenter with
                    P0 = start; P1 = stop; P2 = center; Clockwise = clockwise
            }

        // ---- intrinsics --------------------------------------------------------

        member private _.PushIntrinsic(i: Intrinsic) : int =
            let id = intrinsics.Count
            intrinsics.Add(i)
            id

        member this.SketchDistance(plane: Plane, primitive: int) : Expr =
            let i = {
                Kind = IntrinsicKind.SketchDistance
                Plane = plane
                PrimitiveStart = primitive
                PrimitiveCount = 1
                Closed = false; Flip = false
                Ox = -1; Oy = -1; Oz = -1
                Ax = -1; Ay = -1; Az = -1
            }
            let id = this.PushIntrinsic i
            this.Push { freshNode NodeKind.Intrinsic with A = id }

        member this.SketchPath(
                plane: Plane,
                primitiveStart: int,
                primitiveCount: int,
                closed: bool,
                flip: bool) : Expr =
            let i = {
                Kind = IntrinsicKind.SketchPath
                Plane = plane
                PrimitiveStart = primitiveStart
                PrimitiveCount = primitiveCount
                Closed = closed; Flip = flip
                Ox = -1; Oy = -1; Oz = -1
                Ax = -1; Ay = -1; Az = -1
            }
            let id = this.PushIntrinsic i
            this.Push { freshNode NodeKind.Intrinsic with A = id }
