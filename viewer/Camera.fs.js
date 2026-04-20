import { Record } from "../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, float64_type } from "../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { Vec3_Dot_Z3F547E60, Vec3_op_Multiply_ZB3DA56A, Vec3_op_Addition_Z3F547E60, Vec3_Cross_Z3F547E60, Vec3__get_Normalized, Vec3_op_Subtraction_Z3F547E60, Vec3, Vec3_get_Zero, Vec3_$reflection } from "../core/Math/Vec.fs.js";
import { max } from "../ui/fable_modules/fable-library-js.4.29.0/Double.js";

export const HALF_FOV = 0.3927;

function clamp(lo, hi, v) {
    if (v < lo) {
        return lo;
    }
    else if (v > hi) {
        return hi;
    }
    else {
        return v;
    }
}

export class CameraState extends Record {
    constructor(Azimuth, Elevation, Distance, Target) {
        super();
        this.Azimuth = Azimuth;
        this.Elevation = Elevation;
        this.Distance = Distance;
        this.Target = Target;
    }
}

export function CameraState_$reflection() {
    return record_type("Camera.CameraState", [], CameraState, () => [["Azimuth", float64_type], ["Elevation", float64_type], ["Distance", float64_type], ["Target", Vec3_$reflection()]]);
}

export function create() {
    return new CameraState(0.6, 0.3, 80, Vec3_get_Zero());
}

export function eye(c) {
    const ce = Math.cos(c.Elevation);
    const sa = Math.sin(c.Azimuth);
    const ca = Math.cos(c.Azimuth);
    const se = Math.sin(c.Elevation);
    return new Vec3(c.Target.X + ((c.Distance * ce) * ca), c.Target.Y + ((c.Distance * ce) * sa), c.Target.Z + (c.Distance * se));
}

export class Basis extends Record {
    constructor(Eye, Forward, Right, Up) {
        super();
        this.Eye = Eye;
        this.Forward = Forward;
        this.Right = Right;
        this.Up = Up;
    }
}

export function Basis_$reflection() {
    return record_type("Camera.Basis", [], Basis, () => [["Eye", Vec3_$reflection()], ["Forward", Vec3_$reflection()], ["Right", Vec3_$reflection()], ["Up", Vec3_$reflection()]]);
}

/**
 * Camera basis: forward points from eye toward target, right is
 * forward × world-up (= Z), up is right × forward. Matches camera.ts.
 */
export function basis(c) {
    let copyOfStruct_2;
    const e = eye(c);
    let forward;
    let copyOfStruct = Vec3_op_Subtraction_Z3F547E60(c.Target, e);
    forward = Vec3__get_Normalized(copyOfStruct);
    let right;
    let copyOfStruct_1 = Vec3_Cross_Z3F547E60(forward, new Vec3(0, 0, 1));
    right = Vec3__get_Normalized(copyOfStruct_1);
    return new Basis(e, forward, right, (copyOfStruct_2 = Vec3_Cross_Z3F547E60(right, forward), Vec3__get_Normalized(copyOfStruct_2)));
}

export function orbit(c, dx, dy) {
    c.Azimuth = (c.Azimuth - (dx * 0.01));
    c.Elevation = clamp(-1.4, 1.4, c.Elevation + (dy * 0.01));
}

export function pan(c, dx, dy, height) {
    const b = basis(c);
    const worldPerPx = ((2 * c.Distance) * Math.tan(HALF_FOV)) / max(height, 1);
    c.Target = Vec3_op_Addition_Z3F547E60(Vec3_op_Addition_Z3F547E60(c.Target, Vec3_op_Multiply_ZB3DA56A(-dx * worldPerPx, b.Right)), Vec3_op_Multiply_ZB3DA56A(dy * worldPerPx, b.Up));
}

export function zoom(c, deltaY) {
    const next = c.Distance * Math.exp(deltaY * 0.0012);
    c.Distance = clamp(6, 800, next);
}

export class Ray extends Record {
    constructor(Origin, Direction) {
        super();
        this.Origin = Origin;
        this.Direction = Direction;
    }
}

export function Ray_$reflection() {
    return record_type("Camera.Ray", [], Ray, () => [["Origin", Vec3_$reflection()], ["Direction", Vec3_$reflection()]]);
}

function rayPlaneHit(ray, planeOrigin, planeNormal) {
    const denom = Vec3_Dot_Z3F547E60(ray.Direction, planeNormal);
    if (Math.abs(denom) < 1E-06) {
        return undefined;
    }
    else {
        const t = Vec3_Dot_Z3F547E60(Vec3_op_Subtraction_Z3F547E60(planeOrigin, ray.Origin), planeNormal) / denom;
        if (t <= 0) {
            return undefined;
        }
        else {
            return Vec3_op_Addition_Z3F547E60(ray.Origin, Vec3_op_Multiply_ZB3DA56A(t, ray.Direction));
        }
    }
}

export function screenToRay(width, height, c, x, y) {
    let copyOfStruct;
    const ndcX = ((x / max(width, 1)) * 2) - 1;
    const ndcY = 1 - ((y / max(height, 1)) * 2);
    const aspect = width / max(height, 1);
    const tanHalf = Math.tan(HALF_FOV);
    const b = basis(c);
    return new Ray(b.Eye, (copyOfStruct = Vec3_op_Addition_Z3F547E60(Vec3_op_Addition_Z3F547E60(b.Forward, Vec3_op_Multiply_ZB3DA56A((ndcX * aspect) * tanHalf, b.Right)), Vec3_op_Multiply_ZB3DA56A(ndcY * tanHalf, b.Up)), Vec3__get_Normalized(copyOfStruct)));
}

/**
 * Zoom while keeping whatever's under (x, y) fixed on screen. Adjusts
 * target as well as distance — matches camera.ts's zoomTowardsPointer.
 */
export function zoomTowardsPointer(c, width, height, x, y, deltaY) {
    const forwardBefore = basis(c).Forward;
    const targetBefore = c.Target;
    const hitBefore = rayPlaneHit(screenToRay(width, height, c, x, y), targetBefore, forwardBefore);
    zoom(c, deltaY);
    if (hitBefore != null) {
        const hb = hitBefore;
        const matchValue = rayPlaneHit(screenToRay(width, height, c, x, y), targetBefore, forwardBefore);
        if (matchValue != null) {
            const ha = matchValue;
            c.Target = Vec3_op_Addition_Z3F547E60(c.Target, Vec3_op_Subtraction_Z3F547E60(hb, ha));
        }
    }
}

/**
 * Project a 3D world position onto 2D screen coords (CSS pixels). Returns
 * None when the point is behind the camera or the viewport is degenerate.
 */
export function worldToScreen(width, height, c, world) {
    const w = max(width, 1);
    const h = max(height, 1);
    const b = basis(c);
    const rel = Vec3_op_Subtraction_Z3F547E60(world, b.Eye);
    const z = Vec3_Dot_Z3F547E60(rel, b.Forward);
    if (z <= 1E-06) {
        return undefined;
    }
    else {
        const aspect = w / h;
        const tanHalf = Math.tan(HALF_FOV);
        return [(((Vec3_Dot_Z3F547E60(rel, b.Right) / ((z * tanHalf) * aspect)) + 1) * 0.5) * w, ((1 - (Vec3_Dot_Z3F547E60(rel, b.Up) / (z * tanHalf))) * 0.5) * h];
    }
}

/**
 * Intersect a ray with a 2D plane described by an origin + two axes. Returns
 * the (u, v) coordinates in the local plane frame, or None if behind or parallel.
 */
export function rayPlaneIntersection(ray, origin, xAxis, yAxis) {
    let normal;
    let copyOfStruct = Vec3_Cross_Z3F547E60(xAxis, yAxis);
    normal = Vec3__get_Normalized(copyOfStruct);
    const denom = Vec3_Dot_Z3F547E60(ray.Direction, normal);
    if (Math.abs(denom) < 1E-06) {
        return undefined;
    }
    else {
        const t = Vec3_Dot_Z3F547E60(Vec3_op_Subtraction_Z3F547E60(origin, ray.Origin), normal) / denom;
        if (t <= 0) {
            return undefined;
        }
        else {
            const localV = Vec3_op_Subtraction_Z3F547E60(Vec3_op_Addition_Z3F547E60(ray.Origin, Vec3_op_Multiply_ZB3DA56A(t, ray.Direction)), origin);
            return [Vec3_Dot_Z3F547E60(localV, xAxis), Vec3_Dot_Z3F547E60(localV, yAxis)];
        }
    }
}

