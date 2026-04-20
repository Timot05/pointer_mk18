import { printf, toText } from "../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { Record } from "../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, array_type, float32_type, string_type } from "../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { LabelPos_$reflection } from "../core/Sketch/Sketch.fs.js";
import { map } from "../ui/fable_modules/fable-library-js.4.29.0/Option.js";
import { dimensionFallbackAnchor } from "./SketchOverlayRender.fs.js";
import { item } from "../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { fold } from "../ui/fable_modules/fable-library-js.4.29.0/Seq.js";
import { tryFind } from "../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { disposeSafe, getEnumerator } from "../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { iterateIndexed, exists } from "../ui/fable_modules/fable-library-js.4.29.0/List.js";

const LABEL_COLOUR = new Float32Array([0.4269999861717224, 0.3409999907016754, 0.19200000166893005, 1]);

const LABEL_COLOUR_HOVER = new Float32Array([0.7250000238418579, 0.5099999904632568, 0.17000000178813934, 1]);

const DISPLAY_PX = 14;

function formatDistance(d) {
    if (Math.abs(d) < 0.001) {
        return "0";
    }
    else {
        return toText(printf("%.2f"))(d);
    }
}

function formatAngle(radians) {
    const deg = (radians * 180) / 3.141592653589793;
    return toText(printf("%.1f°"))(deg);
}

export class Label extends Record {
    constructor(Text$, Anchor, Color) {
        super();
        this.Text = Text$;
        this.Anchor = Anchor;
        this.Color = Color;
    }
}

export function Label_$reflection() {
    return record_type("LabelBuilder.Label", [], Label, () => [["Text", string_type], ["Anchor", LabelPos_$reflection()], ["Color", array_type(float32_type)]]);
}

function constraintText(c) {
    let matchResult, d;
    switch (c.tag) {
        case 6: {
            matchResult = 0;
            d = c.fields[2];
            break;
        }
        case 7: {
            matchResult = 0;
            d = c.fields[3];
            break;
        }
        case 18: {
            matchResult = 0;
            d = c.fields[6];
            break;
        }
        case 19: {
            matchResult = 0;
            d = c.fields[5];
            break;
        }
        case 20: {
            matchResult = 0;
            d = c.fields[4];
            break;
        }
        case 21: {
            matchResult = 0;
            d = c.fields[3];
            break;
        }
        case 22: {
            matchResult = 0;
            d = c.fields[5];
            break;
        }
        case 23: {
            matchResult = 0;
            d = c.fields[4];
            break;
        }
        case 17: {
            matchResult = 0;
            d = c.fields[2];
            break;
        }
        case 24: {
            matchResult = 1;
            break;
        }
        default:
            matchResult = 2;
    }
    switch (matchResult) {
        case 0:
            return formatDistance(d);
        case 1:
            return formatAngle(c.fields[6]);
        default:
            return undefined;
    }
}

function constraintLabelPos(c) {
    let matchResult, lp;
    switch (c.tag) {
        case 6: {
            matchResult = 0;
            lp = c.fields[3];
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
        default:
            matchResult = 1;
    }
    switch (matchResult) {
        case 0:
            return lp;
        default:
            return undefined;
    }
}

function constraintLabel(points, radiusLookup, c) {
    let matchValue_1;
    const matchValue = constraintText(c);
    if (matchValue != null) {
        const text = matchValue;
        return map((a) => (new Label(text, a, LABEL_COLOUR)), (matchValue_1 = constraintLabelPos(c), (matchValue_1 == null) ? dimensionFallbackAnchor(points, radiusLookup, c) : matchValue_1));
    }
    else {
        return undefined;
    }
}

function pushVertex(out, anchor, ox, oy, u, v, color) {
    void (out.push(anchor.X));
    void (out.push(anchor.Y));
    void (out.push(ox));
    void (out.push(oy));
    void (out.push(u));
    void (out.push(v));
    void (out.push(item(0, color)));
    void (out.push(item(1, color)));
    void (out.push(item(2, color)));
    void (out.push(item(3, color)));
}

function measureText(metrics, text) {
    return fold((acc, ch) => {
        const matchValue = tryFind(ch, metrics.Chars);
        if (matchValue == null) {
            return acc;
        }
        else {
            return acc + matchValue.XAdvance;
        }
    }, 0, text.split(""));
}

function appendCharQuads(out, metrics, scale, label) {
    let cursorX = -measureText(metrics, label.Text) * 0.5;
    const baseline = metrics.Base * 0.5;
    const invAtlasW = 1 / metrics.ScaleW;
    const invAtlasH = 1 / metrics.ScaleH;
    const enumerator = getEnumerator(label.Text.split(""));
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const matchValue = tryFind(enumerator["System.Collections.Generic.IEnumerator`1.get_Current"](), metrics.Chars);
            if (matchValue != null) {
                const fc = matchValue;
                const px0 = (cursorX + fc.XOffset) * scale;
                const py0 = -(baseline - fc.YOffset) * scale;
                const px1 = ((cursorX + fc.XOffset) + fc.Width) * scale;
                const py1 = -((baseline - fc.YOffset) - fc.Height) * scale;
                const u0 = fc.X * invAtlasW;
                const v0 = fc.Y * invAtlasH;
                const u1 = (fc.X + fc.Width) * invAtlasW;
                const v1 = (fc.Y + fc.Height) * invAtlasH;
                pushVertex(out, label.Anchor, px0, py0, u0, v0, label.Color);
                pushVertex(out, label.Anchor, px1, py0, u1, v0, label.Color);
                pushVertex(out, label.Anchor, px0, py1, u0, v1, label.Color);
                pushVertex(out, label.Anchor, px1, py0, u1, v0, label.Color);
                pushVertex(out, label.Anchor, px1, py1, u1, v1, label.Color);
                pushVertex(out, label.Anchor, px0, py1, u0, v1, label.Color);
                cursorX = (cursorX + fc.XAdvance);
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
}

function isDimensionActive(sketchId, index, hovered, selected) {
    const matches = (t) => {
        if (t.tag === 5) {
            if (t.fields[0] === sketchId) {
                return t.fields[1] === index;
            }
            else {
                return false;
            }
        }
        else {
            return false;
        }
    };
    if ((hovered == null) ? false : matches(hovered)) {
        return true;
    }
    else {
        return exists(matches, selected);
    }
}

/**
 * Build the vertex buffer for all labels produced by the given sketch's
 * dimensional constraints. Active labels (hovered / selected) render in
 * a brighter accent colour.
 */
export function buildSketchLabelBuffer(metrics, points, radiusLookup, sketchId, constraints, hovered, selected) {
    const scale = (metrics.LineHeight > 0) ? (DISPLAY_PX / metrics.LineHeight) : 1;
    const out = [];
    iterateIndexed((i, c) => {
        const matchValue = constraintLabel(points, radiusLookup, c);
        if (matchValue == null) {
        }
        else {
            const label = matchValue;
            appendCharQuads(out, metrics, scale, new Label(label.Text, label.Anchor, isDimensionActive(sketchId, i, hovered, selected) ? LABEL_COLOUR_HOVER : LABEL_COLOUR));
        }
    }, constraints);
    return out.slice();
}

