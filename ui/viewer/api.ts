export interface JsonVec3 { x: number; y: number; z: number }
export interface JsonQuat { w: number; x: number; y: number; z: number }
export interface JsonRigidTransform { rot: JsonQuat; trans: JsonVec3 }

export interface SlotIndexEntry {
  actionId: string;
  path: string;
  slot: number;
}

export type RenderEntity =
  | { case: "REPoint"; id: string; x: number; y: number }
  | { case: "RELine"; id: string; startId: string; endId: string }
  | { case: "RECircle"; id: string; center: string; radius: number }
  | { case: "REArc"; id: string; startId: string; endId: string; data: ArcData };

export type ArcData =
  | { case: "ArcCenter"; center: string; clockwise: boolean }
  | { case: "ArcThreePoint"; through: { x: number; y: number } };

export type SketchConstraint =
  | { case: "Fixed"; point: string; x: number; y: number }
  | { case: "Horizontal"; a: string; b: string }
  | { case: "Vertical"; a: string; b: string }
  | { case: "Distance"; a: string; b: string; distance: number }
  | { case: "CircleDiameter"; circle: string; center: string; diameter: number }
  | { case: "LineDistance"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string; distance: number }
  | { case: "Angle"; aStart: string; aEnd: string; bStart: string; bEnd: string; lineA: string; lineB: string; angle: number; aReverse: boolean; bReverse: boolean; ccwFromAToB: boolean };

export interface ActionSketch {
  entities: RenderEntity[];
  constraints: SketchConstraint[];
}

export interface SketchLoop {
  id: string;
  entityIds: string[];
}

export interface ViewerSketch {
  id: string;
  origin: string | null;
  sketch: ActionSketch;
  loops: SketchLoop[];
}

export interface Pickable {
  case: string;
  pickId: number;
  sketchId?: string;
  frameId?: string;
  part?: string;
  entityId?: string;
  loopId?: string;
  entityIds?: string[];
  actionId?: string;
  constraintIndex?: number;
}

export interface ViewerModel {
  surfaces: unknown[];
  fieldWgsl: string | null;
  fieldSliceWgsl: string | null;
  fieldSurfaceActionIds: string[];
  sketches: ViewerSketch[];
  numSlots: number;
  slotIndex: SlotIndexEntry[];
  pickables: Pickable[];
}

export interface ViewerState {
  params: number[];
  selectedId: string | null;
  hoveredTarget: SelectionTarget | null;
  highlightedTarget: SelectionTarget | null;
  dragTarget: SelectionTarget | null;
  selectedTargets: SelectionTarget[];
  highlightedTargets: SelectionTarget[];
  visibleDimensionSketchIds: string[];
  sketchUi: SketchUiState;
  frames: Array<{ id: string; transform: JsonRigidTransform }>;
  sketchEditFrames: Array<{ id: string; transform: JsonRigidTransform }>;
  sketchTransforms: Array<{ id: string; transform: JsonRigidTransform }>;
  fieldSlices: Array<{
    surfaceIndex: number;
    planeOrigin: JsonVec3;
    planeX: JsonVec3;
    planeY: JsonVec3;
    extent: number;
  }>;
  visible: Record<string, boolean>;
  constraintLabelPositions: Array<{ sketchId: string; constraintIndex: number; position: { x: number; y: number } }>;
  display: Record<string, unknown>;
  errors: Array<{ actionId: string; key: string; error: string }>;
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
  toolPoints: Array<{ x: number; y: number }>;
  editingDimension: { sketchId: string; constraintIndex: number; key: string; value: number } | null;
  constraintPlacementMode: string | null;
  constraintPlacementDraft: { sketchId: string; kind: string; clickedRefs: Array<{ case: string; [key: string]: unknown }> } | null;
  pendingConstraintPlacement: { sketchId: string; constraint: SketchConstraint } | null;
  constraintAvailability: Record<string, boolean>;
  dimensionPlacementAvailability: Record<string, boolean>;
}
