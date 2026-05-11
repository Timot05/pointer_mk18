namespace Server.Lang

// ---------------------------------------------------------------------------
// MathIrCodec.fs — F# encoder for the wire format consumed by
// `kernel/src/math_ir_decode.zig`.
//
// Header (32 bytes, MIR2):
//   u32 magic = "MIR2"  u32 version = 5
//   u32 node_count       u32 affine_count
//   u32 intrinsic_count  u32 root
//   u32 view_count       u32 node_ref_count
//
// Sections (in order): nodes × 32, affines × 48, intrinsics × 48,
// node_refs × 4, views × 12.
//
// View entry (12 bytes): { i32 expr_id; u32 palette_idx; u32 kind; }.
// One per visible Field block. `kind` selects the renderer shading mode
// (0 = opaque surface, 1 = field-lines, 2 = isosurface).
//
// node_refs is a packed i32 array of child node ids. Used by Fold
// (`A`=start, `B`=count), the sketch-primitive subtree node kinds
// (LineSegment / Circle / Bezier* / ArcCenter), and the
// CurveDistanceAlong intrinsic (PrimitiveStart / PrimitiveCount fields
// reused as a NodeRefs window). The legacy Primitives side table was
// retired in Phase 3.
//
// All fields little-endian. Per-element layouts mirror the in-memory MathIR
// types byte-for-byte where natural; explicit padding keeps section strides
// aligned (intrinsics 4-byte tail pad, primitives 7-byte tail pad).
// ---------------------------------------------------------------------------

module MathIrCodec =

    open Fable.Core
    open Fable.Core.JsInterop

    /// "MIR2" little-endian.
    let MAGIC : uint32 = 0x4D495232u
    let VERSION : uint32 = 5u

    let private HEADER_BYTES = 32
    let private NODE_BYTES = 32
    let private AFFINE_BYTES = 48
    let private INTRINSIC_BYTES = 48
    let private NODE_REF_BYTES = 4
    let private VIEW_BYTES = 12

    [<Emit("new Uint8Array($0)")>]
    let private createUint8Array (len: int) : obj = jsNative

    [<Emit("new DataView($0.buffer)")>]
    let private dvOver (u8: obj) : obj = jsNative

    let private setU32 (dv: obj) (offset: int) (v: uint32) =
        dv?setUint32 (offset, v, true) |> ignore

    let private setI32 (dv: obj) (offset: int) (v: int) =
        dv?setInt32 (offset, v, true) |> ignore

    let private setF64 (dv: obj) (offset: int) (v: float) =
        dv?setFloat64 (offset, v, true) |> ignore

    let private setU8 (dv: obj) (offset: int) (v: int) =
        dv?setUint8 (offset, v) |> ignore

    let private writeNode (dv: obj) (off: int) (n: MathIr.Node) =
        setI32 dv (off + 0)  (int n.Kind)
        setI32 dv (off + 4)  n.Op
        setI32 dv (off + 8)  n.A
        setI32 dv (off + 12) n.B
        setI32 dv (off + 16) n.C
        setI32 dv (off + 20) n.D
        setF64 dv (off + 24) n.Value

    let private writeAffine (dv: obj) (off: int) (a: MathIr.Affine3) =
        setI32 dv (off + 0)  a.M00.Id
        setI32 dv (off + 4)  a.M01.Id
        setI32 dv (off + 8)  a.M02.Id
        setI32 dv (off + 12) a.M03.Id
        setI32 dv (off + 16) a.M10.Id
        setI32 dv (off + 20) a.M11.Id
        setI32 dv (off + 24) a.M12.Id
        setI32 dv (off + 28) a.M13.Id
        setI32 dv (off + 32) a.M20.Id
        setI32 dv (off + 36) a.M21.Id
        setI32 dv (off + 40) a.M22.Id
        setI32 dv (off + 44) a.M23.Id

    let private writeIntrinsic (dv: obj) (off: int) (i: MathIr.Intrinsic) =
        setI32 dv (off + 0)  (int i.Kind)
        setI32 dv (off + 4)  (int i.Plane)
        setI32 dv (off + 8)  i.PrimitiveStart
        setI32 dv (off + 12) i.PrimitiveCount
        setU8  dv (off + 16) (if i.Closed then 1 else 0)
        setU8  dv (off + 17) (if i.Flip then 1 else 0)
        // bytes 18..19 padding (Uint8Array zero-initialized — no write needed)
        setI32 dv (off + 20) i.Ox
        setI32 dv (off + 24) i.Oy
        setI32 dv (off + 28) i.Oz
        setI32 dv (off + 32) i.Ax
        setI32 dv (off + 36) i.Ay
        setI32 dv (off + 40) i.Az
        // bytes 44..47 padding

    /// Compute the total byte size of a (MathIR, root, views) encoding
    /// without allocating. Useful for tests and capacity assertions.
    let byteSize (ir: MathIr.MathIR) (views: (MathIr.Expr * uint32 * uint32) list) : int =
        HEADER_BYTES
        + ir.Nodes.Count * NODE_BYTES
        + ir.Affines.Count * AFFINE_BYTES
        + ir.Intrinsics.Count * INTRINSIC_BYTES
        + ir.NodeRefs.Count * NODE_REF_BYTES
        + List.length views * VIEW_BYTES

    /// Serialize (ir, root, views) to a Uint8Array suitable for
    /// `Wasm.uploadIr`. `views` is one entry per visible Field block —
    /// the kernel renders each separately so the winning block at each
    /// pixel can be coloured by `palette_idx` and shaded by `kind`. An
    /// empty list produces the same render as a single `root` view (no
    /// per-pixel tag eval).
    let serialize (ir: MathIr.MathIR) (root: MathIr.Expr) (views: (MathIr.Expr * uint32 * uint32) list) : obj =
        let total = byteSize ir views
        let buf = createUint8Array total
        let dv = dvOver buf

        let nodeCount = ir.Nodes.Count
        let affineCount = ir.Affines.Count
        let intrinsicCount = ir.Intrinsics.Count
        let nodeRefCount = ir.NodeRefs.Count
        let viewCount = List.length views

        // Header
        setU32 dv 0  MAGIC
        setU32 dv 4  VERSION
        setU32 dv 8  (uint32 nodeCount)
        setU32 dv 12 (uint32 affineCount)
        setU32 dv 16 (uint32 intrinsicCount)
        setU32 dv 20 (uint32 root.Id)
        setU32 dv 24 (uint32 viewCount)
        setU32 dv 28 (uint32 nodeRefCount)

        // Sections
        let mutable off = HEADER_BYTES
        for i in 0 .. nodeCount - 1 do
            writeNode dv off ir.Nodes.[i]
            off <- off + NODE_BYTES
        for i in 0 .. affineCount - 1 do
            writeAffine dv off ir.Affines.[i]
            off <- off + AFFINE_BYTES
        for i in 0 .. intrinsicCount - 1 do
            writeIntrinsic dv off ir.Intrinsics.[i]
            off <- off + INTRINSIC_BYTES
        for i in 0 .. nodeRefCount - 1 do
            setI32 dv off ir.NodeRefs.[i]
            off <- off + NODE_REF_BYTES
        for (expr, paletteIdx, kind) in views do
            setI32 dv off expr.Id
            setU32 dv (off + 4) paletteIdx
            setU32 dv (off + 8) kind
            off <- off + VIEW_BYTES

        buf
