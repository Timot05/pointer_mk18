namespace Server

// ---------------------------------------------------------------------------
// BlockCompile — replacement for the old action-graph `Pipeline.compile`.
// Produces only what the notebook-mode sketch authoring path needs:
// a slot table (entity/constraint coords) and a pickable list for the
// sketch entities of every `SketchBlock`.
//
// All field-IR / element-tree / typecheck output is gone; the new
// rendering path is the kernel-backed MathIR push (`MathIrCodec` →
// `Background.updateIr`), built directly from `NotebookCompose.compile`.
// ---------------------------------------------------------------------------

/// Strict subset of the old `PipelineResult`: just the slot table and
/// pickable list that sketch authoring + picking still need.
type BlockCompiled =
    { Slots: SlotTable
      Pickables: Pickable list }

module BlockCompiled =

    let empty : BlockCompiled =
        { Slots = { Values = [||]; Index = Map.empty }
          Pickables = [] }

module BlockCompile =

    let private slotOf (b: SlotTable.Builder) (sketchId: ActionId) (path: string) : Slot =
        SlotTable.alloc b { ActionId = sketchId; Path = path } 0.0

    let private ptSlot (b: SlotTable.Builder) (sketchId: ActionId) (pointId: string) : SlotPt2 =
        { XSlot = slotOf b sketchId (sprintf "sketch.entity.%s.x" pointId)
          YSlot = slotOf b sketchId (sprintf "sketch.entity.%s.y" pointId) }

    let private labelSlot (b: SlotTable.Builder) (sketchId: ActionId) (i: int) : SlotPt2 =
        { XSlot = slotOf b sketchId (sprintf "sketch.constraint.%d.labelPosition.x" i)
          YSlot = slotOf b sketchId (sprintf "sketch.constraint.%d.labelPosition.y" i) }

    let private allocSketchSlots (b: SlotTable.Builder) (sketchId: ActionId) (s: ActionSketch) =
        let a path v = SlotTable.alloc b { ActionId = sketchId; Path = path } v |> ignore

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
            | REArc _ -> ()

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

    let compile (blocks: Server.Lang.Notebook.Block list) : BlockCompiled =
        let b = SlotTable.createBuilder ()
        let counter = ref 0

        for block in blocks do
            match block.Body with
            | Server.Lang.Notebook.SketchBody data ->
                allocSketchSlots b (Server.SketchAuthoring.blockSketchId block.Id) data.Sketch
            | _ -> ()

        let pickables =
            blocks
            |> List.collect (fun block ->
                match block.Body with
                | Server.Lang.Notebook.SketchBody data ->
                    buildSketchPickables b counter (Server.SketchAuthoring.blockSketchId block.Id) data.Sketch
                | _ -> [])

        { Slots = SlotTable.toTable b
          Pickables = pickables }
