namespace Server

// ---------------------------------------------------------------------------
// Compile an ActionSketch into a differentiable Graph (GraphIR).
//
// Entities (points, lines, circles, arcs) become param slots; constraints
// become residual subgraphs whose outputs are appended to Graph.Outputs.
// Solver variables are every param slot NOT pinned by a Fixed constraint.
//
// Frame-based constraints are projected into the sketch's 2D plane using
// the sketch's origin transform and the referenced frame's transform.
// ---------------------------------------------------------------------------

open System
open System.Collections.Generic

/// Which component of a frame a constraint references.
type FramePart =
    | FPOrigin     // the frame's origin as a 2D point
    | FPXAxis      // the frame's +X axis as a 2D direction
    | FPYAxis
    | FPZAxis

/// Geometry in sketch-local 2D coordinates derived from a frame + part.
type FrameGeometry =
    /// A 2D point (used by FPOrigin and as the anchor for axis lines).
    | FGPoint of x: float * y: float
    /// An infinite line through (ox, oy) with direction (dx, dy). dx/dy unit.
    | FGLine  of ox: float * oy: float * dx: float * dy: float

/// Inputs the sketch compiler needs beyond the sketch itself.
type SketchCompileContext =
    { /// Sketch's own origin transform (identity if sketch has no origin).
      SketchOrigin : RigidTransform
      /// All Frame-typed actions resolved to concrete rigid transforms.
      /// Keyed by Frame-action id. (Intentionally `string` not `ActionId`,
      /// so this file does not depend on Domain.fs.)
      Frames : Map<string, RigidTransform> }

module SketchCompile =

    // ── Frame helpers ─────────────────────────────────────────────────────

    let parseFramePart (s: string) : FramePart option =
        match s with
        | "origin" -> Some FPOrigin
        | "xAxis"  -> Some FPXAxis
        | "yAxis"  -> Some FPYAxis
        | "zAxis"  -> Some FPZAxis
        | _ -> None

    /// Project a 3D world point into the sketch's local 2D plane.
    /// The sketch plane is the frame's XY-plane; Z is discarded.
    let projectPoint (sketchOrigin: RigidTransform) (wp: Vec3) : float * float =
        let local = sketchOrigin.Inverse.Apply wp
        local.X, local.Y

    /// Project a 3D world direction into the sketch's local 2D. Returns
    /// None if the projected length is below an epsilon (degenerate).
    let projectDirection (sketchOrigin: RigidTransform) (wd: Vec3) : (float * float) option =
        let local = sketchOrigin.Inverse.Rot.Rotate wd
        let len = sqrt (local.X * local.X + local.Y * local.Y)
        if len < 1e-9 then None
        else Some (local.X / len, local.Y / len)

    /// Resolve a frame + part into 2D sketch-local geometry.
    /// Returns None for unknown parts or degenerate axes.
    let private frameGeometry
        (sketchOrigin: RigidTransform)
        (frameT: RigidTransform)
        (part: FramePart) : FrameGeometry option =

        let ox, oy = projectPoint sketchOrigin frameT.Trans
        match part with
        | FPOrigin -> Some (FGPoint(ox, oy))
        | FPXAxis | FPYAxis | FPZAxis ->
            let axis =
                match part with
                | FPXAxis -> { X = 1.0; Y = 0.0; Z = 0.0 }
                | FPYAxis -> { X = 0.0; Y = 1.0; Z = 0.0 }
                | FPZAxis -> { X = 0.0; Y = 0.0; Z = 1.0 }
                | _       -> Vec3.Zero
            let wd = frameT.Rot.Rotate axis
            match projectDirection sketchOrigin wd with
            | Some (dx, dy) -> Some (FGLine(ox, oy, dx, dy))
            | None -> None

    // ── Entity tables built as we allocate params ─────────────────────────

    type private PointRef =
        { XNode: int; YNode: int; XSlot: int; YSlot: int }

    type private CircleRef =
        { RadiusNode: int; RadiusSlot: int; CenterId: string }

    type private ArcThroughRef =
        { TxNode: int; TyNode: int; TxSlot: int; TySlot: int }

    type private EntityTables =
        { Points:       Dictionary<string, PointRef>
          Circles:      Dictionary<string, CircleRef>
          ArcThrough:   Dictionary<string, ArcThroughRef> }

    // ── Scalar expression helpers (tiny DSL over GraphBuilder) ────────────

    let private constant (b: GraphBuilder) (v: float) = b.Constant v

    /// p2 - p1 → (dx, dy) node pair.
    let private vecSub (b: GraphBuilder) (a: PointRef) (c: PointRef) : int * int =
        b.Sub(c.XNode, a.XNode), b.Sub(c.YNode, a.YNode)

    /// (dx, dy) → dx² + dy²
    let private lenSq (b: GraphBuilder) (dx: int) (dy: int) : int =
        b.Add(b.Mul(dx, dx), b.Mul(dy, dy))

    /// (dx, dy) → √(dx² + dy²)
    let private length (b: GraphBuilder) (dx: int) (dy: int) : int =
        b.Sqrt(lenSq b dx dy)

    /// Dot of (ax, ay) and (bx, by).
    let private dot (b: GraphBuilder) (ax: int) (ay: int) (bx: int) (by: int) : int =
        b.Add(b.Mul(ax, bx), b.Mul(ay, by))

    /// 2D cross z-component: ax*by - ay*bx.
    let private crossZ (b: GraphBuilder) (ax: int) (ay: int) (bx: int) (by: int) : int =
        b.Sub(b.Mul(ax, by), b.Mul(ay, bx))

    // ── Main ──────────────────────────────────────────────────────────────

    let compile (sketch: ActionSketch) (ctx: SketchCompileContext) : Graph =
        let b = GraphBuilder()
        let tables : EntityTables =
            { Points = Dictionary()
              Circles = Dictionary()
              ArcThrough = Dictionary() }
        let fixedSlots = HashSet<int>()

        // Allocate params for every entity.
        for e in sketch.Entities do
            match e with
            | REPoint(id, x, y) ->
                let xSlot = b.ParamCount
                let xNode = b.Param x
                let ySlot = b.ParamCount
                let yNode = b.Param y
                tables.Points[id] <- { XNode = xNode; YNode = yNode; XSlot = xSlot; YSlot = ySlot }
            | RECircle(id, centerId, radius) ->
                let rSlot = b.ParamCount
                let rNode = b.Param radius
                tables.Circles[id] <- { RadiusNode = rNode; RadiusSlot = rSlot; CenterId = centerId }
            | REArc(id, _, _, ArcThreePoint through) ->
                let xSlot = b.ParamCount
                let xNode = b.Param through.X
                let ySlot = b.ParamCount
                let yNode = b.Param through.Y
                tables.ArcThrough[id] <- { TxNode = xNode; TyNode = yNode; TxSlot = xSlot; TySlot = ySlot }
            | RELine _ -> ()
            | REArc _  -> ()   // ArcCenter carries no numeric slots

        let outputs = ResizeArray<int>()
        let mutable skipped = 0

        let tryPoint id = match tables.Points.TryGetValue id with true, p -> Some p | _ -> None
        let tryCircle id = match tables.Circles.TryGetValue id with true, c -> Some c | _ -> None

        let emitDiff a cNode =
            outputs.Add(b.Sub(a, cNode))

        let emitDiffConst a k =
            outputs.Add(b.Sub(a, constant b k))

        for c in sketch.Constraints do
            match c with

            // ── Pinning ───────────────────────────────────────────────
            | Fixed(pid, x, y) ->
                match tryPoint pid with
                | Some p ->
                    emitDiffConst p.XNode x
                    emitDiffConst p.YNode y
                    fixedSlots.Add p.XSlot |> ignore
                    fixedSlots.Add p.YSlot |> ignore
                | None -> skipped <- skipped + 1

            | Coincident(aId, bId) ->
                match tryPoint aId, tryPoint bId with
                | Some pa, Some pb ->
                    outputs.Add(b.Sub(pa.XNode, pb.XNode))
                    outputs.Add(b.Sub(pa.YNode, pb.YNode))
                | _ -> skipped <- skipped + 1

            | Concentric(_, _, caId, cbId) ->
                match tryPoint caId, tryPoint cbId with
                | Some pa, Some pb ->
                    outputs.Add(b.Sub(pa.XNode, pb.XNode))
                    outputs.Add(b.Sub(pa.YNode, pb.YNode))
                | _ -> skipped <- skipped + 1

            // ── Axis-aligned ──────────────────────────────────────────
            | Horizontal(aId, bId) ->
                match tryPoint aId, tryPoint bId with
                | Some pa, Some pb -> outputs.Add(b.Sub(pa.YNode, pb.YNode))
                | _ -> skipped <- skipped + 1

            | Vertical(aId, bId) ->
                match tryPoint aId, tryPoint bId with
                | Some pa, Some pb -> outputs.Add(b.Sub(pa.XNode, pb.XNode))
                | _ -> skipped <- skipped + 1

            // ── Point-point distance ─────────────────────────────────
            | Distance(aId, bId, d, _) ->
                match tryPoint aId, tryPoint bId with
                | Some pa, Some pb ->
                    let dx, dy = vecSub b pa pb
                    emitDiffConst (length b dx dy) d
                | _ -> skipped <- skipped + 1

            // ── Equal-length lines: |a|² = |b|² ─────────────────────
            | Equal(asId, aeId, bsId, beId, _, _) ->
                match tryPoint asId, tryPoint aeId, tryPoint bsId, tryPoint beId with
                | Some aS, Some aE, Some bS, Some bE ->
                    let dxA, dyA = vecSub b aS aE
                    let dxB, dyB = vecSub b bS bE
                    outputs.Add(b.Sub(lenSq b dxA dyA, lenSq b dxB dyB))
                | _ -> skipped <- skipped + 1

            | EqualRadius(aId, bId) ->
                match tryCircle aId, tryCircle bId with
                | Some cA, Some cB ->
                    outputs.Add(b.Sub(cA.RadiusNode, cB.RadiusNode))
                | _ ->
                    // Arcs: EqualRadius involving arcs would compare implicit
                    // radii (|start − center|). Skip for v0.
                    skipped <- skipped + 1

            // ── Midpoint: p = (a + b) / 2 ────────────────────────────
            | Midpoint(pid, _, aStartId, aEndId) ->
                match tryPoint pid, tryPoint aStartId, tryPoint aEndId with
                | Some p, Some aS, Some aE ->
                    let two = constant b 2.0
                    let sumX = b.Add(aS.XNode, aE.XNode)
                    let sumY = b.Add(aS.YNode, aE.YNode)
                    outputs.Add(b.Sub(b.Mul(p.XNode, two), sumX))
                    outputs.Add(b.Sub(b.Mul(p.YNode, two), sumY))
                | _ -> skipped <- skipped + 1

            // ── Parallel / Perpendicular ─────────────────────────────
            | Parallel(asId, aeId, bsId, beId, _, _) ->
                match tryPoint asId, tryPoint aeId, tryPoint bsId, tryPoint beId with
                | Some aS, Some aE, Some bS, Some bE ->
                    let dxA, dyA = vecSub b aS aE
                    let dxB, dyB = vecSub b bS bE
                    outputs.Add(crossZ b dxA dyA dxB dyB)
                | _ -> skipped <- skipped + 1

            | Perpendicular(asId, aeId, bsId, beId, _, _) ->
                match tryPoint asId, tryPoint aeId, tryPoint bsId, tryPoint beId with
                | Some aS, Some aE, Some bS, Some bE ->
                    let dxA, dyA = vecSub b aS aE
                    let dxB, dyB = vecSub b bS bE
                    outputs.Add(dot b dxA dyA dxB dyB)
                | _ -> skipped <- skipped + 1

            // ── Tangent (line to circle): perpDist(center, line) = r ─
            | Tangent(asId, aeId, centerId, _, _, radius) ->
                match tryPoint asId, tryPoint aeId, tryPoint centerId with
                | Some aS, Some aE, Some c ->
                    let dxL, dyL = vecSub b aS aE
                    let dxC = b.Sub(c.XNode, aS.XNode)
                    let dyC = b.Sub(c.YNode, aS.YNode)
                    let cross = crossZ b dxL dyL dxC dyC
                    let lineLen = length b dxL dyL
                    outputs.Add(b.Sub(cross, b.Mul(constant b radius, lineLen)))
                | _ -> skipped <- skipped + 1

            | CircleDiameter(circleId, _, diameter, _) ->
                match tryCircle circleId with
                | Some cir ->
                    let twoR = b.Mul(cir.RadiusNode, constant b 2.0)
                    emitDiffConst twoR diameter
                | _ -> skipped <- skipped + 1

            // ── LineDistance (two parallel lines; emit separation only) ─
            | LineDistance(asId, aeId, bsId, _, _, _, distance, _) ->
                match tryPoint asId, tryPoint aeId, tryPoint bsId with
                | Some aS, Some aE, Some bS ->
                    let dxL, dyL = vecSub b aS aE
                    let dxR = b.Sub(bS.XNode, aS.XNode)
                    let dyR = b.Sub(bS.YNode, aS.YNode)
                    let cross = crossZ b dxL dyL dxR dyR
                    let lineLen = length b dxL dyL
                    outputs.Add(b.Sub(cross, b.Mul(constant b distance, lineLen)))
                | _ -> skipped <- skipped + 1

            | PointLineDistance(pid, _, asId, aeId, distance, _) ->
                match tryPoint pid, tryPoint asId, tryPoint aeId with
                | Some p, Some aS, Some aE ->
                    let dxL, dyL = vecSub b aS aE
                    let dxR = b.Sub(p.XNode, aS.XNode)
                    let dyR = b.Sub(p.YNode, aS.YNode)
                    let cross = crossZ b dxL dyL dxR dyR
                    let lineLen = length b dxL dyL
                    outputs.Add(b.Sub(cross, b.Mul(constant b distance, lineLen)))
                | _ -> skipped <- skipped + 1

            | PointCircleDistance(pid, circleId, centerId, distance, _) ->
                match tryPoint pid, tryPoint centerId, tryCircle circleId with
                | Some p, Some c, Some cr ->
                    let dx = b.Sub(p.XNode, c.XNode)
                    let dy = b.Sub(p.YNode, c.YNode)
                    let dist = length b dx dy
                    let target = b.Add(cr.RadiusNode, constant b distance)
                    outputs.Add(b.Sub(dist, target))
                | _ -> skipped <- skipped + 1

            | LineCircleDistance(_, asId, aeId, circleId, centerId, distance, _) ->
                match tryPoint asId, tryPoint aeId, tryPoint centerId, tryCircle circleId with
                | Some aS, Some aE, Some c, Some cr ->
                    let dxL, dyL = vecSub b aS aE
                    let dxR = b.Sub(c.XNode, aS.XNode)
                    let dyR = b.Sub(c.YNode, aS.YNode)
                    let cross = crossZ b dxL dyL dxR dyR
                    let lineLen = length b dxL dyL
                    let target = b.Add(cr.RadiusNode, constant b distance)
                    outputs.Add(b.Sub(cross, b.Mul(target, lineLen)))
                | _ -> skipped <- skipped + 1

            | CircleCircleDistance(circleAId, caId, circleBId, cbId, distance, internalFlag, _) ->
                match tryPoint caId, tryPoint cbId, tryCircle circleAId, tryCircle circleBId with
                | Some pa, Some pb, Some crA, Some crB ->
                    let dx, dy = vecSub b pa pb
                    let centerDist = length b dx dy
                    let target =
                        if internalFlag then
                            // Internal: |cA - cB| + d = |rA - rB|. We treat
                            // the signed form: assume rA ≥ rB, residual is
                            // centerDist - (rA - rB) + d.
                            let rDiff = b.Sub(crA.RadiusNode, crB.RadiusNode)
                            b.Sub(rDiff, constant b distance)
                        else
                            b.Add(b.Add(crA.RadiusNode, crB.RadiusNode), constant b distance)
                    outputs.Add(b.Sub(centerDist, target))
                | _ -> skipped <- skipped + 1

            // ── Angle between two lines ──────────────────────────────
            | Angle(asId, aeId, bsId, beId, _, _, angleDeg, aReverse, bReverse, ccwFromAToB, _) ->
                match tryPoint asId, tryPoint aeId, tryPoint bsId, tryPoint beId with
                | Some aS, Some aE, Some bS, Some bE ->
                    let sign r = if r then -1.0 else 1.0
                    let sA = constant b (sign aReverse)
                    let sB = constant b (sign bReverse)
                    let dxA0, dyA0 = vecSub b aS aE
                    let dxB0, dyB0 = vecSub b bS bE
                    let dxA = b.Mul(sA, dxA0)
                    let dyA = b.Mul(sA, dyA0)
                    let dxB = b.Mul(sB, dxB0)
                    let dyB = b.Mul(sB, dyB0)
                    let crossAB = crossZ b dxA dyA dxB dyB
                    let dotAB = dot b dxA dyA dxB dyB
                    let angle = b.Atan2(crossAB, dotAB)
                    // Flip sign if ccwFromAToB is false.
                    let angleSigned =
                        if ccwFromAToB then angle else b.Neg angle
                    let target = constant b (angleDeg * Math.PI / 180.0)
                    emitDiff angleSigned target
                | _ -> skipped <- skipped + 1

            // ── Frame-based constraints ──────────────────────────────
            | FrameCoincident(pid, frameId, part) ->
                match tryPoint pid, parseFramePart part, Map.tryFind frameId ctx.Frames with
                | Some p, Some fp, Some ft ->
                    match frameGeometry ctx.SketchOrigin ft fp with
                    | Some (FGPoint(fx, fy)) ->
                        emitDiffConst p.XNode fx
                        emitDiffConst p.YNode fy
                    | Some (FGLine(ox, oy, dx, dy)) ->
                        // Point on line: cross((p - o), dir) = 0
                        let dpx = b.Sub(p.XNode, constant b ox)
                        let dpy = b.Sub(p.YNode, constant b oy)
                        outputs.Add(crossZ b (constant b dx) (constant b dy) dpx dpy)
                    | None -> skipped <- skipped + 1
                | _ -> skipped <- skipped + 1

            | FrameDistance(pid, frameId, part, distance, _) ->
                match tryPoint pid, parseFramePart part, Map.tryFind frameId ctx.Frames with
                | Some p, Some fp, Some ft ->
                    match frameGeometry ctx.SketchOrigin ft fp with
                    | Some (FGPoint(fx, fy)) ->
                        let dx = b.Sub(p.XNode, constant b fx)
                        let dy = b.Sub(p.YNode, constant b fy)
                        emitDiffConst (length b dx dy) distance
                    | Some (FGLine(ox, oy, dx, dy)) ->
                        // Perpendicular distance = cross((p - o), (dx,dy)) / |dir|.
                        // dir is already unit from frameGeometry.
                        let dpx = b.Sub(p.XNode, constant b ox)
                        let dpy = b.Sub(p.YNode, constant b oy)
                        let cross = crossZ b (constant b dx) (constant b dy) dpx dpy
                        emitDiffConst cross distance
                    | None -> skipped <- skipped + 1
                | _ -> skipped <- skipped + 1

            | FrameParallel(asId, aeId, _, frameId, part) ->
                match tryPoint asId, tryPoint aeId, parseFramePart part, Map.tryFind frameId ctx.Frames with
                | Some aS, Some aE, Some fp, Some ft when fp <> FPOrigin ->
                    match frameGeometry ctx.SketchOrigin ft fp with
                    | Some (FGLine(_, _, dx, dy)) ->
                        let dxL, dyL = vecSub b aS aE
                        outputs.Add(crossZ b dxL dyL (constant b dx) (constant b dy))
                    | _ -> skipped <- skipped + 1
                | _ -> skipped <- skipped + 1

            | FramePerpendicular(asId, aeId, _, frameId, part) ->
                match tryPoint asId, tryPoint aeId, parseFramePart part, Map.tryFind frameId ctx.Frames with
                | Some aS, Some aE, Some fp, Some ft when fp <> FPOrigin ->
                    match frameGeometry ctx.SketchOrigin ft fp with
                    | Some (FGLine(_, _, dx, dy)) ->
                        let dxL, dyL = vecSub b aS aE
                        outputs.Add(dot b dxL dyL (constant b dx) (constant b dy))
                    | _ -> skipped <- skipped + 1
                | _ -> skipped <- skipped + 1

            | FrameLineDistance(_, asId, aeId, frameId, part, distance, _) ->
                // Sketch line assumed parallel to the frame axis (enforced
                // separately by FrameParallel if the user wants it). We emit
                // only the perpendicular separation: distance from aStart to
                // the axis line equals `distance`.
                match tryPoint asId, tryPoint aeId, parseFramePart part, Map.tryFind frameId ctx.Frames with
                | Some aS, _aE, Some fp, Some ft when fp <> FPOrigin ->
                    match frameGeometry ctx.SketchOrigin ft fp with
                    | Some (FGLine(ox, oy, dx, dy)) ->
                        let dpx = b.Sub(aS.XNode, constant b ox)
                        let dpy = b.Sub(aS.YNode, constant b oy)
                        let cross = crossZ b (constant b dx) (constant b dy) dpx dpy
                        emitDiffConst cross distance
                    | _ -> skipped <- skipped + 1
                | _ -> skipped <- skipped + 1

            | FramePointLineDistance(pid, frameId, part, distance, _) ->
                match tryPoint pid, parseFramePart part, Map.tryFind frameId ctx.Frames with
                | Some p, Some fp, Some ft when fp <> FPOrigin ->
                    match frameGeometry ctx.SketchOrigin ft fp with
                    | Some (FGLine(ox, oy, dx, dy)) ->
                        let dpx = b.Sub(p.XNode, constant b ox)
                        let dpy = b.Sub(p.YNode, constant b oy)
                        let cross = crossZ b (constant b dx) (constant b dy) dpx dpy
                        emitDiffConst cross distance
                    | _ -> skipped <- skipped + 1
                | _ -> skipped <- skipped + 1

            | CurveTangent(entityAId, caId, entityBId, cbId, internalFlag) ->
                match tryPoint caId, tryPoint cbId, tryCircle entityAId, tryCircle entityBId with
                | Some pa, Some pb, Some crA, Some crB ->
                    let dx, dy = vecSub b pa pb
                    let centerDist = length b dx dy
                    let radiusCombo =
                        if internalFlag then b.Sub(crA.RadiusNode, crB.RadiusNode)
                        else b.Add(crA.RadiusNode, crB.RadiusNode)
                    outputs.Add(b.Sub(centerDist, radiusCombo))
                | _ -> skipped <- skipped + 1

        if skipped > 0 then
            eprintfn "[SketchCompile] skipped %d constraints (unsupported variant, unresolved ref, or degenerate frame axis)" skipped

        let paramCount = b.ParamCount
        let varSlots =
            [| 0 .. paramCount - 1 |]
            |> Array.filter (fun s -> not (fixedSlots.Contains s))

        b.Build(outputs.ToArray(), varSlots)
