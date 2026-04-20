import { Union, Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { list_type, option_type, union_type, bool_type, string_type, record_type, float64_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { empty } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";

export class FreePoint extends Record {
    constructor(X, Y) {
        super();
        this.X = X;
        this.Y = Y;
    }
}

export function FreePoint_$reflection() {
    return record_type("Server.FreePoint", [], FreePoint, () => [["X", float64_type], ["Y", float64_type]]);
}

export class ArcData extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["ArcCenter", "ArcThreePoint"];
    }
}

export function ArcData_$reflection() {
    return union_type("Server.ArcData", [], ArcData, () => [[["center", string_type], ["clockwise", bool_type]], [["through", FreePoint_$reflection()]]]);
}

export class RenderEntity extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["REPoint", "RELine", "RECircle", "REArc"];
    }
}

export function RenderEntity_$reflection() {
    return union_type("Server.RenderEntity", [], RenderEntity, () => [[["id", string_type], ["x", float64_type], ["y", float64_type]], [["id", string_type], ["startId", string_type], ["endId", string_type]], [["id", string_type], ["center", string_type], ["radius", float64_type]], [["id", string_type], ["startId", string_type], ["endId", string_type], ["data", ArcData_$reflection()]]]);
}

export class LabelPos extends Record {
    constructor(X, Y) {
        super();
        this.X = X;
        this.Y = Y;
    }
}

export function LabelPos_$reflection() {
    return record_type("Server.LabelPos", [], LabelPos, () => [["X", float64_type], ["Y", float64_type]]);
}

export class SketchConstraint extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["Fixed", "Coincident", "FrameCoincident", "Concentric", "Horizontal", "Vertical", "Distance", "FrameDistance", "Equal", "EqualRadius", "Midpoint", "Parallel", "FrameParallel", "Perpendicular", "FramePerpendicular", "Tangent", "CurveTangent", "CircleDiameter", "LineDistance", "FrameLineDistance", "PointLineDistance", "PointCircleDistance", "LineCircleDistance", "CircleCircleDistance", "Angle"];
    }
}

export function SketchConstraint_$reflection() {
    return union_type("Server.SketchConstraint", [], SketchConstraint, () => [[["point", string_type], ["x", float64_type], ["y", float64_type]], [["a", string_type], ["b", string_type]], [["point", string_type], ["frame", string_type], ["part", string_type]], [["entityA", string_type], ["entityB", string_type], ["centerA", string_type], ["centerB", string_type]], [["a", string_type], ["b", string_type]], [["a", string_type], ["b", string_type]], [["a", string_type], ["b", string_type], ["distance", float64_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["point", string_type], ["frame", string_type], ["part", string_type], ["distance", float64_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["aStart", string_type], ["aEnd", string_type], ["bStart", string_type], ["bEnd", string_type], ["lineA", string_type], ["lineB", string_type]], [["entityA", string_type], ["entityB", string_type]], [["point", string_type], ["lineA", string_type], ["aStart", string_type], ["aEnd", string_type]], [["aStart", string_type], ["aEnd", string_type], ["bStart", string_type], ["bEnd", string_type], ["lineA", string_type], ["lineB", string_type]], [["aStart", string_type], ["aEnd", string_type], ["lineA", string_type], ["frame", string_type], ["part", string_type]], [["aStart", string_type], ["aEnd", string_type], ["bStart", string_type], ["bEnd", string_type], ["lineA", string_type], ["lineB", string_type]], [["aStart", string_type], ["aEnd", string_type], ["lineA", string_type], ["frame", string_type], ["part", string_type]], [["aStart", string_type], ["aEnd", string_type], ["center", string_type], ["circle", string_type], ["lineA", string_type], ["radius", float64_type]], [["entityA", string_type], ["centerA", string_type], ["entityB", string_type], ["centerB", string_type], ["internal", bool_type]], [["circle", string_type], ["center", string_type], ["diameter", float64_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["aStart", string_type], ["aEnd", string_type], ["bStart", string_type], ["bEnd", string_type], ["lineA", string_type], ["lineB", string_type], ["distance", float64_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["lineA", string_type], ["aStart", string_type], ["aEnd", string_type], ["frame", string_type], ["part", string_type], ["distance", float64_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["point", string_type], ["lineA", string_type], ["aStart", string_type], ["aEnd", string_type], ["distance", float64_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["point", string_type], ["circle", string_type], ["center", string_type], ["distance", float64_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["lineA", string_type], ["aStart", string_type], ["aEnd", string_type], ["circle", string_type], ["center", string_type], ["distance", float64_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["circleA", string_type], ["centerA", string_type], ["circleB", string_type], ["centerB", string_type], ["distance", float64_type], ["internal", bool_type], ["labelPosition", option_type(LabelPos_$reflection())]], [["aStart", string_type], ["aEnd", string_type], ["bStart", string_type], ["bEnd", string_type], ["lineA", string_type], ["lineB", string_type], ["angle", float64_type], ["aReverse", bool_type], ["bReverse", bool_type], ["ccwFromAToB", bool_type], ["labelPosition", option_type(LabelPos_$reflection())]]]);
}

/**
 * Extract the optional label position from any constraint variant.
 * Exhaustive: when a new variant is added, this match forces a decision here.
 */
export function SketchConstraintModule_labelPos(c) {
    let matchResult, lp;
    switch (c.tag) {
        case 0:
        case 1:
        case 2:
        case 3:
        case 4:
        case 5:
        case 8:
        case 9:
        case 10:
        case 11:
        case 12:
        case 13:
        case 14:
        case 15:
        case 16: {
            matchResult = 1;
            break;
        }
        case 7: {
            matchResult = 0;
            lp = c.fields[4];
            break;
        }
        case 18: {
            matchResult = 0;
            lp = c.fields[7];
            break;
        }
        case 19: {
            matchResult = 0;
            lp = c.fields[6];
            break;
        }
        case 20: {
            matchResult = 0;
            lp = c.fields[5];
            break;
        }
        case 21: {
            matchResult = 0;
            lp = c.fields[4];
            break;
        }
        case 22: {
            matchResult = 0;
            lp = c.fields[6];
            break;
        }
        case 23: {
            matchResult = 0;
            lp = c.fields[6];
            break;
        }
        case 17: {
            matchResult = 0;
            lp = c.fields[3];
            break;
        }
        case 24: {
            matchResult = 0;
            lp = c.fields[10];
            break;
        }
        default: {
            matchResult = 0;
            lp = c.fields[3];
        }
    }
    switch (matchResult) {
        case 0:
            return lp;
        default:
            return undefined;
    }
}

export class ActionSketch extends Record {
    constructor(Entities, Constraints) {
        super();
        this.Entities = Entities;
        this.Constraints = Constraints;
    }
}

export function ActionSketch_$reflection() {
    return record_type("Server.ActionSketch", [], ActionSketch, () => [["Entities", list_type(RenderEntity_$reflection())], ["Constraints", list_type(SketchConstraint_$reflection())]]);
}

export const ActionSketchModule_empty = new ActionSketch(empty(), empty());

export class SketchPlane extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["XY", "XZ", "YZ"];
    }
}

export function SketchPlane_$reflection() {
    return union_type("Server.SketchPlane", [], SketchPlane, () => [[], [], []]);
}

export const SketchPlaneModule_defaults = new SketchPlane(0, []);

export class FromSketchSelection extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["SelectionLoop", "SelectionElements"];
    }
}

export function FromSketchSelection_$reflection() {
    return union_type("Server.FromSketchSelection", [], FromSketchSelection, () => [[["loopId", option_type(string_type)]], [["lineIds", list_type(string_type)]]]);
}

export const FromSketchSelectionModule_defaults = new FromSketchSelection(0, [undefined]);

