namespace Server

type SketchSolveBinding =
    { LocalFields: ActionParamField[]
      LocalToGlobal: int[]
      VarIndexByLocal: Map<int, int> }

module SketchSolve =

    let localFields (sketch: ActionSketch) : ActionParamField[] =
        [|
            for entity in sketch.Entities do
                match entity with
                | REPoint(id, _, _) ->
                    yield SketchEntityField(id, PointX)
                    yield SketchEntityField(id, PointY)
                | RECircle(id, _, _) ->
                    yield SketchEntityField(id, CircleRadius)
                | REArc(id, _, _, ArcThreePoint _) ->
                    yield SketchEntityField(id, ArcThroughX)
                    yield SketchEntityField(id, ArcThroughY)
                | _ -> ()

            for index, sketchConstraint in sketch.Constraints |> List.indexed do
                match sketchConstraint with
                | Distance _
                | FrameDistance _
                | LineDistance _
                | FrameLineDistance _
                | PointLineDistance _
                | PointCircleDistance _
                | LineCircleDistance _
                | CircleCircleDistance _ ->
                    yield SketchConstraintField(index, ConstraintDistance)
                | CircleDiameter _ ->
                    yield SketchConstraintField(index, ConstraintDiameter)
                | Angle _ ->
                    yield SketchConstraintField(index, ConstraintAngle)
                | _ -> ()
        |]

    let binding (slots: SlotTable) (sketchId: string) (sketch: ActionSketch) (varSlots: int[]) : SketchSolveBinding =
        let fields = localFields sketch

        let localToGlobal =
            fields
            |> Array.map (fun field ->
                let path = Document.pathOfParamField field
                Map.find { ActionId = sketchId; Path = path } slots.Index)

        let varIndexByLocal =
            varSlots
            |> Array.mapi (fun varIndex localSlot -> localSlot, varIndex)
            |> Map.ofArray

        { LocalFields = fields
          LocalToGlobal = localToGlobal
          VarIndexByLocal = varIndexByLocal }

    let buildPins (xField: ActionParamField) (yField: ActionParamField) (target: LabelPos) (binding: SketchSolveBinding) : SolverPin list =
        let findLocal field =
            binding.LocalFields |> Array.tryFindIndex ((=) field)

        match findLocal xField, findLocal yField with
        | Some xLocal, Some yLocal ->
            match Map.tryFind xLocal binding.VarIndexByLocal, Map.tryFind yLocal binding.VarIndexByLocal with
            | Some xVar, Some yVar ->
                [ { LocalSlot = xLocal
                    VarIndex = xVar
                    Target = target.X
                    Weight = 20.0 }
                  { LocalSlot = yLocal
                    VarIndex = yVar
                    Target = target.Y
                    Weight = 20.0 } ]
            | _ ->
                []
        | _ ->
            []

    let overlaySolvedSketch (baseParams: float[]) (slots: SlotTable) (sketchId: string) (sketch: ActionSketch) (solvedLocal: float32[]) : float[] =
        let overlaid = Array.copy baseParams
        let fields = localFields sketch
        let count = min fields.Length solvedLocal.Length

        for i in 0 .. count - 1 do
            let path = Document.pathOfParamField fields.[i]
            let globalSlot = Map.find { ActionId = sketchId; Path = path } slots.Index
            if globalSlot < overlaid.Length then
                overlaid.[globalSlot] <- float solvedLocal.[i]

        overlaid

    let commitSolvedSketch (sketchId: string) (solvedLocal: float32[]) (doc: Document) : Document =
        match doc.Actions |> List.tryFind (fun action -> action.Id = sketchId) with
        | Some { Kind = Sketch(_, _, sketch) } ->
            let fields = localFields sketch
            let count = min fields.Length solvedLocal.Length

            ((doc, [ 0 .. count - 1 ])
             ||> List.fold (fun current index ->
                 Document.patchParamValue sketchId fields.[index] (VFloat(float solvedLocal.[index])) current))
        | _ ->
            doc
