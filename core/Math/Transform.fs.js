import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { Quat, Quat__get_Inverse, Quat__Rotate_Z2E054BF3, Quat_op_Multiply_Z3F536FE0, Quat_get_Identity, Quat_$reflection } from "./Quat.fs.js";
import { Vec3__get_Normalized, Vec3_op_UnaryNegation_Z2E054BF3, Vec3_op_Addition_Z3F547E60, Vec3_get_Zero, Vec3_$reflection } from "./Vec.fs.js";
import { record_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";

export class RigidTransform extends Record {
    constructor(Rot, Trans) {
        super();
        this.Rot = Rot;
        this.Trans = Trans;
    }
}

export function RigidTransform_$reflection() {
    return record_type("Server.RigidTransform", [], RigidTransform, () => [["Rot", Quat_$reflection()], ["Trans", Vec3_$reflection()]]);
}

export function RigidTransform_get_Identity() {
    return new RigidTransform(Quat_get_Identity(), Vec3_get_Zero());
}

/**
 * Compose: T1 * T2 means apply T2 first, then T1.
 * (R1*R2, R1*t2 + t1)
 */
export function RigidTransform_op_Multiply_ZFA4D60(a, b) {
    return new RigidTransform(Quat_op_Multiply_Z3F536FE0(a.Rot, b.Rot), Vec3_op_Addition_Z3F547E60(Quat__Rotate_Z2E054BF3(a.Rot, b.Trans), a.Trans));
}

/**
 * Inverse: T⁻¹ = (R⁻¹, -R⁻¹ * t)
 */
export function RigidTransform__get_Inverse(t) {
    const ri = Quat__get_Inverse(t.Rot);
    return new RigidTransform(ri, Vec3_op_UnaryNegation_Z2E054BF3(Quat__Rotate_Z2E054BF3(ri, t.Trans)));
}

/**
 * Apply transform to a point.
 */
export function RigidTransform__Apply_Z2E054BF3(t, p) {
    return Vec3_op_Addition_Z3F547E60(Quat__Rotate_Z2E054BF3(t.Rot, p), t.Trans);
}

/**
 * Pure translation (no rotation).
 */
export function RigidTransformModule_translate(v) {
    return new RigidTransform(Quat_get_Identity(), v);
}

/**
 * Rotation from axis-angle in radians. Axis need not be unit length;
 * its length is ignored and angle is separate.
 */
export function RigidTransformModule_fromAxisAngle(axis, angleRad) {
    const half = angleRad * 0.5;
    const a = Vec3__get_Normalized(axis);
    const s = Math.sin(half);
    return new RigidTransform(new Quat(Math.cos(half), a.X * s, a.Y * s, a.Z * s), Vec3_get_Zero());
}

