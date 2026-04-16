module PointerMk18.Ui.ViewerMessages

open Server

// ---------------------------------------------------------------------------
// Typed message factories for the TS viewer. The main F# UI constructs
// Message values directly; the TS viewer can't, so we expose these small
// wrappers that Fable compiles to plain JS exports. One line each — they
// exist so viewer.ts doesn't have to know Fable's union-tag encoding.
// ---------------------------------------------------------------------------

let viewerHover candidates = ViewerHover candidates
let viewerPick intent candidates = ViewerPick(intent, candidates)
let beginPointDrag sketchId pointId x y =
    BeginSketchDrag
        { SketchId = sketchId
          Kind = DragPoint pointId
          XField = SketchEntityField(pointId, PointX)
          YField = SketchEntityField(pointId, PointY)
          Target = { X = x; Y = y } }

let beginConstraintLabelDrag sketchId constraintIndex x y =
    BeginSketchDrag
        { SketchId = sketchId
          Kind = DragConstraintLabel constraintIndex
          XField = SketchConstraintField(constraintIndex, ConstraintLabelX)
          YField = SketchConstraintField(constraintIndex, ConstraintLabelY)
          Target = { X = x; Y = y } }

let updateSketchDrag x y = UpdateSketchDragTarget { X = x; Y = y }
let finishSketchDrag = FinishSketchDrag
let cancelSketchDrag = CancelSketchDrag
let viewerToolClick x y = ViewerToolClick(x, y)
let viewerPlaceConstraint x y = ViewerPlaceConstraint(x, y)
let viewerDimensionClickTarget = ViewerDimensionClickTarget
let startEditingDimension index = StartEditingDimension index
let commitEditingDimension value = CommitEditingDimension value
let cancelEditingDimension = CancelEditingDimension
let setConstraintPlacementCursor cursor = SetConstraintPlacementCursor cursor
