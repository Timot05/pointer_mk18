namespace Server.Lang

// ---------------------------------------------------------------------------
// Notebook.fs — port of pointer_mk19/compiler/lib/notebook.ml.
//
// Pure data shapes for a notebook of blocks plus its evaluation result. The
// driver lives in NotebookEval.fs.
// ---------------------------------------------------------------------------

module Notebook =

    open Token
    open Value

    type BlockId = int

    /// A Script block: source text + a wiring table of (input name → DSL
    /// expression text). Input expressions evaluate against the prior
    /// scope only when the block calls `@input(name)`.
    type Script = {
        Source: string
        Inputs: (string * string) list
    }

    /// Sketch block payload. Stores the same `ActionSketch` shape mk18's
    /// authoring UI produces (entities + constraints + float coords) plus
    /// the plane the sketch lives in. Consumer-side lowering is a separate
    /// concern.
    type SketchData = {
        Sketch: Server.ActionSketch
        Plane:  Server.SketchPlane
    }

    type BlockKind =
        | ScriptBlock of Script
        | SketchBlock of SketchData

    type Block = {
        Id: BlockId
        Name: string
        Kind: BlockKind
    }

    type Notebook = {
        NextId: BlockId
        Blocks: Block list
    }

    type IoKind =
        | InputIo
        | OutputIo
        | ViewIo
        | PrintIo
        | DebugIo

    /// Single recorded `@input` / `@output` / `@view` / `@print` / `@debug`
    /// call. The `Name` field is the binding name for input/output/print
    /// (empty for view/debug).
    type IoBinding = {
        Kind: IoKind
        Name: string
        Span: Span
        Value: Value
    }

    type BlockEval = {
        Id: BlockId
        Outputs: (string * Value) list
        IoTrace: IoBinding list
        InputsUsed: string list
        View: Value option
        Error: EvalError option
    }

    type Evaluation = {
        PerBlock: BlockEval list
        Scope: Map<string, Value>
        Ir: MathIr.MathIR
    }
