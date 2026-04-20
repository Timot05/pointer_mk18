import { Record, Union } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { list_type, record_type, int32_type, union_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { FieldInterval_simplify, IntervalBox, IntervalModule_make, Interval_$reflection, IntervalBox_$reflection } from "./FieldInterval.fs.js";
import { FieldNode_$reflection } from "./FieldIR.fs.js";
import { fold as fold_1, singleton, empty, append, map, collect, toArray, ofArray } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { max } from "../../ui/fable_modules/fable-library-js.4.29.0/Double.js";
import { equals } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { fold } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";

export class TileClass extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["Outside", "Inside", "Ambiguous"];
    }
}

export function TileClass_$reflection() {
    return union_type("Server.TileClass", [], TileClass, () => [[], [], []]);
}

export class LeafTile extends Record {
    constructor(Box, Class, Depth, Bound, Node$) {
        super();
        this.Box = Box;
        this.Class = Class;
        this.Depth = (Depth | 0);
        this.Bound = Bound;
        this.Node = Node$;
    }
}

export function LeafTile_$reflection() {
    return record_type("Server.LeafTile", [], LeafTile, () => [["Box", IntervalBox_$reflection()], ["Class", TileClass_$reflection()], ["Depth", int32_type], ["Bound", Interval_$reflection()], ["Node", FieldNode_$reflection()]]);
}

export class TileStats extends Record {
    constructor(LeafTiles, EvalCount, MaxDepthReached) {
        super();
        this.LeafTiles = LeafTiles;
        this.EvalCount = (EvalCount | 0);
        this.MaxDepthReached = (MaxDepthReached | 0);
    }
}

export function TileStats_$reflection() {
    return record_type("Server.TileStats", [], TileStats, () => [["LeafTiles", list_type(LeafTile_$reflection())], ["EvalCount", int32_type], ["MaxDepthReached", int32_type]]);
}

/**
 * Split an IntervalBox into 8 equal children (octree).
 */
export function TileRecursion_split(b) {
    const halves = (i) => {
        const m = (i.Lo + i.Hi) * 0.5;
        return ofArray([IntervalModule_make(i.Lo, m), IntervalModule_make(m, i.Hi)]);
    };
    return toArray(collect((x) => collect((y) => map((z) => (new IntervalBox(x, y, z)), halves(b.ZI)), halves(b.YI)), halves(b.XI)));
}

export function TileRecursion_classify(i) {
    if (i.Lo > 0) {
        return new TileClass(0, []);
    }
    else if (i.Hi < 0) {
        return new TileClass(1, []);
    }
    else {
        return new TileClass(2, []);
    }
}

function TileRecursion_merge(a, b) {
    return new TileStats(append(b.LeafTiles, a.LeafTiles), a.EvalCount + b.EvalCount, max(a.MaxDepthReached, b.MaxDepthReached));
}

/**
 * Recursively subdivide. At each tile we `simplify` the node (computing
 * the interval bound AND a potentially smaller FieldNode with dominated
 * boolean branches removed) and pass the simplified tree to the 8 child
 * recursions. Pruned tiles (Outside/Inside) become leaves immediately;
 * Ambiguous tiles subdivide until maxDepth is reached.
 */
export function TileRecursion_recurse(slots, root, node, maxDepth) {
    const go = (b, n, depth) => {
        const patternInput = FieldInterval_simplify(slots, b, n);
        const simplified = patternInput[1];
        const bound = patternInput[0];
        const cls = TileRecursion_classify(bound);
        const self = new TileStats(empty(), 1, depth);
        if (!equals(cls, new TileClass(2, [])) ? true : (depth >= maxDepth)) {
            return new TileStats(singleton(new LeafTile(b, cls, depth, bound, simplified)), self.EvalCount, self.MaxDepthReached);
        }
        else {
            return fold((acc, child) => TileRecursion_merge(acc, go(child, simplified, depth + 1)), self, TileRecursion_split(b));
        }
    };
    return go(root, node, 0);
}

/**
 * Convenience: count leaves by class as (outside, inside, ambiguous).
 */
export function TileRecursion_countByClass(stats) {
    return fold_1((tupledArg, t) => {
        const o = tupledArg[0] | 0;
        const i = tupledArg[1] | 0;
        const a = tupledArg[2] | 0;
        const matchValue = t.Class;
        switch (matchValue.tag) {
            case 1:
                return [o, i + 1, a];
            case 2:
                return [o, i, a + 1];
            default:
                return [o + 1, i, a];
        }
    }, [0, 0, 0], stats.LeafTiles);
}

