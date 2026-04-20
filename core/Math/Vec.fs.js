import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, float64_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";

export class Vec3 extends Record {
    constructor(X, Y, Z) {
        super();
        this.X = X;
        this.Y = Y;
        this.Z = Z;
    }
}

export function Vec3_$reflection() {
    return record_type("Server.Vec3", [], Vec3, () => [["X", float64_type], ["Y", float64_type], ["Z", float64_type]]);
}

export function Vec3_get_Zero() {
    return new Vec3(0, 0, 0);
}

export function Vec3_op_Addition_Z3F547E60(a, b) {
    return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

export function Vec3_op_Subtraction_Z3F547E60(a, b) {
    return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}

export function Vec3_op_UnaryNegation_Z2E054BF3(a) {
    return new Vec3(-a.X, -a.Y, -a.Z);
}

export function Vec3_op_Multiply_ZB3DA56A(s, v) {
    return new Vec3(s * v.X, s * v.Y, s * v.Z);
}

export function Vec3__get_LengthSq(v) {
    return ((v.X * v.X) + (v.Y * v.Y)) + (v.Z * v.Z);
}

export function Vec3__get_Length(v) {
    return Math.sqrt(Vec3__get_LengthSq(v));
}

export function Vec3__get_Normalized(v) {
    const len = Vec3__get_Length(v);
    if (len < 1E-12) {
        return Vec3_get_Zero();
    }
    else {
        const inv = 1 / len;
        return new Vec3(inv * v.X, inv * v.Y, inv * v.Z);
    }
}

export function Vec3_Dot_Z3F547E60(a, b) {
    return ((a.X * b.X) + (a.Y * b.Y)) + (a.Z * b.Z);
}

export function Vec3_Cross_Z3F547E60(a, b) {
    return new Vec3((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
}

/**
 * Euclidean distance between two 2D points represented as (x, y) tuples.
 */
export function Vec2_distance(_arg2_, _arg2__1, _arg1_, _arg1__1) {
    const _arg = [_arg2_, _arg2__1];
    const _arg_1 = [_arg1_, _arg1__1];
    const dx = _arg_1[0] - _arg[0];
    const dy = _arg_1[1] - _arg[1];
    return Math.sqrt((dx * dx) + (dy * dy));
}

/**
 * Perpendicular distance from point p to the line through a–b.
 * Returns 0 if a and b coincide.
 */
export function Vec2_pointLineDistance(_arg3_, _arg3__1, _arg2_, _arg2__1, _arg1_, _arg1__1) {
    const _arg = [_arg3_, _arg3__1];
    const _arg_1 = [_arg2_, _arg2__1];
    const _arg_2 = [_arg1_, _arg1__1];
    const ay = _arg_1[1];
    const ax = _arg_1[0];
    const dx = _arg_2[0] - ax;
    const dy = _arg_2[1] - ay;
    const len = Math.sqrt((dx * dx) + (dy * dy));
    if (len < 1E-09) {
        return 0;
    }
    else {
        return Math.abs(((dx * (_arg[1] - ay)) - (dy * (_arg[0] - ax))) / len);
    }
}

