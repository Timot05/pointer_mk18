const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const math_domain = b.createModule(.{
        .root_source_file = b.path("src/math_domain.zig"),
        .target = target,
        .optimize = optimize,
    });

    // ── Tests (native target) ────────────────────────────────────────────

    const tests = b.addTest(.{
        .root_module = b.createModule(.{
            .root_source_file = b.path("tests/math_domain_tests.zig"),
            .target = target,
            .optimize = optimize,
        }),
    });
    tests.root_module.addImport("math_domain", math_domain);

    const run_tests = b.addRunArtifact(tests);
    const test_step = b.step("test", "Run Zig tests");
    test_step.dependOn(&run_tests.step);

    // ── Diagnostic CLIs (native target) ──────────────────────────────────

    const render_demo = b.addExecutable(.{
        .name = "render_demo",
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/render_demo.zig"),
            .target = target,
            .optimize = .ReleaseFast,
        }),
    });
    const run_render_demo = b.addRunArtifact(render_demo);
    if (b.args) |args| run_render_demo.addArgs(args);
    const render_demo_step = b.step("render-demo", "Render one frame of a scene to render_demo.ppm");
    render_demo_step.dependOn(&run_render_demo.step);

    const ir_dump = b.addExecutable(.{
        .name = "ir_dump",
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/ir_dump.zig"),
            .target = target,
            .optimize = optimize,
        }),
    });
    const run_ir_dump = b.addRunArtifact(ir_dump);
    if (b.args) |args| run_ir_dump.addArgs(args);
    const ir_dump_step = b.step("ir-dump", "Dump IR DAG (Graphviz) and tape listing for a named scene");
    ir_dump_step.dependOn(&run_ir_dump.step);

    // ── Browser kernel WASM (the default build product) ──────────────────
    //
    // Invoked with `zig build -p ..` from `kernel/`, this writes
    // `viewer.wasm` to `<repo>/ui/public/kernel/` — where Vite serves it
    // as `/kernel/viewer.wasm` for the F# host. simd128 enables v128
    // codegen for `@Vector(4, f32)` hot loops in the renderer.

    const wasm_target = b.resolveTargetQuery(.{
        .cpu_arch = .wasm32,
        .os_tag = .freestanding,
        .cpu_features_add = std.Target.wasm.featureSet(&.{
            .simd128,
            .bulk_memory,
            .sign_ext,
        }),
    });
    const viewer = b.addExecutable(.{
        .name = "viewer",
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/web_main.zig"),
            .target = wasm_target,
            .optimize = .ReleaseFast,
        }),
    });
    viewer.entry = .disabled;
    viewer.rdynamic = true;

    const install_viewer = b.addInstallArtifact(viewer, .{
        .dest_dir = .{ .override = .{ .custom = "ui/public/kernel" } },
    });
    b.getInstallStep().dependOn(&install_viewer.step);
}
