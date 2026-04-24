namespace Server

// ---------------------------------------------------------------------------
// Sketch data model — rendering entities and constraints.
// No solving logic, no field compilation; just the types.
// ---------------------------------------------------------------------------

/// A 2D point with no identity (used for Arc "through" point).
type FreePoint =
    { X: float
      Y: float }

/// Arc can be defined in two modes:
/// - Center: center point + clockwise flag
/// - ThreePoint: a through-point on the arc
type ArcData =
    | ArcCenter of center: string * clockwise: bool
    | ArcThreePoint of through: FreePoint

/// Drawable entities in a sketch. Points own their (x, y); other entities
/// reference points by id.
type RenderEntity =
    | REPoint of id: string * x: float * y: float
    | RELine of id: string * startId: string * endId: string
    | RECircle of id: string * center: string * radius: float
    | REArc of id: string * startId: string * endId: string * data: ArcData

/// 2D coordinate for label positioning on dimensional constraints.
type LabelPos = { X: float; Y: float }

/// All constraint types the sketch solver supports.
/// Variants cover position, alignment, distance, angle, tangency, etc.
type SketchConstraint =
    | Fixed of point: string * x: float * y: float
    | Coincident of a: string * b: string
    | FrameCoincident of point: string * frame: string * part: string
    | Concentric of entityA: string * entityB: string * centerA: string * centerB: string
    | Horizontal of a: string * b: string
    | Vertical of a: string * b: string
    | Distance of a: string * b: string * distance: float * labelPosition: LabelPos option
    | FrameDistance of point: string * frame: string * part: string * distance: float * labelPosition: LabelPos option
    | Equal of aStart: string * aEnd: string * bStart: string * bEnd: string * lineA: string * lineB: string
    | EqualRadius of entityA: string * entityB: string
    | Midpoint of point: string * lineA: string * aStart: string * aEnd: string
    | Parallel of aStart: string * aEnd: string * bStart: string * bEnd: string * lineA: string * lineB: string
    | FrameParallel of aStart: string * aEnd: string * lineA: string * frame: string * part: string
    | Perpendicular of aStart: string * aEnd: string * bStart: string * bEnd: string * lineA: string * lineB: string
    | FramePerpendicular of aStart: string * aEnd: string * lineA: string * frame: string * part: string
    | Tangent of aStart: string * aEnd: string * center: string * circle: string * lineA: string * radius: float
    | CurveTangent of entityA: string * centerA: string * entityB: string * centerB: string * ``internal``: bool
    | CircleDiameter of circle: string * center: string * diameter: float * labelPosition: LabelPos option
    | LineDistance of aStart: string * aEnd: string * bStart: string * bEnd: string * lineA: string * lineB: string * distance: float * labelPosition: LabelPos option
    | FrameLineDistance of lineA: string * aStart: string * aEnd: string * frame: string * part: string * distance: float * labelPosition: LabelPos option
    | PointLineDistance of point: string * lineA: string * aStart: string * aEnd: string * distance: float * labelPosition: LabelPos option
    | PointCircleDistance of point: string * circle: string * center: string * distance: float * labelPosition: LabelPos option
    | LineCircleDistance of lineA: string * aStart: string * aEnd: string * circle: string * center: string * distance: float * labelPosition: LabelPos option
    | CircleCircleDistance of circleA: string * centerA: string * circleB: string * centerB: string * distance: float * ``internal``: bool * labelPosition: LabelPos option
    | Angle of aStart: string * aEnd: string * bStart: string * bEnd: string * lineA: string * lineB: string * angle: float * aReverse: bool * bReverse: bool * ccwFromAToB: bool * labelPosition: LabelPos option

module SketchConstraint =
    /// Extract the optional label position from any constraint variant.
    /// Exhaustive: when a new variant is added, this match forces a decision here.
    let labelPos (c: SketchConstraint) : LabelPos option =
        match c with
        | Distance(_, _, _, lp)
        | FrameDistance(_, _, _, _, lp)
        | LineDistance(_, _, _, _, _, _, _, lp)
        | FrameLineDistance(_, _, _, _, _, _, lp)
        | PointLineDistance(_, _, _, _, _, lp)
        | PointCircleDistance(_, _, _, _, lp)
        | LineCircleDistance(_, _, _, _, _, _, lp)
        | CircleCircleDistance(_, _, _, _, _, _, lp)
        | CircleDiameter(_, _, _, lp)
        | Angle(_, _, _, _, _, _, _, _, _, _, lp) -> lp
        | Fixed _
        | Coincident _
        | FrameCoincident _
        | Concentric _
        | Horizontal _
        | Vertical _
        | Equal _
        | EqualRadius _
        | Midpoint _
        | Parallel _
        | FrameParallel _
        | Perpendicular _
        | FramePerpendicular _
        | Tangent _
        | CurveTangent _ -> None

/// The full content of a Sketch action: a set of entities and the
/// constraints between them.
type ActionSketch =
    { Entities: RenderEntity list
      Constraints: SketchConstraint list }

module ActionSketch =
    let empty : ActionSketch = { Entities = []; Constraints = [] }

type SketchPlane =
    | XY
    | XZ
    | YZ

module SketchPlane =
    let defaults = XY

/// How FromSketch selects geometry from its parent Sketch.
///   AllLoops: every detected closed loop, auto-tracking as the sketch
///             changes. The default for a freshly-created FromSketch.
///   Loops:    a specific subset of loops by id; the user has explicitly
///             toggled at least one off, so we no longer auto-track.
///   Elements: specific lines/arcs to trace (legacy; not used by the
///             current toggle UI but kept for the picking pipeline).
type FromSketchSelection =
    | SelectionAllLoops
    | SelectionLoops of loopIds: string list
    | SelectionElements of lineIds: string list

module FromSketchSelection =
    let defaults = SelectionAllLoops
