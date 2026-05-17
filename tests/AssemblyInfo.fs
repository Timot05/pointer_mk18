module AssemblyInfo

// ---------------------------------------------------------------------------
// AssemblyInfo.fs — test-assembly-level attributes.
//
// xUnit's default is to run test *classes* (= F# modules with [<Fact>]
// members) in parallel. Several parts of the codebase keep
// single-threaded mutable state at module scope — most notably
// `Typecheck.cellStack` for refinement-cell shadowing during lambda
// elaboration. Production (Fable in the browser) is single-threaded by
// construction; the .NET test runner is not, hence this opt-out.
//
// `DisableTestParallelization = true` serializes everything in the
// assembly — slightly slower but eliminates whole classes of
// shared-state flakes. Easy to revisit if we ever isolate the
// module-level state (e.g. by carrying it as a checker context).
// ---------------------------------------------------------------------------

open Xunit

[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()
