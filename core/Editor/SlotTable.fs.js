import { FSharpRef, Record } from "../../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { class_type, int32_type, array_type, float64_type, record_type, string_type } from "../../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { Dictionary } from "../../ui/fable_modules/fable-library-js.4.29.0/MutableMap.js";
import { compare, safeHash, equals } from "../../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { tryGetValue } from "../../ui/fable_modules/fable-library-js.4.29.0/MapUtil.js";
import { tryFind, ofSeq } from "../../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { map } from "../../ui/fable_modules/fable-library-js.4.29.0/Seq.js";
import { item, setItem, copy } from "../../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { iterate } from "../../ui/fable_modules/fable-library-js.4.29.0/List.js";
import { map as map_1 } from "../../ui/fable_modules/fable-library-js.4.29.0/Option.js";

export class SlotRef extends Record {
    constructor(ActionId, Path) {
        super();
        this.ActionId = ActionId;
        this.Path = Path;
    }
}

export function SlotRef_$reflection() {
    return record_type("Server.SlotRef", [], SlotRef, () => [["ActionId", string_type], ["Path", string_type]]);
}

export class SlotTable extends Record {
    constructor(Values, Index) {
        super();
        this.Values = Values;
        this.Index = Index;
    }
}

export function SlotTable_$reflection() {
    return record_type("Server.SlotTable", [], SlotTable, () => [["Values", array_type(float64_type)], ["Index", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [SlotRef_$reflection(), int32_type])]]);
}

export class SlotTableModule_Builder extends Record {
    constructor(Values, Index) {
        super();
        this.Values = Values;
        this.Index = Index;
    }
}

export function SlotTableModule_Builder_$reflection() {
    return record_type("Server.SlotTableModule.Builder", [], SlotTableModule_Builder, () => [["Values", array_type(float64_type)], ["Index", class_type("System.Collections.Generic.Dictionary`2", [SlotRef_$reflection(), int32_type])]]);
}

export function SlotTableModule_createBuilder() {
    return new SlotTableModule_Builder([], new Dictionary([], {
        Equals: equals,
        GetHashCode: safeHash,
    }));
}

/**
 * Returns the slot for this ref; allocates if new, reuses if already seen
 * (idempotent). On first allocation seeds Values with the provided default.
 */
export function SlotTableModule_alloc(b, ref, defaultVal) {
    let matchValue;
    let outArg = 0;
    matchValue = [tryGetValue(b.Index, ref, new FSharpRef(() => outArg, (v) => {
        outArg = (v | 0);
    })), outArg];
    if (matchValue[0]) {
        return matchValue[1] | 0;
    }
    else {
        const slot_1 = b.Values.length | 0;
        void (b.Values.push(defaultVal));
        b.Index.set(ref, slot_1);
        return slot_1 | 0;
    }
}

export function SlotTableModule_tryGet(b, ref) {
    let matchValue;
    let outArg = 0;
    matchValue = [tryGetValue(b.Index, ref, new FSharpRef(() => outArg, (v) => {
        outArg = (v | 0);
    })), outArg];
    if (matchValue[0]) {
        return matchValue[1];
    }
    else {
        return undefined;
    }
}

export function SlotTableModule_toTable(b) {
    const index = ofSeq(map((kv) => [kv[0], kv[1]], b.Index), {
        Compare: compare,
    });
    return new SlotTable(b.Values.slice(), index);
}

export function SlotTableModule_tryFindSlot(table, ref) {
    return tryFind(ref, table.Index);
}

export function SlotTableModule_patchedValues(values, updates) {
    const next = copy(values);
    iterate((tupledArg) => {
        setItem(next, tupledArg[0], tupledArg[1]);
    }, updates);
    return next;
}

/**
 * In-place update of a slot value. Returns true on hit, false if the
 * ref isn't allocated. Used by the rapid-drag fast path.
 */
export function SlotTableModule_update(table, ref, value) {
    const matchValue = tryFind(ref, table.Index);
    if (matchValue == null) {
        return false;
    }
    else {
        const slot = matchValue | 0;
        table.Values[slot] = value;
        return true;
    }
}

export function SlotTableModule_valueAt(table, ref) {
    return map_1((s) => item(s, table.Values), tryFind(ref, table.Index));
}

