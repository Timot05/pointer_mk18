namespace Server

// ---------------------------------------------------------------------------
// DocumentPipeline — projects EditorState into the model the sidebar /
// document UI renders.
// ---------------------------------------------------------------------------

type DocumentView =
    { Name: string
      SelectedTargets: SelectionTarget list
      SketchUi: SketchUiState
      Blocks: Server.Lang.Notebook.Block list
      SelectedBlockId: Server.Lang.Notebook.BlockId option
      OpenedScriptBlockId: Server.Lang.Notebook.BlockId option
      ExpandedBlockIds: Set<Server.Lang.Notebook.BlockId>
      LastNotebookError: string option
      BlockErrors: Map<Server.Lang.Notebook.BlockId, string list>
      BlockOutputs: Map<Server.Lang.Notebook.BlockId, Server.Lang.Type.T>
      EditingBlockRef: (Server.Lang.Notebook.BlockId * string) option }

module DocumentPipeline =

    let documentView (state: EditorState) : DocumentView =
        { Name = state.Doc.Name
          SelectedTargets = state.SelectedTargets
          SketchUi = Editor.sketchUiState state
          Blocks = state.Doc.Blocks
          SelectedBlockId = state.Doc.SelectedBlockId
          OpenedScriptBlockId = state.OpenedScriptBlockId
          ExpandedBlockIds = state.ExpandedBlockIds
          LastNotebookError = state.LastNotebookError
          BlockErrors = state.NotebookBlockErrors
          BlockOutputs = state.NotebookBlockOutputs
          EditingBlockRef = state.EditingBlockRef }
