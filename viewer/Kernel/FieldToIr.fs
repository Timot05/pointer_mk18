module Kernel.FieldToIr

// Bridges the editor's slot-based `FieldNode` tree (from
// `core/Field/FieldIR.fs`) to the flat byte-encoded IR the Zig kernel
// consumes (via `Kernel.IrCodec`). The conversion:
//
//   * Resolves each `Slot` index to its current float via the live
//     `SlotValues` array. The kernel IR has no slot concept — values
//     are baked into nodes, so every rebuild sees up-to-date numbers.
//   * Walks the tree recursively; each `FieldNode` maps 1:1 to an
//     `IrCodec` call.
//   * Unions multiple top-level surfaces into a single root with hard
//     `union`. Smooth radius is a per-boolean slot inside the tree, not
//     something we apply at the fusion seam.
//
// Returns `None` for an empty surface list so callers can treat that as
// "render nothing" rather than synthesizing a placeholder.

open Server

let private axisOf (s: string) : IrCodec.Axis =
    match s with
    | "X" -> IrCodec.X
    | "Y" -> IrCodec.Y
    | _ -> IrCodec.Z

let rec private compileNode
        (ir: IrCodec.IrBuilder)
        (values: float array)
        (node: FieldNode) : IrCodec.NodeRef =
    let slot (s: Slot) = values.[s]

    match node with
    | FPrimitive (PrimSphere r) ->
        IrCodec.sphere ir (slot r)

    | FPrimitive (PrimCylinder (r, h)) ->
        IrCodec.cylinder ir (slot r) (slot h)

    | FPrimitive (PrimBox (w, h, d)) ->
        IrCodec.box ir (slot w) (slot h) (slot d)

    | FPrimitive (PrimHalfPlane (axis, offset, flip)) ->
        IrCodec.halfPlane ir (axisOf axis) (slot offset) flip

    | FTranslate (x, y, z, child) ->
        let c = compileNode ir values child
        IrCodec.translate ir (slot x) (slot y) (slot z) c

    | FRotate (ax, ay, az, angle, child) ->
        let c = compileNode ir values child
        IrCodec.rotate ir (slot ax) (slot ay) (slot az) (slot angle) c

    | FBoolean (op, radiusSlot, a, b) ->
        let ra = compileNode ir values a
        let rb = compileNode ir values b
        let r = slot radiusSlot
        // Radius = 0 is a hard boolean; smooth* with r=0 degenerates to
        // the same thing on the kernel side, so always use the smooth
        // entry points.
        match op with
        | BoolUnion -> IrCodec.unionSmooth ir r ra rb
        | BoolSubtract -> IrCodec.subtractSmooth ir r ra rb
        | BoolIntersect -> IrCodec.intersectSmooth ir r ra rb

    | FFieldOp (op, valueSlot, child) ->
        let c = compileNode ir values child
        let v = slot valueSlot
        match op with
        | OpThicken -> IrCodec.thicken ir v c
        | OpShell -> IrCodec.shell ir v c

    | FSketch sk ->
        // Snapshot PrimCount *before* pushing this sketch's 2D prims so
        // the node points at its own contiguous slice. Kernel stores
        // (primsFirst, primsLen); slices from sibling sketches live
        // immediately before/after.
        let firstPrim = ir.PrimCount
        sk.Primitives
        |> List.iter (fun prim ->
            match prim with
            | SpLineSegment (s, e) ->
                IrCodec.sketchLine ir
                    (slot s.XSlot) (slot s.YSlot)
                    (slot e.XSlot) (slot e.YSlot)
                |> ignore
            | SpCircle (c, r) ->
                IrCodec.sketchCircle ir
                    (slot c.XSlot) (slot c.YSlot)
                    (slot r)
                |> ignore
            | SpArcCenter (s, e, c, cw) ->
                IrCodec.sketchArc ir
                    (slot s.XSlot) (slot s.YSlot)
                    (slot e.XSlot) (slot e.YSlot)
                    (slot c.XSlot) (slot c.YSlot)
                    cw
                |> ignore)
        IrCodec.sketch ir firstPrim sk.Primitives.Length sk.Closed sk.Flip

/// Build a serialized kernel-IR blob from the editor's field surfaces and
/// current slot values. Returns `None` when there's nothing to render so
/// the caller can clear the display instead of uploading an empty tape.
let build (surfaces: FieldSurface list) (values: float array) : obj option =
    match surfaces with
    | [] -> None
    | _ ->
        let ir = IrCodec.create ()
        let roots =
            surfaces |> List.map (fun s -> compileNode ir values s.Field)
        let root = roots |> List.reduce (fun a b -> IrCodec.union ir a b)
        Some (IrCodec.serialize ir root)
