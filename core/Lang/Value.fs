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

    /// Authoring-time sketch: the same `ActionSketch` shape mk18's UI
    /// produces (entities + constraints, raw float coords, string ids),
    /// plus the plane the sketch lives in. Lowering to MathIR primitives
    /// + slot allocation happens at the consumer (a future
    /// `@sketch_distance` / `@sketch_path` builtin), not here.
    type SketchValue = {
        Sketch: Server.ActionSketch
        Plane:  Server.SketchPlane
    }

    type CurveValue = { PrimitiveId: int; Plane: MathIr.Plane }

    type Value =
        | VNumber of float
        | VBool of bool
        | VString of string
        | VField of MathIr.Expr
        | VFrame of Frame
        | VSketch of SketchValue
        | VCurve of CurveValue
        | VRecord of (string * Value) list
        | VClosure of Closure
        | VUnit

    and Closure = {
        Param: string
        Body: Expr
        Captured: Env
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

    /// Hooks for the @input / @output / @view / @print / @debug specials.
    /// Notebook-driver-supplied; the standalone test harness ships a stub
    /// that records calls or returns errors.
    type Specials = {
        Input:  Span -> string -> Result<Value, EvalError>
        Output: Span -> string -> Value -> Result<Value, EvalError>
        View:   Span -> Value -> Result<Value, EvalError>
        Print:  Span -> string -> Value -> Result<Value, EvalError>
        Debug:  Span -> Value -> Result<Value, EvalError>
    }

    /// Specials that error on every call — used when no notebook context
    /// is available.
    let unboundSpecials : Specials = {
        Input  = fun sp _      -> evalError sp "@input not bound (no notebook context)"
        Output = fun sp _ _    -> evalError sp "@output not bound (no notebook context)"
        View   = fun sp _      -> evalError sp "@view not bound (no notebook context)"
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

    let fieldOfValue (span: Span) (v: Value) : Result<MathIr.Expr, EvalError> =
        match v with
        | VField f -> Ok f
        | _ -> evalError span "expected field"
