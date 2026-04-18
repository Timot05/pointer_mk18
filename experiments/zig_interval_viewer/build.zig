const std = @import("std");

pub fn build(b: *std.Build) void {
    const features = std.Target.wasm.featureSet(&.{
        .simd128,
        .bulk_memory,
        .sign_ext,
    });
    const target = b.resolveTargetQuery(.{
        .cpu_arch = .wasm32,
        .os_tag = .freestanding,
        .cpu_features_add = features,
    });
    const optimize = b.standardOptimizeOption(.{});

    const root_mod = b.createModule(.{
        .root_source_file = b.path("src/main.zig"),
        .target = target,
        .optimize = optimize,
    });

    const exe = b.addExecutable(.{
        .name = "viewer",
        .root_module = root_mod,
    });
    exe.entry = .disabled;
    exe.rdynamic = true;

    const install = b.addInstallArtifact(exe, .{
        .dest_dir = .{ .override = .{ .custom = "../web" } },
    });
    b.getInstallStep().dependOn(&install.step);
}
