export type Op =
  | { case: "Constant"; value: number }
  | { case: "Param"; slot: number }
  | { case: "Neg" | "Sin" | "Cos" | "Sqrt" }
  | { case: "Add" | "Sub" | "Mul" | "Div" | "Atan2" };

export interface Node {
  op: Op;
  inputs: number[];
}

export interface Graph {
  nodes: Node[];
  params: Float32Array;
  outputs: Int32Array;
  varSlots: Int32Array;
}

export interface GraphJson {
  nodes: Node[];
  params: number[];
  outputs: number[];
  varSlots: number[];
}

export function graphFromJson(json: GraphJson): Graph {
  return {
    nodes: json.nodes,
    params: new Float32Array(json.params),
    outputs: new Int32Array(json.outputs),
    varSlots: new Int32Array(json.varSlots),
  };
}

export function slotToNodeIndex(g: Graph): Int32Array {
  const m = new Int32Array(g.params.length);
  for (let i = 0; i < g.nodes.length; i++) {
    const op = g.nodes[i].op;
    if (op.case === "Param") m[op.slot] = i;
  }
  return m;
}
