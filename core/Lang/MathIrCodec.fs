namespace Server.Lang

// ---------------------------------------------------------------------------
// MathIrCodec.fs — F# encoder for the wire format consumed by
// `kernel/src/math_ir_decode.zig`.
//
// Total bytes = 28 (header)
//             + nodes      × 32
//             + affines    × 48
//             + intrinsics × 48
//             + primitives × 64
//
// All fields little-endian. Per-element layouts mirror the in-memory MathIR
// types byte-for-byte where natural; explicit padding keeps section strides
// aligned (intrinsics 4-byte tail pad, primitives 7-byte tail pad).
// ---------------------------------------------------------------------------

module MathIrCodec =

    open Fable.Core
    open Fable.Core.JsInterop

    /// "MIR1" little-endian.
    let MAGIC : uint32 = 0x4D495231u
    let VERSION : uint32 = 1u

    let private HEADER_BYTES = 28
    let private NODE_BYTES = 32
    let private AFFINE_BYTES = 48
    let private INTRINSIC_BYTES = 48
    let private PRIMITIVE_BYTES = 64

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

    let private writePrimitive (dv: obj) (off: int) (p: MathIr.SketchPrimitive) =
        setI32 dv (off + 0)  (int p.Kind)
        setI32 dv (off + 4)  p.P0.XSlot
        setI32 dv (off + 8)  p.P0.YSlot
        setI32 dv (off + 12) p.P1.XSlot
        setI32 dv (off + 16) p.P1.YSlot
        setI32 dv (off + 20) p.P2.XSlot
        setI32 dv (off + 24) p.P2.YSlot
        setI32 dv (off + 28) p.P3.XSlot
        setI32 dv (off + 32) p.P3.YSlot
        setI32 dv (off + 36) p.Radius
        setI32 dv (off + 40) p.Chord
        setI32 dv (off + 44) p.MaxCamber
        setI32 dv (off + 48) p.CamberPos
        setI32 dv (off + 52) p.Thickness
        setU8  dv (off + 56) (if p.Clockwise then 1 else 0)
        // bytes 57..63 padding

    /// Compute the total byte size of a (MathIR, root) encoding without
    /// allocating. Useful for tests and capacity assertions.
    let byteSize (ir: MathIr.MathIR) : int =
        HEADER_BYTES
        + ir.Nodes.Count * NODE_BYTES
        + ir.Affines.Count * AFFINE_BYTES
        + ir.Intrinsics.Count * INTRINSIC_BYTES
        + ir.Primitives.Count * PRIMITIVE_BYTES

    /// Serialize (ir, root) to a Uint8Array suitable for `Wasm.uploadIr`.
    /// The kernel's `math_ir_decode` accepts the result directly.
    let serialize (ir: MathIr.MathIR) (root: MathIr.Expr) : obj =
        let total = byteSize ir
        let buf = createUint8Array total
        let dv = dvOver buf

        let nodeCount = ir.Nodes.Count
        let affineCount = ir.Affines.Count
        let intrinsicCount = ir.Intrinsics.Count
        let primitiveCount = ir.Primitives.Count

        // Header
        setU32 dv 0  MAGIC
        setU32 dv 4  VERSION
        setU32 dv 8  (uint32 nodeCount)
        setU32 dv 12 (uint32 affineCount)
        setU32 dv 16 (uint32 intrinsicCount)
        setU32 dv 20 (uint32 primitiveCount)
        setU32 dv 24 (uint32 root.Id)

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
        for i in 0 .. primitiveCount - 1 do
            writePrimitive dv off ir.Primitives.[i]
            off <- off + PRIMITIVE_BYTES

        buf
