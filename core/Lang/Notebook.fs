namespace Server.Lang

// ---------------------------------------------------------------------------
// Notebook.fs — typed-block notebook data model.
//
// Each block is a *function*: a curried lambda (described by a `BlockSpec`)
// with declared inputs and a single output. A block instance pairs a spec
// name with a map from input name to `Ast.Expr`. The compose pass
// (NotebookCompose.fs) splices those expressions verbatim into the program
// AST, so any DSL form — a scalar literal, an upstream block reference,
// a path into a structured value (`profile.loop_0`), or a richer
// expression — is valid as a block input.
//
// The active driver lives in NotebookCompose.fs.
// ---------------------------------------------------------------------------

module Notebook =

    open Token
    open Ast
    open Value

    type BlockId = int

    /// Sketch payload. Sketch blocks are kind-special: their author-time
    /// data structure (entities + constraints) doesn't fit the typed
    /// expression arg shape. The driver picks them up via a separate
    /// code path; their "input" surface in the UI is the sketch
    /// authoring panel, not a list of typed-arg rows.
    type SketchData = {
        Sketch: Server.ActionSketch
        Plane:  Server.SketchPlane
    }

    /// Reference-image plane payload. Image blocks are purely visual:
    /// they render a flat textured quad in 3D space (the image fetched
    /// from `Url`) and contribute no SDF / no field output. Useful as
    /// a CAD-blueprint overlay — drop a 3-view drawing in place and
    /// sketch over it. The texture lives in the viewer's per-block
    /// texture cache; this record stores only the data needed to
    /// position the quad.
    type ImageData = {
        Url:     string
        Plane:   Server.SketchPlane
        Origin:  Server.Vec3
        Width:   float
        Height:  float
        Opacity: float
        /// In-plane rotation, in degrees, about the plane's normal
        /// through `Origin`. 0 leaves the image axis-aligned to the
        /// plane's local X/Y; positive values rotate counter-clockwise
        /// as viewed looking down the plane's normal.
        Rotation: float
    }

    /// What a block actually is at the data level. Native blocks are
    /// `(specName, args)` where each arg is a full DSL expression —
    /// typically a scalar literal (`ENumber n`), a reference to an
    /// upstream block (`EVar name`), or a path into a structured
    /// value (`EPath [name; "loop_0"]`). Anything Ast.Expr can
    /// represent is a valid arg; the compose pass splices it
    /// verbatim into the program AST. Absence of a key means the
    /// slot is unwired (compose-time fallback to
    /// `AstBuilder.unwiredE`). Sketches carry their own structured
    /// payload — see `SketchData`. Image blocks carry an
    /// `ImageData` for URL + transform; they emit no field.
    type BlockBody =
        | NativeBody of specName: string * args: Map<string, Expr>
        | SketchBody of SketchData
        | ImageBody  of ImageData

    /// What the viewer should do with this block's output. `VHidden` means
    /// the block contributes to downstream wires but isn't rendered itself;
    /// `VIsosurface` is the default 3D surface render; `VFieldLines` is
    /// an iso-contour overlay drawn on the block's `SlicePlane`.
    type BlockVisibility =
        | VHidden
        | VIsosurface
        | VFieldLines

    /// Plane through space on which the viewer rasterises a Field block's
    /// SDF as iso-contour lines (only consulted when `Visibility =
    /// VFieldLines`). `AxisX` and `AxisY` span the plane; the rendered quad
    /// is `Origin ± Extent` along each. Defaults to the world XY plane
    /// through the origin with extent 20 (see `defaultSlicePlane`).
    type SlicePlane = {
        Origin: Server.Vec3
        AxisX:  Server.Vec3
        AxisY:  Server.Vec3
        Extent: float
    }

    let defaultSlicePlane : SlicePlane =
        { Origin = { X = 0.0; Y = 0.0; Z = 0.0 }
          AxisX  = { X = 1.0; Y = 0.0; Z = 0.0 }
          AxisY  = { X = 0.0; Y = 1.0; Z = 0.0 }
          Extent = 20.0 }

    type Block = {
        Id: BlockId
        Name: string
        Body: BlockBody
        Visibility: BlockVisibility
        /// Index into the renderer's fixed field-colour palette. This is a
        /// display setting, not part of the block's value semantics.
        ColorIndex: int
        /// Slice plane used when `Visibility = VFieldLines`. Carried on every
        /// block so toggling visibility kinds preserves the user's plane
        /// choice. Initialised to `defaultSlicePlane` for new blocks.
        SlicePlane: SlicePlane
    }

    type Notebook = {
        NextId: BlockId
        Blocks: Block list
    }
