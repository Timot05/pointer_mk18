const std = @import("std");

// Image synthesis from a voxel G-buffer. Takes raw (depth, normal) arrays
// produced by voxel.render and writes RGBA bytes. Pure pixel work — no
// SDF evaluation, no octree state.

pub const ColorMode = enum { world_pos_lit, normal_rgb };

const bg_r: u8 = 10;
const bg_g: u8 = 10;
const bg_b: u8 = 13;

// Shade a G-buffer into RGBA using the chosen mode.
//
//   depth:  one f32 per pixel, wcz of the closest surface hit, -inf for miss
//   normal: three f32 per pixel (nx, ny, nz)
//
// view_half_{w,h} are only used by world_pos_lit to reconstruct wcx / wcy
// from the pixel indices.
pub fn resolveGbuffer(
    pixels: [*]u8,
    width: u32,
    height: u32,
    depth: []const f32,
    normal: []const f32,
    view_half_w: f32,
    view_half_h: f32,
    mode: ColorMode,
) void {
    const lmag = @sqrt(0.6 * 0.6 + 0.7 * 0.7 + 0.4 * 0.4);
    const ld = [_]f32{ 0.6 / lmag, 0.7 / lmag, 0.4 / lmag };

    const wf: f32 = @floatFromInt(width);
    const hf: f32 = @floatFromInt(height);
    const neg_inf = -std.math.inf(f32);

    var py: u32 = 0;
    while (py < height) : (py += 1) {
        const wcy = (1.0 - ((@as(f32, @floatFromInt(py)) + 0.5) / hf) * 2.0) * view_half_h;
        var px: u32 = 0;
        while (px < width) : (px += 1) {
            const idx: usize = @as(usize, py) * width + @as(usize, px);
            const po = idx * 4;
            const d = depth[idx];
            pixels[po + 3] = 255;
            if (d == neg_inf) {
                pixels[po + 0] = bg_r;
                pixels[po + 1] = bg_g;
                pixels[po + 2] = bg_b;
                continue;
            }
            const nx = normal[idx * 3 + 0];
            const ny = normal[idx * 3 + 1];
            const nz = normal[idx * 3 + 2];
            switch (mode) {
                .world_pos_lit => {
                    const wcx = (((@as(f32, @floatFromInt(px)) + 0.5) / wf) * 2.0 - 1.0) * view_half_w;
                    const diffuse = @max(0.0, nx * ld[0] + ny * ld[1] + nz * ld[2]);
                    const shade = 0.25 + diffuse * 0.75;
                    pixels[po + 0] = toByte((wcx * 0.35 + 0.5) * shade * 255.0);
                    pixels[po + 1] = toByte((wcy * 0.35 + 0.5) * shade * 255.0);
                    pixels[po + 2] = toByte((d * 0.35 + 0.5) * shade * 255.0);
                },
                .normal_rgb => {
                    pixels[po + 0] = toByte(@abs(nx) * 255.0);
                    pixels[po + 1] = toByte(@abs(ny) * 255.0);
                    pixels[po + 2] = toByte(@abs(nz) * 255.0);
                },
            }
        }
    }
}

// Grayscale depth image. `near` maps to white, `far` maps to black; camera
// convention is "larger wcz = closer" (matches the voxel walker). Miss pixels
// (-inf) render as the background color so they stay visually distinct from
// a genuinely-far hit.
pub fn resolveDepthGray(
    pixels: [*]u8,
    width: u32,
    height: u32,
    depth: []const f32,
    near: f32,
    far: f32,
) void {
    const neg_inf = -std.math.inf(f32);
    const range = near - far;
    const total_px: usize = @as(usize, width) * @as(usize, height);
    var i: usize = 0;
    while (i < total_px) : (i += 1) {
        const po = i * 4;
        pixels[po + 3] = 255;
        const d = depth[i];
        if (d == neg_inf) {
            pixels[po + 0] = bg_r;
            pixels[po + 1] = bg_g;
            pixels[po + 2] = bg_b;
            continue;
        }
        const t = @max(0.0, @min(1.0, (d - far) / range));
        const gray = toByte(t * 255.0);
        pixels[po + 0] = gray;
        pixels[po + 1] = gray;
        pixels[po + 2] = gray;
    }
}

fn toByte(v: f32) u8 {
    return @intFromFloat(@max(0.0, @min(255.0, v)));
}
