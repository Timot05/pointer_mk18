namespace Server

// ---------------------------------------------------------------------------
// DocumentPipeline — projects EditorState into the model the sidebar /
// document UI renders. Pure consumer of Editor; never mutates state.
// ---------------------------------------------------------------------------

type DocumentView =
    { Name: string
      Actions: DocAction list
      SelectedId: string option
      ExpandedActionIds: Set<ActionId>
      EditFocusIdx: int
      EditingInputField: ActionParamField option
      EditingInputInitial: string option
      RefPickIdx: int
      ActionPickerOpen: bool
      SelectedTargets: SelectionTarget list
      SketchUi: SketchUiState
      RefOptions: Map<string, string list>
      SketchLoops: Map<string, SketchLoopView list>
      TypeMap: Map<ActionId, FieldType>
      Errors: ActionErrorView list
      // Notebook-mode fields
      Blocks: Server.Lang.Notebook.Block list
      SelectedBlockId: Server.Lang.Notebook.BlockId option
      OpenedScriptBlockId: Server.Lang.Notebook.BlockId option
      LastNotebookError: string option }

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

        let sketchLoops =
            state.Doc.Actions
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
          Actions = state.Doc.Actions
          SelectedId = state.Doc.SelectedId
          ExpandedActionIds = state.ExpandedActionIds
          EditFocusIdx = state.EditFocusIdx
          EditingInputField = state.EditingInputField
          EditingInputInitial = state.EditingInputInitial
          RefPickIdx = state.RefPickIdx
          ActionPickerOpen = state.ActionPickerOpen
          SelectedTargets = state.SelectedTargets
          SketchUi = Editor.sketchUiState state
          RefOptions = refOptions
          SketchLoops = sketchLoops
          TypeMap = tm
          Errors = errors
          Blocks = state.Doc.Blocks
          SelectedBlockId = state.Doc.SelectedBlockId
          OpenedScriptBlockId = state.OpenedScriptBlockId
          LastNotebookError = state.LastNotebookError }

    let paletteView (state: EditorState) =
        Palette.toState state.PaletteSession state.Compiled.TypeMap state.Doc
