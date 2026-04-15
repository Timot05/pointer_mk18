// ---------------------------------------------------------------------------
// Shared UI document types
// ---------------------------------------------------------------------------

export interface DisplaySettings {
  enabled: boolean;
  color: number[];
  opacity: number;
  isoValue: number;
}

export interface FieldSliceSettings {
  enabled: boolean;
  plane: string;
  offset: number;
  extent: number;
}

export interface Action {
  id: string;
  name: string | null;
  kind: ActionKind;
  visible: boolean;
  display: DisplaySettings | null;
  fieldSlice: FieldSliceSettings | null;
}

export interface FreePoint { x: number; y: number; }

export type ArcData =
  | { case: "ArcCenter"; center: string; clockwise: boolean }
  | { case: "ArcThreePoint"; through: FreePoint };

export type RenderEntity =
  | { case: "REPoint"; id: string; x: number; y: number }
  | { case: "RELine"; id: string; startId: string; endId: string }
  | { case: "RECircle"; id: string; center: string; radius: number }
  | { case: "REArc"; id: string; startId: string; endId: string; data: ArcData };

export interface LabelPos { x: number; y: number; }

export type SketchConstraint =
  | { case: "Fixed"; point: string; x: number; y: number }
  | { case: "Coincident"; a: string; b: string }
  | { case: "FrameCoincident"; point: string; frame: string; part: string }
  | { case: "Concentric"; entityA: string; entityB: string; centerA: string; centerB: string }
  | { case: "Horizontal"; a: string; b: string }
  | { case: "Vertical"; a: string; b: string }
  | { case: "Distance"; a: string; b: string; distance: number; labelPosition: LabelPos | null }
  | { case: "FrameDistance"; point: string; frame: string; part: string; distance: number; labelPosition: LabelPos | null }
  | { case: "Equal"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string }
  | { case: "EqualRadius"; entityA: string; entityB: string }
  | { case: "Midpoint"; point: string; lineA: string; aStart: string; aEnd: string }
  | { case: "Parallel"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string }
  | { case: "FrameParallel"; aStart: string; aEnd: string; lineA: string; frame: string; part: string }
  | { case: "Perpendicular"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string }
  | { case: "FramePerpendicular"; aStart: string; aEnd: string; lineA: string; frame: string; part: string }
  | { case: "Tangent"; aStart: string; aEnd: string; center: string; circle: string; lineA: string; radius: number }
  | { case: "CurveTangent"; entityA: string; centerA: string; entityB: string; centerB: string; internal: boolean }
  | { case: "CircleDiameter"; circle: string; center: string; diameter: number; labelPosition: LabelPos | null }
  | { case: "LineDistance"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string; distance: number; labelPosition: LabelPos | null }
  | { case: "FrameLineDistance"; lineA: string; aStart: string; aEnd: string; frame: string; part: string; distance: number; labelPosition: LabelPos | null }
  | { case: "PointLineDistance"; point: string; lineA: string; aStart: string; aEnd: string; distance: number; labelPosition: LabelPos | null }
  | { case: "PointCircleDistance"; point: string; circle: string; center: string; distance: number; labelPosition: LabelPos | null }
  | { case: "LineCircleDistance"; lineA: string; aStart: string; aEnd: string; circle: string; center: string; distance: number; labelPosition: LabelPos | null }
  | { case: "CircleCircleDistance"; circleA: string; centerA: string; circleB: string; centerB: string; distance: number; internal: boolean; labelPosition: LabelPos | null }
  | { case: "Angle"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string; angle: number; aReverse: boolean; bReverse: boolean; ccwFromAToB: boolean; labelPosition: LabelPos | null };

export interface ActionSketch {
  entities: RenderEntity[];
  constraints: SketchConstraint[];
}

export type FromSketchSelection =
  | { case: "SelectionLoop"; loopId: string | null }
  | { case: "SelectionElements"; lineIds: string[] };

export type ActionKind =
  | { case: "Origin" }
  | { case: "Cylinder"; radius: number; height: number }
  | { case: "Sphere"; radius: number }
  | { case: "Box"; width: number; height: number; depth: number }
  | { case: "HalfPlane"; axis: string; offset: number; flip: boolean }
  | { case: "Translate"; child: string | null; x: number; y: number; z: number }
  | { case: "Rotate"; child: string | null; ax: number; ay: number; az: number; angle: number }
  | { case: "Move"; child: string | null; frame: string | null }
  | { case: "Union"; a: string | null; b: string | null; radius: number }
  | { case: "Subtract"; a: string | null; b: string | null; radius: number }
  | { case: "Intersect"; a: string | null; b: string | null; radius: number }
  | { case: "Sketch"; origin: string | null; plane: string; sketch: ActionSketch }
  | { case: "FromSketch"; child: string | null; flip: boolean; selection: FromSketchSelection }
  | { case: "Thicken"; child: string | null; amount: number }
  | { case: "Shell"; child: string | null; thickness: number }
  | { case: "Mesh"; child: string | null; size: number; resolution: number };

export interface ActionError {
  actionId: string;
  key: string;
  error: string;
}

export interface Document {
  name: string;
  actions: Action[];
  selectedId: string | null;
  selectedTargets: SelectionTarget[];
  sketchUi: SketchUiState;
  refOptions: Record<string, string[]>;
  sketchLoops: Record<string, Array<{ id: string; entityIds: string[] }>>;
  errors: ActionError[];
}

export interface SerializedModel {
  name: string;
  actions: Action[];
}

export type SelectionTarget =
  | { case: "TargetPoint"; sketchId: string; entityId: string }
  | { case: "TargetLine"; sketchId: string; entityId: string }
  | { case: "TargetCircle"; sketchId: string; entityId: string }
  | { case: "TargetArc"; sketchId: string; entityId: string }
  | { case: "TargetLoop"; sketchId: string; loopId: string }
  | { case: "TargetDimension"; sketchId: string; constraintIndex: number }
  | { case: "TargetFrameOrigin"; frameId: string }
  | { case: "TargetFrameAxis"; frameId: string; part: string }
  | { case: "TargetSurface"; actionId: string };

export interface SketchUiState {
  editMode: boolean;
  tool: string;
  toolPoints: LabelPos[];
  editingDimension: { sketchId: string; constraintIndex: number; key: string; value: number } | null;
  constraintPlacementMode: string | null;
  constraintPlacementDraft: { sketchId: string; kind: string; clickedRefs: Array<{ case: string; [key: string]: unknown }> } | null;
  pendingConstraintPlacement: { sketchId: string; constraint: SketchConstraint } | null;
  constraintAvailability: Record<string, boolean>;
  dimensionPlacementAvailability: Record<string, boolean>;
}

export interface PaletteItem {
  id: string;
  label: string;
  kind: string;
}

export interface PaletteChip {
  label: string;
  value: string;
}

export interface PaletteScalarField {
  key: string;
  label: string;
  value: number;
}

export interface PaletteState {
  isOpen: boolean;
  mode: string;
  pickedKind: string | null;
  chips: PaletteChip[];
  prompt: string;
  items: PaletteItem[];
  scalarFields: PaletteScalarField[];
  hintBar: string[];
}
