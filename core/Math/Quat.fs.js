import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, float64_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { Vec3 } from "./Vec.fs.js";

export class Quat extends Record {
    constructor(W, X, Y, Z) {
        super();
        this.W = W;
        this.X = X;
        this.Y = Y;
        this.Z = Z;
    }
}

export function Quat_$reflection() {
    return record_type("Server.Quat", [], Quat, () => [["W", float64_type], ["X", float64_type], ["Y", float64_type], ["Z", float64_type]]);
}

export function Quat_get_Identity() {
    return new Quat(1, 0, 0, 0);
}

/**
 * Hamilton product (rotation composition).
 */
export function Quat_op_Multiply_Z3F536FE0(a, b) {
    return new Quat((((a.W * b.W) - (a.X * b.X)) - (a.Y * b.Y)) - (a.Z * b.Z), (((a.W * b.X) + (a.X * b.W)) + (a.Y * b.Z)) - (a.Z * b.Y), (((a.W * b.Y) - (a.X * b.Z)) + (a.Y * b.W)) + (a.Z * b.X), (((a.W * b.Z) + (a.X * b.Y)) - (a.Y * b.X)) + (a.Z * b.W));
}

/**
 * Conjugate = inverse for unit quaternions.
 */
export function Quat__get_Inverse(q) {
    return new Quat(q.W, -q.X, -q.Y, -q.Z);
}

/**
 * Rotate a vector: q * v * q⁻¹
 */
export function Quat__Rotate_Z2E054BF3(q, v) {
    const tx = 2 * ((q.Y * v.Z) - (q.Z * v.Y));
    const ty = 2 * ((q.Z * v.X) - (q.X * v.Z));
    const tz = 2 * ((q.X * v.Y) - (q.Y * v.X));
    return new Vec3((v.X + (q.W * tx)) + ((q.Y * tz) - (q.Z * ty)), (v.Y + (q.W * ty)) + ((q.Z * tx) - (q.X * tz)), (v.Z + (q.W * tz)) + ((q.X * ty) - (q.Y * tx)));
}

export function QuatModule_fromBasis(xAxis, yAxis, zAxis) {
    const m02 = zAxis.X;
    const m01 = yAxis.X;
    const m00 = xAxis.X;
    const m12 = zAxis.Y;
    const m11 = yAxis.Y;
    const m10 = xAxis.Y;
    const m22 = zAxis.Z;
    const m21 = yAxis.Z;
    const m20 = xAxis.Z;
    const trace = (m00 + m11) + m22;
    if (trace > 0) {
        const s = Math.sqrt(trace + 1) * 2;
        return new Quat(0.25 * s, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s);
    }
    else if ((m00 > m11) && (m00 > m22)) {
        const s_1 = Math.sqrt(((1 + m00) - m11) - m22) * 2;
        return new Quat((m21 - m12) / s_1, 0.25 * s_1, (m01 + m10) / s_1, (m02 + m20) / s_1);
    }
    else if (m11 > m22) {
        const s_2 = Math.sqrt(((1 + m11) - m00) - m22) * 2;
        return new Quat((m02 - m20) / s_2, (m01 + m10) / s_2, 0.25 * s_2, (m12 + m21) / s_2);
    }
    else {
        const s_3 = Math.sqrt(((1 + m22) - m00) - m11) * 2;
        return new Quat((m10 - m01) / s_3, (m02 + m20) / s_3, (m12 + m21) / s_3, 0.25 * s_3);
    }
}

