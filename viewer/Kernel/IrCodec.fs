module Kernel.IrCodec

// Field IR builder + binary encoder for the voxel viewer. Output bytes
// are consumed by `ir_upload` in kernel/src/main.zig — keep the format
// in sync with the block comment there.

open Fable.Core
open Fable.Core.JsInterop

// ── Constants (must match kernel/src/main.zig) ──────────────────────────
let private IR_HEADER_SIZE = 16
let private IR_NODE_SIZE = 32
let private IR_PRIM_SIZE = 32

// Node kinds
let private NK_PRIMITIVE = 0
let private NK_TRANSLATE = 1
let private NK_ROTATE = 2
let private NK_BOOLEAN = 3
let private NK_UNARY = 4
let private NK_SKETCH = 5

// Primitive sub-kinds
let private PK_SPHERE = 0
let private PK_CYLINDER = 1
let private PK_BOX = 2
let private PK_HALF_PLANE = 3

// Boolean sub-kinds
let private BK_UNION = 0
let private BK_SUBTRACT = 1
let private BK_INTERSECT = 2

// Unary sub-kinds
let private UK_THICKEN = 0
let private UK_SHELL = 1

// Sketch-prim kinds
let private SP_LINE = 0
let private SP_CIRCLE = 1
let private SP_ARC = 2

let private AXIS_X = 0
let private AXIS_Y = 1
let private AXIS_Z = 2

let private MAX_NODES = 256
let private MAX_PRIMS = 128

type NodeRef = int
type PrimRef = int

type Axis = X | Y | Z

// ── Typed-array interop (DataView + Uint8Array) ─────────────────────────

[<Emit("new Uint8Array($0)")>]
let private newUint8Array (size: int) : obj = jsNative

[<Emit("new DataView($0.buffer)")>]
let private dataViewOf (arr: obj) : obj = jsNative

[<Emit("$0.setFloat32($1, $2, true)")>]
let private setF32 (view: obj) (offset: int) (value: float) : unit = jsNative

[<Emit("$0.setUint32($1, $2, true)")>]
let private setU32 (view: obj) (offset: int) (value: int) : unit = jsNative

[<Emit("$0[$1] = $2")>]
let private setByte (arr: obj) (offset: int) (value: int) : unit = jsNative

[<Emit("$0.subarray($1, $2)")>]
let private subarray (arr: obj) (startI: int) (endI: int) : obj = jsNative

[<Emit("$0.set($1, $2)")>]
let private setFrom (dst: obj) (src: obj) (offset: int) : unit = jsNative

// ── Builder ─────────────────────────────────────────────────────────────

type IrBuilder =
    { Nodes: obj          // Uint8Array
      Prims: obj
      NodeView: obj       // DataView
      PrimView: obj
      mutable NodeCount: int
      mutable PrimCount: int }

let create () : IrBuilder =
    let nodes = newUint8Array (MAX_NODES * IR_NODE_SIZE)
    let prims = newUint8Array (MAX_PRIMS * IR_PRIM_SIZE)
    { Nodes = nodes
      Prims = prims
      NodeView = dataViewOf nodes
      PrimView = dataViewOf prims
      NodeCount = 0
      PrimCount = 0 }

let private pushNode (b: IrBuilder) (kind: int) (sub: int) (fill: int -> unit) : NodeRef =
    if b.NodeCount >= MAX_NODES then failwith "IR node limit exceeded"
    let idx = b.NodeCount
    b.NodeCount <- idx + 1
    let baseOff = idx * IR_NODE_SIZE
    setByte b.Nodes baseOff kind
    setByte b.Nodes (baseOff + 1) sub
    fill (baseOff + 4)
    idx

let private pushPrim (b: IrBuilder) (kind: int) (flags: int) (fill: int -> unit) : PrimRef =
    if b.PrimCount >= MAX_PRIMS then failwith "IR prim limit exceeded"
    let idx = b.PrimCount
    b.PrimCount <- idx + 1
    let baseOff = idx * IR_PRIM_SIZE
    setByte b.Prims baseOff kind
    setByte b.Prims (baseOff + 1) flags
    fill (baseOff + 4)
    idx

// ── Primitives ──────────────────────────────────────────────────────────

let sphere (b: IrBuilder) (radius: float) : NodeRef =
    pushNode b NK_PRIMITIVE PK_SPHERE (fun o ->
        setF32 b.NodeView o radius)

let cylinder (b: IrBuilder) (radius: float) (height: float) : NodeRef =
    pushNode b NK_PRIMITIVE PK_CYLINDER (fun o ->
        setF32 b.NodeView o radius
        setF32 b.NodeView (o + 4) height)

let box (b: IrBuilder) (w: float) (h: float) (d: float) : NodeRef =
    pushNode b NK_PRIMITIVE PK_BOX (fun o ->
        setF32 b.NodeView o w
        setF32 b.NodeView (o + 4) h
        setF32 b.NodeView (o + 8) d)

let halfPlane (b: IrBuilder) (axis: Axis) (offset: float) (flip: bool) : NodeRef =
    let axisCode =
        match axis with
        | X -> AXIS_X
        | Y -> AXIS_Y
        | Z -> AXIS_Z
    pushNode b NK_PRIMITIVE PK_HALF_PLANE (fun o ->
        setU32 b.NodeView o axisCode
        setF32 b.NodeView (o + 4) offset
        setU32 b.NodeView (o + 8) (if flip then 1 else 0))

// ── Transforms ──────────────────────────────────────────────────────────

let translate (b: IrBuilder) (x: float) (y: float) (z: float) (child: NodeRef) : NodeRef =
    pushNode b NK_TRANSLATE 0 (fun o ->
        setF32 b.NodeView o x
        setF32 b.NodeView (o + 4) y
        setF32 b.NodeView (o + 8) z
        setU32 b.NodeView (o + 12) child)

let rotate (b: IrBuilder) (ax: float) (ay: float) (az: float) (angle: float) (child: NodeRef) : NodeRef =
    pushNode b NK_ROTATE 0 (fun o ->
        setF32 b.NodeView o ax
        setF32 b.NodeView (o + 4) ay
        setF32 b.NodeView (o + 8) az
        setF32 b.NodeView (o + 12) angle
        setU32 b.NodeView (o + 16) child)

// ── Booleans ────────────────────────────────────────────────────────────

let private boolOp (b: IrBuilder) (op: int) (a: NodeRef) (bRef: NodeRef) (radius: float) : NodeRef =
    pushNode b NK_BOOLEAN op (fun o ->
        setF32 b.NodeView o radius
        setU32 b.NodeView (o + 4) a
        setU32 b.NodeView (o + 8) bRef)

let union (b: IrBuilder) (a: NodeRef) (bRef: NodeRef) : NodeRef =
    boolOp b BK_UNION a bRef 0.0
let unionSmooth (b: IrBuilder) (radius: float) (a: NodeRef) (bRef: NodeRef) : NodeRef =
    boolOp b BK_UNION a bRef radius

let subtract (b: IrBuilder) (a: NodeRef) (bRef: NodeRef) : NodeRef =
    boolOp b BK_SUBTRACT a bRef 0.0
let subtractSmooth (b: IrBuilder) (radius: float) (a: NodeRef) (bRef: NodeRef) : NodeRef =
    boolOp b BK_SUBTRACT a bRef radius

let intersect (b: IrBuilder) (a: NodeRef) (bRef: NodeRef) : NodeRef =
    boolOp b BK_INTERSECT a bRef 0.0
let intersectSmooth (b: IrBuilder) (radius: float) (a: NodeRef) (bRef: NodeRef) : NodeRef =
    boolOp b BK_INTERSECT a bRef radius

// ── Unary ───────────────────────────────────────────────────────────────

let thicken (b: IrBuilder) (amount: float) (child: NodeRef) : NodeRef =
    pushNode b NK_UNARY UK_THICKEN (fun o ->
        setF32 b.NodeView o amount
        setU32 b.NodeView (o + 4) child)

let shell (b: IrBuilder) (thickness: float) (child: NodeRef) : NodeRef =
    pushNode b NK_UNARY UK_SHELL (fun o ->
        setF32 b.NodeView o thickness
        setU32 b.NodeView (o + 4) child)

// ── Sketch ──────────────────────────────────────────────────────────────

let sketch (b: IrBuilder) (primsFirst: PrimRef) (primsLen: int) (closed: bool) (flip: bool) : NodeRef =
    pushNode b NK_SKETCH 0 (fun o ->
        setU32 b.NodeView o primsFirst
        setU32 b.NodeView (o + 4) primsLen
        setU32 b.NodeView (o + 8) (if closed then 1 else 0)
        setU32 b.NodeView (o + 12) (if flip then 1 else 0))

let sketchLine (b: IrBuilder) (sx: float) (sy: float) (ex: float) (ey: float) : PrimRef =
    pushPrim b SP_LINE 0 (fun o ->
        setF32 b.PrimView o sx
        setF32 b.PrimView (o + 4) sy
        setF32 b.PrimView (o + 8) ex
        setF32 b.PrimView (o + 12) ey)

let sketchCircle (b: IrBuilder) (cx: float) (cy: float) (radius: float) : PrimRef =
    pushPrim b SP_CIRCLE 0 (fun o ->
        setF32 b.PrimView o cx
        setF32 b.PrimView (o + 4) cy
        setF32 b.PrimView (o + 8) radius)

let sketchArc (b: IrBuilder) (sx: float) (sy: float) (ex: float) (ey: float) (cx: float) (cy: float) (clockwise: bool) : PrimRef =
    pushPrim b SP_ARC (if clockwise then 1 else 0) (fun o ->
        setF32 b.PrimView o sx
        setF32 b.PrimView (o + 4) sy
        setF32 b.PrimView (o + 8) ex
        setF32 b.PrimView (o + 12) ey
        setF32 b.PrimView (o + 16) cx
        setF32 b.PrimView (o + 20) cy)

// ── Serialize ───────────────────────────────────────────────────────────

let serialize (b: IrBuilder) (root: NodeRef) : obj =
    let total = IR_HEADER_SIZE + b.NodeCount * IR_NODE_SIZE + b.PrimCount * IR_PRIM_SIZE
    let out = newUint8Array total
    let dv = dataViewOf out
    setU32 dv 0 1             // version
    setU32 dv 4 b.NodeCount
    setU32 dv 8 b.PrimCount
    setU32 dv 12 root
    setFrom out (subarray b.Nodes 0 (b.NodeCount * IR_NODE_SIZE)) IR_HEADER_SIZE
    setFrom out
        (subarray b.Prims 0 (b.PrimCount * IR_PRIM_SIZE))
        (IR_HEADER_SIZE + b.NodeCount * IR_NODE_SIZE)
    out
