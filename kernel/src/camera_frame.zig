// Camera frame composed at the root of the tape. Mirrors mk18's pattern
// (kernel/src/lower.zig). The kernel renders in CAMERA-LOCAL coordinates;
// the tape transforms them to world coordinates so the user's authored
// SDF (in world space) sees the right point:
//
//     world = eye + basis_x * wcx + basis_y * wcy + basis_z * wcz
//
// Wrapping is just IR-level composition: 12 `const_` nodes + 9 muls + 9
// adds + a `remap_axes` around the user's root. The 12 constants are
// **mutable** — `MutableCamera.setFrame` writes directly into the
// compiled tape's immediates without re-lowering, so orbit costs 12 f32
// writes per frame.

const std = @import("std");
const math_ir = @import("math_ir.zig");
const reg = @import("math_reg_tape.zig");

const MathIR = math_ir.MathIR;
const Expr = math_ir.Expr;
const RegTape = reg.RegTape;
const Op = reg.Op;

/// Camera basis. `basis_z` is the renderer's "+forward" direction —
/// rays march from the eye along `+wcz` (camera-local) toward the scene.
pub const CameraFrame = struct {
    eye: [3]f32,
    basis_x: [3]f32,
    basis_y: [3]f32,
    basis_z: [3]f32,

    pub const identity: CameraFrame = .{
        .eye = .{ 0, 0, 0 },
        .basis_x = .{ 1, 0, 0 },
        .basis_y = .{ 0, 1, 0 },
        .basis_z = .{ 0, 0, 1 },
    };

    /// Right-handed lookAt. `basis_z` points from `eye` toward `target` —
    /// the forward direction. Matches the renderer's convention:
    /// smaller t = closer to eye, larger t = deeper into the scene.
    pub fn lookAt(eye: [3]f32, target: [3]f32, up_hint: [3]f32) CameraFrame {
        const bz = nrm(.{ target[0] - eye[0], target[1] - eye[1], target[2] - eye[2] });
        const bx = nrm(cross(up_hint, bz));
        const by = cross(bz, bx);
        return .{ .eye = eye, .basis_x = bx, .basis_y = by, .basis_z = bz };
    }
};

/// IR-node IDs of the 12 mutable camera constants. Captured by
/// `wrapWithCameraFrame` so we can later locate the corresponding
/// immediate-array slots in the compiled tape.
pub const CameraFrameNodes = struct {
    eye: [3]i32,
    basis_x: [3]i32,
    basis_y: [3]i32,
    basis_z: [3]i32,
};

pub const WrappedCamera = struct {
    /// Pass this to `compileToRegTape` instead of the original root.
    wrapped_root: Expr,
    /// Hand to `MutableCamera.bind(...)` after compiling.
    nodes: CameraFrameNodes,
};

/// Wraps `root` with a camera-frame `remap_axes`. The wrapped tree's input
/// space is camera-local (wcx, wcy, wcz); the wrapper computes
/// `world = eye + bx*wcx + by*wcy + bz*wcz` and remaps so the user's
/// `var_x`/`var_y`/`var_z` references resolve to the world coordinates.
pub fn wrapWithCameraFrame(ir: *MathIR, root: Expr, frame: CameraFrame) !WrappedCamera {
    const e0 = try ir.constant(@floatCast(frame.eye[0]));
    const e1 = try ir.constant(@floatCast(frame.eye[1]));
    const e2 = try ir.constant(@floatCast(frame.eye[2]));
    const bx0 = try ir.constant(@floatCast(frame.basis_x[0]));
    const bx1 = try ir.constant(@floatCast(frame.basis_x[1]));
    const bx2 = try ir.constant(@floatCast(frame.basis_x[2]));
    const by0 = try ir.constant(@floatCast(frame.basis_y[0]));
    const by1 = try ir.constant(@floatCast(frame.basis_y[1]));
    const by2 = try ir.constant(@floatCast(frame.basis_y[2]));
    const bz0 = try ir.constant(@floatCast(frame.basis_z[0]));
    const bz1 = try ir.constant(@floatCast(frame.basis_z[1]));
    const bz2 = try ir.constant(@floatCast(frame.basis_z[2]));

    // Kernel-input axes (camera-local: wcx, wcy, wcz). Fresh var_ nodes
    // — these are distinct from any var_x/y/z the user's tree may have.
    const wcx = try ir.x();
    const wcy = try ir.y();
    const wcz = try ir.z();

    const world_x = try axisFold(ir, e0, bx0, by0, bz0, wcx, wcy, wcz);
    const world_y = try axisFold(ir, e1, bx1, by1, bz1, wcx, wcy, wcz);
    const world_z = try axisFold(ir, e2, bx2, by2, bz2, wcx, wcy, wcz);

    const wrapped = try ir.remapAxes(root, world_x, world_y, world_z);

    return .{
        .wrapped_root = wrapped,
        .nodes = .{
            .eye = .{ e0.id, e1.id, e2.id },
            .basis_x = .{ bx0.id, bx1.id, bx2.id },
            .basis_y = .{ by0.id, by1.id, by2.id },
            .basis_z = .{ bz0.id, bz1.id, bz2.id },
        },
    };
}

fn axisFold(ir: *MathIR, eye: Expr, bx: Expr, by: Expr, bz: Expr, x: Expr, y: Expr, z: Expr) !Expr {
    const tx = try ir.binary(.mul, bx, x);
    const ty = try ir.binary(.mul, by, y);
    const tz = try ir.binary(.mul, bz, z);
    const xy = try ir.binary(.add, eye, tx);
    const yz = try ir.binary(.add, ty, tz);
    return ir.binary(.add, xy, yz);
}

/// Resolved positions in `tape.immediates` for the 12 camera-frame slots.
/// Construct via `bind(nodes, tape)` after compiling.
pub const MutableCamera = struct {
    eye: [3]u16,
    basis_x: [3]u16,
    basis_y: [3]u16,
    basis_z: [3]u16,

    /// Walks the tape's `load_const` ops once and records the
    /// `tape.immediates[]` index for each of our 12 IR const-nodes.
    ///
    /// Note: `tape.immediate()` deduplicates by value during compilation,
    /// so multiple IR const nodes with the same value share one immediate
    /// slot. To make each of our 12 nodes individually mutable, we split
    /// each one off into a fresh slot here — copy the value into a new
    /// `immediates[]` entry, rewrite the load_const's `aux`. Other consts
    /// that previously shared with us keep their original slot.
    pub fn bind(nodes: CameraFrameNodes, tape: *RegTape) error{ImmediateCapacity}!MutableCamera {
        const all_ids = [12]i32{
            nodes.eye[0],     nodes.eye[1],     nodes.eye[2],
            nodes.basis_x[0], nodes.basis_x[1], nodes.basis_x[2],
            nodes.basis_y[0], nodes.basis_y[1], nodes.basis_y[2],
            nodes.basis_z[0], nodes.basis_z[1], nodes.basis_z[2],
        };
        var imms: [12]u16 = undefined;
        var found = [_]bool{false} ** 12;

        var ip: usize = 0;
        while (ip < tape.instruction_count) : (ip += 1) {
            const op: Op = @enumFromInt(tape.opcodes[ip]);
            if (op != .load_const) continue;
            const dst_id: i32 = @intCast(tape.dst[ip]);
            for (all_ids, 0..) |id, i| {
                if (!found[i] and dst_id == id) {
                    const old_imm: usize = @intCast(tape.aux[ip]);
                    if (tape.immediate_count >= math_ir.max_immediates) {
                        return error.ImmediateCapacity;
                    }
                    const new_imm = tape.immediate_count;
                    tape.immediates[new_imm] = tape.immediates[old_imm];
                    tape.immediate_count += 1;
                    tape.aux[ip] = @intCast(new_imm);
                    imms[i] = @intCast(new_imm);
                    found[i] = true;
                }
            }
        }

        for (found) |f| std.debug.assert(f);

        return .{
            .eye = .{ imms[0], imms[1], imms[2] },
            .basis_x = .{ imms[3], imms[4], imms[5] },
            .basis_y = .{ imms[6], imms[7], imms[8] },
            .basis_z = .{ imms[9], imms[10], imms[11] },
        };
    }

    /// Update camera in-place. The tape's other ops are unaffected; the
    /// next `decodeRegEval*` call uses the new values.
    pub fn setFrame(self: MutableCamera, tape: *RegTape, frame: CameraFrame) void {
        inline for (0..3) |i| tape.immediates[self.eye[i]] = @floatCast(frame.eye[i]);
        inline for (0..3) |i| tape.immediates[self.basis_x[i]] = @floatCast(frame.basis_x[i]);
        inline for (0..3) |i| tape.immediates[self.basis_y[i]] = @floatCast(frame.basis_y[i]);
        inline for (0..3) |i| tape.immediates[self.basis_z[i]] = @floatCast(frame.basis_z[i]);
    }
};

// ── Small vec3 helpers (kept private to this file) ─────────────────────

fn cross(a: [3]f32, b: [3]f32) [3]f32 {
    return .{
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    };
}

fn nrm(v: [3]f32) [3]f32 {
    const len = @sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
    if (len < 1e-8) return .{ 0, 0, 1 };
    const inv = 1.0 / len;
    return .{ v[0] * inv, v[1] * inv, v[2] * inv };
}
