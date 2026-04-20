import { Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { class_type, record_type, array_type, union_type, int32_type, float64_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";

export class Op extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["Constant", "Param", "Neg", "Sin", "Cos", "Sqrt", "Add", "Sub", "Mul", "Div", "Atan2"];
    }
}

export function Op_$reflection() {
    return union_type("Server.Op", [], Op, () => [[["value", float64_type]], [["slot", int32_type]], [], [], [], [], [], [], [], [], []]);
}

export class Node$ extends Record {
    constructor(Op, Inputs) {
        super();
        this.Op = Op;
        this.Inputs = Inputs;
    }
}

export function Node$_$reflection() {
    return record_type("Server.Node", [], Node$, () => [["Op", Op_$reflection()], ["Inputs", array_type(int32_type)]]);
}

export class Graph extends Record {
    constructor(Nodes, Params, Outputs, VarSlots) {
        super();
        this.Nodes = Nodes;
        this.Params = Params;
        this.Outputs = Outputs;
        this.VarSlots = VarSlots;
    }
}

export function Graph_$reflection() {
    return record_type("Server.Graph", [], Graph, () => [["Nodes", array_type(Node$_$reflection())], ["Params", array_type(float64_type)], ["Outputs", array_type(int32_type)], ["VarSlots", array_type(int32_type)]]);
}

export class GraphBuilder {
    constructor() {
        this.nodes = [];
        this.initial = [];
    }
}

export function GraphBuilder_$reflection() {
    return class_type("Server.GraphBuilder", undefined, GraphBuilder);
}

export function GraphBuilder_$ctor() {
    return new GraphBuilder();
}

export function GraphBuilder__Constant_5E38073B(_, v) {
    const id = _.nodes.length | 0;
    void (_.nodes.push(new Node$(new Op(0, [v]), new Int32Array([]))));
    return id | 0;
}

export function GraphBuilder__Param_5E38073B(_, init) {
    const slot = _.initial.length | 0;
    void (_.initial.push(init));
    const id = _.nodes.length | 0;
    void (_.nodes.push(new Node$(new Op(1, [slot]), new Int32Array([]))));
    return id | 0;
}

export function GraphBuilder__get_ParamCount(_) {
    return _.initial.length;
}

function GraphBuilder__Unary(this$, op, a) {
    const id = this$.nodes.length | 0;
    void (this$.nodes.push(new Node$(op, new Int32Array([a]))));
    return id | 0;
}

function GraphBuilder__Binary(this$, op, a, b) {
    const id = this$.nodes.length | 0;
    void (this$.nodes.push(new Node$(op, new Int32Array([a, b]))));
    return id | 0;
}

export function GraphBuilder__Neg_Z524259A4(this$, a) {
    return GraphBuilder__Unary(this$, new Op(2, []), a);
}

export function GraphBuilder__Sin_Z524259A4(this$, a) {
    return GraphBuilder__Unary(this$, new Op(3, []), a);
}

export function GraphBuilder__Cos_Z524259A4(this$, a) {
    return GraphBuilder__Unary(this$, new Op(4, []), a);
}

export function GraphBuilder__Sqrt_Z524259A4(this$, a) {
    return GraphBuilder__Unary(this$, new Op(5, []), a);
}

export function GraphBuilder__Add_Z37302880(this$, a, b) {
    return GraphBuilder__Binary(this$, new Op(6, []), a, b);
}

export function GraphBuilder__Sub_Z37302880(this$, a, b) {
    return GraphBuilder__Binary(this$, new Op(7, []), a, b);
}

export function GraphBuilder__Mul_Z37302880(this$, a, b) {
    return GraphBuilder__Binary(this$, new Op(8, []), a, b);
}

export function GraphBuilder__Div_Z37302880(this$, a, b) {
    return GraphBuilder__Binary(this$, new Op(9, []), a, b);
}

/**
 * atan2(y, x): first arg is y, second is x (standard signature).
 */
export function GraphBuilder__Atan2_Z37302880(this$, y, x) {
    return GraphBuilder__Binary(this$, new Op(10, []), y, x);
}

export function GraphBuilder__Build_7E3D5760(_, outputs, varSlots) {
    return new Graph(_.nodes.slice(), _.initial.slice(), outputs, varSlots);
}

