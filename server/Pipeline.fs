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
      Slots: SlotTable }

module Pipeline =

    // ── Slot allocation for display + field-slice settings ────────────────
    // Allocated for every Field-type action. If the action's Display/FieldSlice
    // is None, uses the default values as seeds.

    let private allocDisplaySlots (b: SlotTable.Builder) (action: DocAction) =
        let d = action.Display |> Option.defaultValue DisplaySettings.defaults
        let id = action.Id
        let r = { ActionId = id; Path = "display.color.0" }
        SlotTable.alloc b r d.Color.[0] |> ignore
        SlotTable.alloc b { ActionId = id; Path = "display.color.1" } d.Color.[1] |> ignore
        SlotTable.alloc b { ActionId = id; Path = "display.color.2" } d.Color.[2] |> ignore
        SlotTable.alloc b { ActionId = id; Path = "display.opacity" } d.Opacity |> ignore
        SlotTable.alloc b { ActionId = id; Path = "display.isoValue" } d.IsoValue |> ignore

    let private allocFieldSliceSlots (b: SlotTable.Builder) (action: DocAction) =
        let fs = action.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
        SlotTable.alloc b { ActionId = action.Id; Path = "fieldSlice.offset" } fs.Offset |> ignore

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

        // Note: label positions stay in topology (not slots) for now — they
        // need a "set vs unset" signal that NaN sentinels break for JSON.
        // Revisit when the sketch canvas UI lands.
        s.Constraints |> List.iteri (fun i c ->
            match c with
            | Fixed(_, x, y) ->
                a (sprintf "sketch.constraint.%d.x" i) x
                a (sprintf "sketch.constraint.%d.y" i) y
            | Distance(_, _, dist, _)
            | FrameDistance(_, _, _, dist, _)
            | LineDistance(_, _, _, _, _, _, dist, _)
            | FrameLineDistance(_, _, _, _, _, dist, _)
            | PointLineDistance(_, _, _, _, dist, _)
            | FramePointLineDistance(_, _, _, dist, _)
            | PointCircleDistance(_, _, _, dist, _)
            | LineCircleDistance(_, _, _, _, _, dist, _)
            | CircleCircleDistance(_, _, _, _, dist, _, _) ->
                a (sprintf "sketch.constraint.%d.distance" i) dist
            | CircleDiameter(_, _, diam, _) ->
                a (sprintf "sketch.constraint.%d.diameter" i) diam
            | Angle(_, _, _, _, _, _, deg, _, _, _, _) ->
                a (sprintf "sketch.constraint.%d.angleDegrees" i) deg
            | Tangent(_, _, _, _, _, radius) ->
                a (sprintf "sketch.constraint.%d.radius" i) radius
            | _ -> ())

    // ── Full pipeline ─────────────────────────────────────────────────────

    let compile (actions: DocAction list) : PipelineResult =
        let tc = TypeCheck.typecheck actions
        let typeMap = tc.Typed |> List.map (fun t -> t.Id, t.Output) |> Map.ofList

        let buildResult = Element.build actions typeMap
        let b = SlotTable.createBuilder ()

        // Compile field surfaces (allocates primitive/transform/boolean slots)
        let surfaces = FieldCompile.compile actions buildResult.Elements b

        // Allocate display + fieldSlice slots for Field-type actions,
        // and sketch slots for Sketch-type actions.
        for action in actions do
            match Map.tryFind action.Id typeMap with
            | Some FieldType.Field ->
                allocDisplaySlots b action
                allocFieldSliceSlots b action
            | Some FieldType.Sketch ->
                match action.Kind with
                | Sketch(_, s) -> allocSketchSlots b action.Id s
                | _ -> ()
            | _ -> ()

        { Surfaces = surfaces
          TypeMap = typeMap
          Errors = tc.Errors
          Slots = SlotTable.toTable b }
