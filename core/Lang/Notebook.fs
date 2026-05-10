namespace Server.Lang

// ---------------------------------------------------------------------------
// Notebook.fs — typed-block notebook data model.
//
// Each block is a *function*: a curried lambda (described by a `BlockSpec`)
// with declared inputs and a single output. Block instances pair a spec name
// with the values bound to each input — scalars (drag-edited via a slot),
// refs (wired to upstream blocks), or structured payloads like sketches.
//
// The driver lives in NotebookEval.fs.
// ---------------------------------------------------------------------------

module Notebook =

    open Token
    open Value

    type BlockId = int

    /// A value bound to one of a block's named inputs. Scalars become slot-
    /// backed `VField`s in the eval env; refs resolve to the upstream block's
    /// output. Sketch-shaped payloads (entities + constraints + plane) live
    /// directly on the block since they aren't shareable across kinds.
    type BlockArg =
        | ArgScalar of float
        | ArgRef of BlockId option

    /// Sketch payload. Sketch blocks are kind-special: their author-time
    /// data structure (entities + constraints) doesn't fit the scalar/ref
    /// arg shape. The driver picks them up via a separate code path; their
    /// "input" surface in the UI is the sketch authoring panel, not a
    /// list of typed-arg rows.
    type SketchData = {
        Sketch: Server.ActionSketch
        Plane:  Server.SketchPlane
    }

    /// What a block actually is at the data level. Native blocks are
    /// (specName, args). Sketches carry their own structured payload.
    /// Future user-defined blocks would carry their parsed AST + an arg
    /// map of the same shape as native.
    type BlockBody =
        | NativeBody of specName: string * args: Map<string, BlockArg>
        | SketchBody of SketchData

    /// What the viewer should do with this block's output. `VHidden` means
    /// the block contributes to downstream wires but isn't rendered itself;
    /// `VVisible` is the default opaque surface; `VFieldLines` and
    /// `VIsosurface` are alternate rendering modes for inspecting fields.
    type BlockVisibility =
        | VHidden
        | VVisible
        | VFieldLines
        | VIsosurface

    type Block = {
        Id: BlockId
        Name: string
        Body: BlockBody
        Visibility: BlockVisibility
    }

    type Notebook = {
        NextId: BlockId
        Blocks: Block list
    }

    /// Per-block evaluation result. `Output` is the single value the
    /// block exposes downstream; for sketch blocks this is the wrapped
    /// `VSketch`. Errors are localised to the block — a downstream block
    /// referencing a failed upstream sees its slot as unbound.
    type BlockEval = {
        Id: BlockId
        Output: Value option
        Error: EvalError option
    }

    type Evaluation = {
        PerBlock: BlockEval list
        Outputs: Map<BlockId, Value>
        Ir: MathIr.MathIR
    }
