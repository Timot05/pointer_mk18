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
      ExpandedBlockIds: Set<Server.Lang.Notebook.BlockId>
      LastNotebookError: string option
      BlockErrors: Map<Server.Lang.Notebook.BlockId, string list>
      BlockOutputs: Map<Server.Lang.Notebook.BlockId, Server.Lang.Type.T>
      EditingBlockRef: (Server.Lang.Notebook.BlockId * string) option
      /// Whether the Monaco script editor is currently open.
      ScriptEditorOpen: bool
      /// Current script source text, propagated to the editor on each
      /// render so external changes (file load, undo) sync into the
      /// Monaco model.
      ScriptSourceText: string
      /// User-defined specs parsed from `ScriptSourceText`. The BlockList
      /// merges these into the +Add palette and uses them as the source
      /// of truth for parameter editors on user-defined blocks.
      UserScript: Server.Lang.UserScript.Result }

module DocumentPipeline =

    let documentView (state: EditorState) : DocumentView =
        { Name = state.Doc.Name
          SelectedTargets = state.SelectedTargets
          SketchUi = Editor.sketchUiState state
          Blocks = state.Doc.Blocks
          SelectedBlockId = state.Doc.SelectedBlockId
          ExpandedBlockIds = state.ExpandedBlockIds
          LastNotebookError = state.LastNotebookError
          BlockErrors = state.NotebookBlockErrors
          BlockOutputs = state.NotebookBlockOutputs
          EditingBlockRef = state.EditingBlockRef
          ScriptEditorOpen = state.ScriptEditorOpen
          ScriptSourceText = state.Doc.ScriptSourceText
          UserScript = Server.Lang.UserScript.analyze state.Doc.ScriptSourceText }
