import { Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, float32_type, array_type, uint32_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { item, map, tryItem, setItem, iterateIndexed } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { defaultArg } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { join } from "../../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { int32ToString } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";

export class PackedGpuGraph extends Record {
    constructor(PackedNodes, Consts, VarSlotNodes) {
        super();
        this.PackedNodes = PackedNodes;
        this.Consts = Consts;
        this.VarSlotNodes = VarSlotNodes;
    }
}

export function PackedGpuGraph_$reflection() {
    return record_type("Server.PackedGpuGraph", [], PackedGpuGraph, () => [["PackedNodes", array_type(uint32_type)], ["Consts", array_type(float32_type)], ["VarSlotNodes", array_type(uint32_type)]]);
}

function GpuGraph_opCode(_arg) {
    switch (_arg.tag) {
        case 1:
            return 1;
        case 2:
            return 2;
        case 3:
            return 3;
        case 4:
            return 4;
        case 5:
            return 5;
        case 6:
            return 6;
        case 7:
            return 7;
        case 8:
            return 8;
        case 9:
            return 9;
        case 10:
            return 10;
        default:
            return 0;
    }
}

export function GpuGraph_slotToNodeIndex(graph) {
    const slotToNode = new Int32Array(graph.Params.length);
    iterateIndexed((nodeIndex, node) => {
        const matchValue = node.Op;
        if (matchValue.tag === 1) {
            setItem(slotToNode, matchValue.fields[0], nodeIndex | 0);
        }
    }, graph.Nodes);
    return slotToNode;
}

export function GpuGraph_packGraph(graph) {
    const packed = new Uint32Array(graph.Nodes.length * 4);
    const consts = [];
    iterateIndexed((index, node) => {
        let a;
        const value_1 = defaultArg(tryItem(0, node.Inputs), 0) | 0;
        a = (value_1 >>> 0);
        let b;
        const value_3 = defaultArg(tryItem(1, node.Inputs), 0) | 0;
        b = (value_3 >>> 0);
        let aux;
        const matchValue = node.Op;
        switch (matchValue.tag) {
            case 0: {
                const offset = consts.length >>> 0;
                void (consts.push(matchValue.fields[0]));
                aux = offset;
                break;
            }
            case 1: {
                aux = (matchValue.fields[0] >>> 0);
                break;
            }
            default:
                aux = 0;
        }
        setItem(packed, index * 4, GpuGraph_opCode(node.Op));
        setItem(packed, (index * 4) + 1, a);
        setItem(packed, (index * 4) + 2, b);
        setItem(packed, (index * 4) + 3, aux);
    }, graph.Nodes);
    const slotToNode = GpuGraph_slotToNodeIndex(graph);
    const varSlotNodes = map((slot_1) => (item(slot_1, slotToNode) >>> 0), graph.VarSlots, Uint32Array);
    return new PackedGpuGraph(packed, consts.slice(), varSlotNodes);
}

export function GpuGraph_graphKey(graph) {
    const packed = GpuGraph_packGraph(graph);
    return join("|", [int32ToString(graph.Nodes.length), int32ToString(graph.Params.length), int32ToString(graph.Outputs.length), int32ToString(graph.VarSlots.length), join(",", map((value) => value.toString(), packed.PackedNodes)), join(",", map((value_1) => value_1.toString(), packed.Consts)), join(",", map(int32ToString, graph.Outputs)), join(",", map(int32ToString, graph.VarSlots))]);
}

