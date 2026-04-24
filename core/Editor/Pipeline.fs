namespace Server

// ---------------------------------------------------------------------------
// Compilation pipeline — chains typecheck → element tree → field IR.
// Always produces a type map, surfaces, and a slot table. Errors are
// collected but don't prevent compilation of valid actions.
// ---------------------------------------------------------------------------

type PipelineResult =
    { Surfaces: FieldSurface list
      TypeMap: Map<ActionId, FieldType>
      Errors: TypeError list
      Slots: SlotTable
      Pickables: Pickable list
      Frames: Map<ActionId, FrameChain> }

module Pipeline =

    let private allocFrameSlots (b: SlotTable.Builder) (action: DocAction) =
        match action.Kind with
        | Translate(_, x, y, z) ->
            SlotTable.alloc b { ActionId = action.Id; Path = "x" } x |> ignore
            SlotTable.alloc b { ActionId = action.Id; Path = "y" } y |> ignore
            SlotTable.alloc b { ActionId = action.Id; Path = "z" } z |> ignore
        | Rotate(_, ax, ay, az, angle) ->
            SlotTable.alloc b { ActionId = action.Id; Path = "ax" } ax |> ignore
            SlotTable.alloc b { ActionId = action.Id; Path = "ay" } ay |> ignore
            SlotTable.alloc b { ActionId = action.Id; Path = "az" } az |> ignore
            SlotTable.alloc b { ActionId = action.Id; Path = "angle" } angle |> ignore
        | _ ->
            ()

    // ── Slot allocation for sketch entities & constraints ────────────────

    let private allocSketchSlots (b: SlotTable.Builder) (actionId: ActionId) (s: ActionSketch) =
        let a path v = SlotTable.alloc b { ActionId = actionId; Path = path } v |> ignore

        for entity in s.Entities do
            match entity with
            | REPoint(eid, x, y) ->
                a (sprintf "sketch.entity.%s.x" eid) x
                a (sprintf "sketch.entity.%s.y" eid) y
            | RELine _ -> ()
            | RECircle(eid, _center, radius) ->
                a (sprintf "sketch.entity.%s.radius" eid) radius
            | REArc(eid, _s, _e, ArcThreePoint through) ->
                a (sprintf "sketch.entity.%s.throughX" eid) through.X
                a (sprintf "sketch.entity.%s.throughY" eid) through.Y
            | REArc _ -> ()  // ArcCenter: no numeric slots (clockwise is bool)

        // Label positions are slots too. When the constraint's LabelPos is
        // None, we allocate with default 0.0 — the topology's `labelPosition`
        // field (which remains Option) tells the renderer whether to read the
        // slots or fall back to auto-layout. Once the user drags the label,
        // the option becomes Some and the slot values carry the real position.
        let labelXY i (lp: LabelPos option) =
            let pos = lp |> Option.defaultValue { X = 0.0; Y = 0.0 }
            a (sprintf "sketch.constraint.%d.labelPosition.x" i) pos.X
            a (sprintf "sketch.constraint.%d.labelPosition.y" i) pos.Y

        s.Constraints |> List.iteri (fun i c ->
            match c with
            | Fixed(_, x, y) ->
                a (sprintf "sketch.constraint.%d.x" i) x
                a (sprintf "sketch.constraint.%d.y" i) y
            | Distance(_, _, dist, lp)
            | FrameDistance(_, _, _, dist, lp)
            | LineDistance(_, _, _, _, _, _, dist, lp)
            | FrameLineDistance(_, _, _, _, _, dist, lp)
            | PointLineDistance(_, _, _, _, dist, lp)
            | PointCircleDistance(_, _, _, dist, lp)
            | LineCircleDistance(_, _, _, _, _, dist, lp)
            | CircleCircleDistance(_, _, _, _, dist, _, lp) ->
                a (sprintf "sketch.constraint.%d.distance" i) dist
                labelXY i lp
            | CircleDiameter(_, _, diam, lp) ->
                a (sprintf "sketch.constraint.%d.diameter" i) diam
                labelXY i lp
            | Angle(_, _, _, _, _, _, angle, _, _, _, lp) ->
                a (sprintf "sketch.constraint.%d.angle" i) angle
                labelXY i lp
            | Tangent(_, _, _, _, _, radius) ->
                a (sprintf "sketch.constraint.%d.radius" i) radius
            | _ -> ())

    // ── Pickable list construction ────────────────────────────────────────
    //
    // Walks actions in authored order, allocating sequential PickIds.
    // Reuses the slot keys already allocated by the sketch/surface compile,
    // so coord refs are shared with the SDF shader's param buffer.

    let private slotOf (b: SlotTable.Builder) (actionId: ActionId) (path: string) : Slot =
        // Idempotent lookup — the slot was already allocated upstream with
        // a real default; this call returns the same index.
        SlotTable.alloc b { ActionId = actionId; Path = path } 0.0

    let private ptSlot (b: SlotTable.Builder) (sketchId: ActionId) (pointId: string) : SlotPt2 =
        { XSlot = slotOf b sketchId (sprintf "sketch.entity.%s.x" pointId)
          YSlot = slotOf b sketchId (sprintf "sketch.entity.%s.y" pointId) }

    let private labelSlot (b: SlotTable.Builder) (sketchId: ActionId) (i: int) : SlotPt2 =
        { XSlot = slotOf b sketchId (sprintf "sketch.constraint.%d.labelPosition.x" i)
          YSlot = slotOf b sketchId (sprintf "sketch.constraint.%d.labelPosition.y" i) }

    let private buildSketchPickables
        (b: SlotTable.Builder)
        (counter: int ref)
        (sketchId: ActionId)
        (sketch: ActionSketch)
        : Pickable list =
        let nextId () =
            let id = counter.Value
            counter.Value <- id + 1
            id

        let entityPickables =
            sketch.Entities
            |> List.choose (fun e ->
                match e with
                | REPoint(eid, _, _) ->
                    let x = slotOf b sketchId (sprintf "sketch.entity.%s.x" eid)
                    let y = slotOf b sketchId (sprintf "sketch.entity.%s.y" eid)
                    Some (PickPoint(nextId(), sketchId, eid, x, y))
                | RELine(eid, startId, endId) ->
                    Some (PickLine(nextId(), sketchId, eid, ptSlot b sketchId startId, ptSlot b sketchId endId))
                | RECircle(eid, centerId, _) ->
                    let r = slotOf b sketchId (sprintf "sketch.entity.%s.radius" eid)
                    Some (PickCircle(nextId(), sketchId, eid, ptSlot b sketchId centerId, r))
                | REArc(eid, startId, endId, ArcCenter(centerId, cw)) ->
                    Some (PickArc(nextId(), sketchId, eid,
                                  ptSlot b sketchId startId,
                                  ptSlot b sketchId endId,
                                  ptSlot b sketchId centerId,
                                  cw))
                | REArc(_, _, _, ArcThreePoint _) -> None)

        let loopPickables =
            SketchLoops.detectLoops sketch.Entities
            |> List.map (fun loop ->
                PickLoop(nextId(), sketchId, loop.Id, loop.EntityIds))

        let dimensionPickables =
            sketch.Constraints
            |> List.mapi (fun i c -> i, c)
            |> List.choose (fun (i, c) ->
                let hasLabel =
                    match c with
                    | Distance _ | FrameDistance _ | LineDistance _ | FrameLineDistance _
                    | PointLineDistance _ | PointCircleDistance _
                    | LineCircleDistance _ | CircleCircleDistance _ | CircleDiameter _ | Angle _ -> true
                    | _ -> false
                if hasLabel then
                    Some (PickDimension(nextId(), sketchId, i, labelSlot b sketchId i))
                else None)

        entityPickables @ loopPickables @ dimensionPickables

    let private buildPickables
        (b: SlotTable.Builder)
        (actions: DocAction list)
        (typeMap: Map<ActionId, FieldType>)
        : Pickable list =
        let counter = ref 0
        let nextId () =
            let id = counter.Value
            counter.Value <- id + 1
            id

        actions
        |> List.collect (fun action ->
            match Map.tryFind action.Id typeMap with
            | Some FieldType.Frame ->
                // One pickable per frame — the whole gizmo (origin + axes)
                // picks as the same `TargetFrameOrigin`. Per-axis picks
                // via `PickFrameAxis` were retired; sketch constraints
                // only need the frame identity. The DU case is kept so
                // the pattern matches elsewhere still typecheck.
                [ PickFrameOrigin(nextId(), action.Id) ]
            | Some FieldType.Sketch ->
                match action.Kind with
                | Sketch(_, _, sk) -> buildSketchPickables b counter action.Id sk
                | _ -> []
            | _ -> [])

    let private allocActionSlots
        (typeMap: Map<ActionId, FieldType>)
        (b: SlotTable.Builder)
        (action: DocAction)
        : SlotTable.Builder =
        // Translate / Rotate always get their x/y/z (or axis/angle)
        // slots, regardless of output type. A Field-output Translate
        // with an unwired child otherwise skips slot allocation
        // (FieldIR bails on the empty child), and the gizmo-drag
        // path can't find its slots to patch. `allocFrameSlots`
        // no-ops for non-Translate/Rotate kinds.
        allocFrameSlots b action
        match Map.tryFind action.Id typeMap, action.Kind with
        | Some FieldType.Sketch, Sketch(_, _, sketch) ->
            allocSketchSlots b action.Id sketch
            b
        | _ ->
            b

    // ── Full pipeline ─────────────────────────────────────────────────────

    let compile (actions: DocAction list) : PipelineResult =
        let tc = TypeCheck.typecheck actions
        let typeMap = tc.Typed |> List.map (fun t -> t.Id, t.Output) |> Map.ofList

        let buildResult = Element.build actions typeMap
        let b = SlotTable.createBuilder ()

        // Compile field surfaces (allocates primitive/transform/boolean slots)
        let surfaces = FieldCompile.compile actions buildResult.Elements b

        // Allocate action-owned runtime slots from the typed action
        // stream: frames contribute transform slots; sketches
        // contribute entity/constraint slots.
        actions |> List.fold (allocActionSlots typeMap) b |> ignore

        let pickables = buildPickables b actions typeMap

        { Surfaces = surfaces
          TypeMap = typeMap
          Errors = tc.Errors
          Slots = SlotTable.toTable b
          Pickables = pickables
          Frames = buildResult.Frames }
