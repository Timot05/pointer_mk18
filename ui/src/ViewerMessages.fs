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
let viewerToolClick x y = ViewerToolClick(x, y)
let viewerPlaceConstraint x y = ViewerPlaceConstraint(x, y)
let viewerDimensionClickTarget = ViewerDimensionClickTarget
let startEditingDimension index = StartEditingDimension index
let commitEditingDimension value = CommitEditingDimension value
let cancelEditingDimension = CancelEditingDimension
let setConstraintPlacementCursor cursor = SetConstraintPlacementCursor cursor
let patchActionParamValue id key value = PatchActionParamValue(id, key, value)
