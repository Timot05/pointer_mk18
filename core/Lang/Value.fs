namespace Server.Lang

// ---------------------------------------------------------------------------
// Value.fs — port of pointer_mk19/lib/value.ml.
//
// Runtime values + environment + the Specials record (hooks the notebook
// driver injects for @input / @output / @view / @print / @debug). Closures
// capture their defining env by reference.
// ---------------------------------------------------------------------------

module Value =

    open Token
    open Ast

    type EvalError = { Message: string; Span: Span }

    let inline (>>=) (r: Result<'a, EvalError>) (f: 'a -> Result<'b, EvalError>) =
        match r with
        | Ok v -> f v
        | Error e -> Error e

    let evalError (span: Span) (msg: string) : Result<'a, EvalError> =
        Error { Message = msg; Span = span }

    /// Rigid 3D pose: position + unit quaternion (x, y, z, w).
    type Frame = {
        X: float; Y: float; Z: float
        Qx: float; Qy: float; Qz: float; Qw: float
    }

    // `SketchValue`, `LoopValue`, `CurveValue`, `Value`, `Closure`,
    // `BuiltinValue`, and `Env` form a single mutually-recursive group.

    /// Authoring-time sketch: the same `ActionSketch` shape mk18's UI
    /// produces (entities + constraints, raw float coords, string ids),
    /// plus the plane the sketch lives in. Lowering to MathIR primitives
    /// + slot allocation happens at the consumer (a future
    /// `@sketch_distance` / `@sketch_path` builtin), not here.
    ///
    /// `Fields` carries per-loop `VLoop` runtime values keyed by
    /// `LoopRecord.Id`. Populated by the compose bridge from the
    /// persisted Loops registry; consulted by `Eval.evalExpr` when an
    /// `EPath` walks into a VSketch. Generic sketches built by ad-hoc
    /// paths leave it as `Map.empty`.
    type SketchValue = {
        Sketch: Server.ActionSketch
        Plane:  Server.SketchPlane
        Fields: Map<string, Value>
    }

    /// A closed sketch loop. Its `Fields` map exposes the loop's derived
    /// values — typically `signed_distance: VField`, plus per-primitive
    /// `VPrimitive` entries. The runtime auto-projects a VLoop to its
    /// `signed_distance` field at every VField-consuming site (binary
    /// ops, fold, etc.), matching the `Loop { signed_distance: Field }
    /// <: Field` subtyping rule in `Type.isSubtypeOf`.
    and LoopValue = {
        Fields: Map<string, Value>
    }

    /// A single sketch primitive (line/arc/circle). Same shape as
    /// LoopValue — its `Fields` map names whatever the bridge populated,
    /// including `signed_distance: VField` for auto-projection.
    and PrimitiveValue = {
        Fields: Map<string, Value>
    }

    and CurveValue = { PrimitiveId: int; Plane: MathIr.Plane }

    and Value =
        | VNumber of float
        | VBool of bool
        | VString of string
        | VField of MathIr.Expr
        | VFrame of Frame
        | VSketch of SketchValue
        | VLoop of LoopValue
        | VPrimitive of PrimitiveValue
        | VCurve of CurveValue
        | VRecord of (string * Value) list
        | VClosure of Closure
        | VBuiltin of BuiltinValue
        | VUnit

    and Closure = {
        Param: string
        Body: Expr
        Captured: Env
    }

    // Curried builtin handle. Bound under the bare name (e.g. `sphere`) in
    // every fresh env. Each application via `EApply` accumulates one positional
    // arg; once `AccArgs.Length = Arity` the dispatch path fires. Specials
    // (view/output/input/print/debug) route through `ctx.Specials`; the rest
    // route through `Builtins.dispatchPositional`.
    and BuiltinValue = {
        Name: string
        Arity: int
        AccArgs: Value list
    }

    /// Parented environment. Lookups walk the parent chain; bindings only
    /// touch the innermost frame. Closures capture the env reference at
    /// lambda time, so any later mutations confined to child frames remain
    /// invisible to them.
    and Env = {
        Bindings: System.Collections.Generic.Dictionary<string, Value>
        Parent: Env option
    }

    let newEnv (parent: Env option) : Env =
        { Bindings = System.Collections.Generic.Dictionary<string, Value>(); Parent = parent }

    let rec envLookup (env: Env) (name: string) : Value option =
        let mutable v = Unchecked.defaultof<Value>
        if env.Bindings.TryGetValue(name, &v) then Some v
        else
            match env.Parent with
            | Some p -> envLookup p name
            | None -> None

    let envBind (env: Env) (name: string) (value: Value) =
        env.Bindings.[name] <- value

    /// Hooks for the imperative specials (`@print`, `@debug`). Notebook-
    /// driver-supplied; the standalone test harness ships a stub that
    /// records calls or returns errors. Block-level I/O (`let import` /
    /// `let pub`) is no longer routed through this record.
    type Specials = {
        Print:  Span -> string -> Value -> Result<Value, EvalError>
        Debug:  Span -> Value -> Result<Value, EvalError>
    }

    /// Specials that error on every call — used when no notebook context
    /// is available.
    let unboundSpecials : Specials = {
        Print  = fun sp _ _    -> evalError sp "@print not bound (no notebook context)"
        Debug  = fun sp _      -> evalError sp "@debug not bound (no notebook context)"
    }

    type EvalContext = {
        Ir: MathIr.MathIR
        mutable Env: Env
        mutable Specials: Specials
        mutable NextSlot: int
        mutable NextOwner: int
    }

    let createContext () : EvalContext = {
        Ir = MathIr.MathIR()
        Env = newEnv None
        Specials = unboundSpecials
        NextSlot = 0
        NextOwner = 0
    }

    /// Seat an evaluator on an existing IR + env. Used by the notebook driver
    /// so multiple blocks share one MathIR (cross-block expressions reference
    /// the same node array) and chain into a previously-built scope.
    let createContextWith (ir: MathIr.MathIR) (env: Env) : EvalContext = {
        Ir = ir
        Env = env
        Specials = unboundSpecials
        NextSlot = 0
        NextOwner = 0
    }

    // -- Coercions ---------------------------------------------------------------

    let numberOfValue (span: Span) (v: Value) : Result<float, EvalError> =
        match v with
        | VNumber n -> Ok n
        | _ -> evalError span "expected number"

    let stringOfValue (span: Span) (v: Value) : Result<string, EvalError> =
        match v with
        | VString s -> Ok s
        | _ -> evalError span "expected string"

    let frameOfValue (span: Span) (v: Value) : Result<Frame, EvalError> =
        match v with
        | VFrame f -> Ok f
        | _ -> evalError span "expected frame"

    /// Project any structural value with a `signed_distance: VField`
    /// member to that field. Mirrors the type-level subtyping rule
    /// `(Loop | Primitive) { signed_distance: Field } <: Field`.
    /// Every VField-consuming site calls this so loops and primitives
    /// stand in for fields wherever fields are expected.
    let projectToFieldValue (v: Value) : Value option =
        match v with
        | VLoop lv ->
            match Map.tryFind "signed_distance" lv.Fields with
            | Some (VField _ as f) -> Some f
            | _ -> None
        | VPrimitive pv ->
            match Map.tryFind "signed_distance" pv.Fields with
            | Some (VField _ as f) -> Some f
            | _ -> None
        | _ -> None

    let fieldOfValue (span: Span) (v: Value) : Result<MathIr.Expr, EvalError> =
        match v with
        | VField f -> Ok f
        | _ ->
            match projectToFieldValue v with
            | Some (VField f) -> Ok f
            | _ -> evalError span "expected field"
