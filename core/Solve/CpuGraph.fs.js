import { map, setItem, item, iterateIndexed } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { GpuGraph_slotToNodeIndex } from "./GpuGraph.fs.js";
import { disposeSafe, getEnumerator } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { rangeDouble } from "../../ui/fable_modules/fable-library-js.4.29.0/Range.js";

export function evaluateValues(graph, paramValues) {
    const values = new Float64Array(graph.Nodes.length);
    iterateIndexed((index, node) => {
        let value;
        const matchValue = node.Op;
        value = ((matchValue.tag === 1) ? item(matchValue.fields[0], paramValues) : ((matchValue.tag === 2) ? -item(item(0, node.Inputs), values) : ((matchValue.tag === 3) ? Math.sin(item(item(0, node.Inputs), values)) : ((matchValue.tag === 4) ? Math.cos(item(item(0, node.Inputs), values)) : ((matchValue.tag === 5) ? Math.sqrt(item(item(0, node.Inputs), values)) : ((matchValue.tag === 6) ? (item(item(0, node.Inputs), values) + item(item(1, node.Inputs), values)) : ((matchValue.tag === 7) ? (item(item(0, node.Inputs), values) - item(item(1, node.Inputs), values)) : ((matchValue.tag === 8) ? (item(item(0, node.Inputs), values) * item(item(1, node.Inputs), values)) : ((matchValue.tag === 9) ? (item(item(0, node.Inputs), values) / item(item(1, node.Inputs), values)) : ((matchValue.tag === 10) ? Math.atan2(item(item(0, node.Inputs), values), item(item(1, node.Inputs), values)) : matchValue.fields[0]))))))))));
        setItem(values, index, value);
    }, graph.Nodes);
    return values;
}

export function evaluateOutputs(graph, paramValues) {
    const values = evaluateValues(graph, paramValues);
    return map((output) => item(output, values), graph.Outputs, Float64Array);
}

export function jacobianReverse(graph, paramValues) {
    const values = evaluateValues(graph, paramValues);
    let varSlotNodes;
    const slotToNode = GpuGraph_slotToNodeIndex(graph);
    varSlotNodes = map((slot) => item(slot, slotToNode), graph.VarSlots, Int32Array);
    const nRes = graph.Outputs.length | 0;
    const nVar = graph.VarSlots.length | 0;
    const jacobian = new Float64Array(nRes * nVar);
    for (let residualIndex = 0; residualIndex <= (nRes - 1); residualIndex++) {
        const adjoints = new Float64Array(graph.Nodes.length);
        setItem(adjoints, item(residualIndex, graph.Outputs), 1);
        const enumerator = getEnumerator(rangeDouble(graph.Nodes.length - 1, -1, 0));
        try {
            while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
                const reverseIndex = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]() | 0;
                const ai = item(reverseIndex, adjoints);
                if (ai !== 0) {
                    const matchValue = item(reverseIndex, graph.Nodes).Op;
                    switch (matchValue.tag) {
                        case 2: {
                            const a = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            setItem(adjoints, a, item(a, adjoints) - ai);
                            break;
                        }
                        case 3: {
                            const a_1 = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            setItem(adjoints, a_1, item(a_1, adjoints) + (ai * Math.cos(item(a_1, values))));
                            break;
                        }
                        case 4: {
                            const a_2 = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            setItem(adjoints, a_2, item(a_2, adjoints) - (ai * Math.sin(item(a_2, values))));
                            break;
                        }
                        case 5: {
                            const a_3 = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            const v = item(a_3, values);
                            if (v > 0) {
                                setItem(adjoints, a_3, item(a_3, adjoints) + (ai / (2 * Math.sqrt(v))));
                            }
                            break;
                        }
                        case 6: {
                            const a_4 = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            const b = item(1, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            setItem(adjoints, a_4, item(a_4, adjoints) + ai);
                            setItem(adjoints, b, item(b, adjoints) + ai);
                            break;
                        }
                        case 7: {
                            const a_5 = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            const b_1 = item(1, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            setItem(adjoints, a_5, item(a_5, adjoints) + ai);
                            setItem(adjoints, b_1, item(b_1, adjoints) - ai);
                            break;
                        }
                        case 8: {
                            const a_6 = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            const b_2 = item(1, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            setItem(adjoints, a_6, item(a_6, adjoints) + (ai * item(b_2, values)));
                            setItem(adjoints, b_2, item(b_2, adjoints) + (ai * item(a_6, values)));
                            break;
                        }
                        case 9: {
                            const a_7 = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            const b_3 = item(1, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            const bv = item(b_3, values);
                            setItem(adjoints, a_7, item(a_7, adjoints) + (ai / bv));
                            setItem(adjoints, b_3, item(b_3, adjoints) - ((ai * item(a_7, values)) / (bv * bv)));
                            break;
                        }
                        case 10: {
                            const y = item(0, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            const x = item(1, item(reverseIndex, graph.Nodes).Inputs) | 0;
                            const yv = item(y, values);
                            const xv = item(x, values);
                            const denom = (xv * xv) + (yv * yv);
                            if (denom > 0) {
                                setItem(adjoints, y, item(y, adjoints) + ((ai * xv) / denom));
                                setItem(adjoints, x, item(x, adjoints) - ((ai * yv) / denom));
                            }
                            break;
                        }
                        default:
                            undefined;
                    }
                }
            }
        }
        finally {
            disposeSafe(enumerator);
        }
        for (let varIndex = 0; varIndex <= (nVar - 1); varIndex++) {
            setItem(jacobian, (residualIndex * nVar) + varIndex, item(item(varIndex, varSlotNodes), adjoints));
        }
    }
    return jacobian;
}

