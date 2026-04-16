namespace Server

// ---------------------------------------------------------------------------
// DocumentPipeline — projects EditorState into the model the sidebar /
// document UI renders. Pure consumer of Editor; never mutates state.
// ---------------------------------------------------------------------------

type DocumentView =
    { Name: string
      Actions: DocAction list
      SelectedId: string option
      SelectedTargets: SelectionTarget list
      SketchUi: SketchUiState
      RefOptions: Map<string, string list>
      SketchLoops: Map<string, SketchLoopView list>
      Errors: ActionErrorView list }

module DocumentPipeline =

    let documentView (state: EditorState) : DocumentView =
        let tm = state.Compiled.TypeMap
        let errors = Editor.formatErrors state.Compiled.Errors
        let refOptions =
            match state.Doc.SelectedId with
            | None -> Map.empty
            | Some selId ->
                match state.Doc.Actions |> List.tryFind (fun a -> a.Id = selId) with
                | None -> Map.empty
                | Some sel ->
                    let selIdx = state.Doc.Actions |> List.findIndex (fun a -> a.Id = selId)
                    let before = state.Doc.Actions |> List.take selIdx
                    let accepted = TypeCheck.acceptedInputs sel.Kind
                    accepted
                    |> Map.map (fun _key types ->
                        before
                        |> List.choose (fun a ->
                            match Map.tryFind a.Id tm with
                            | Some t when List.contains t types -> Some a.Id
                            | _ -> None))

        let actions =
            state.Doc.Actions
            |> List.map (fun a ->
                match Map.tryFind a.Id tm with
                | Some FieldType.Field ->
                    { a with
                        Display = Some(a.Display |> Option.defaultValue DisplaySettings.defaults)
                        FieldSlice = Some(a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults) }
                | _ ->
                    { a with Display = None; FieldSlice = None })

        let sketchLoops =
            actions
            |> List.choose (fun a ->
                match a.Kind with
                | Sketch(_, _, sketch) ->
                    let loops =
                        SketchLoops.detectLoops sketch.Entities
                        |> List.map (fun loop -> { Id = loop.Id; EntityIds = loop.EntityIds })
                    Some(a.Id, loops)
                | _ -> None)
            |> Map.ofList

        { Name = state.Doc.Name
          Actions = actions
          SelectedId = state.Doc.SelectedId
          SelectedTargets = state.SelectedTargets
          SketchUi = Editor.sketchUiState state
          RefOptions = refOptions
          SketchLoops = sketchLoops
          Errors = errors }

    let paletteView (state: EditorState) =
        Palette.toState state.PaletteSession state.Compiled.TypeMap state.Doc
