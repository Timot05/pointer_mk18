namespace Server

// ---------------------------------------------------------------------------
// Flat-arena computation graph IR.
//
// Produced by SketchCompile from an ActionSketch. Consumed (later) by a
// GPU solver. Topologically sorted: for every Node, all entries in Inputs
// are strictly smaller indices.
//
// Deliberately small. Every sketch constraint reduces to this op set.
// ---------------------------------------------------------------------------

type Op =
    | Constant of value: float
    | Param    of slot: int
    | Neg | Sin | Cos | Sqrt
    | Add | Sub | Mul | Div
    | Atan2

type Node = { Op: Op; Inputs: int[] }

type Graph =
    { Nodes:    Node[]
      Params:   float[]
      Outputs:  int[]
      VarSlots: int[] }

/// Mutable builder for Graph. Appends nodes in evaluation order.
type GraphBuilder() =
    let nodes = ResizeArray<Node>()
    let initial = ResizeArray<float>()

    member _.Constant (v: float) : int =
        let id = nodes.Count
        nodes.Add { Op = Constant v; Inputs = [||] }
        id

    member _.Param (init: float) : int =
        let slot = initial.Count
        initial.Add init
        let id = nodes.Count
        nodes.Add { Op = Param slot; Inputs = [||] }
        id

    member _.ParamCount : int = initial.Count

    member private this.Unary (op: Op) (a: int) : int =
        let id = nodes.Count
        nodes.Add { Op = op; Inputs = [| a |] }
        id

    member private this.Binary (op: Op) (a: int) (b: int) : int =
        let id = nodes.Count
        nodes.Add { Op = op; Inputs = [| a; b |] }
        id

    member this.Neg a = this.Unary Neg a
    member this.Sin a = this.Unary Sin a
    member this.Cos a = this.Unary Cos a
    member this.Sqrt a = this.Unary Sqrt a
    member this.Add (a, b) = this.Binary Add a b
    member this.Sub (a, b) = this.Binary Sub a b
    member this.Mul (a, b) = this.Binary Mul a b
    member this.Div (a, b) = this.Binary Div a b
    /// atan2(y, x): first arg is y, second is x (standard signature).
    member this.Atan2 (y, x) = this.Binary Atan2 y x

    member _.Build (outputs: int[], varSlots: int[]) : Graph =
        { Nodes    = nodes.ToArray()
          Params   = initial.ToArray()
          Outputs  = outputs
          VarSlots = varSlots }
