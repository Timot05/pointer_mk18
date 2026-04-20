import { Record } from "./fable_modules/fable-library-js.4.29.0/Types.js";
import { Effect_$reflection } from "../core/Editor/Editor.fs.js";
import { record_type, unit_type, lambda_type, tuple_type, list_type } from "./fable_modules/fable-library-js.4.29.0/Reflection.js";
import { cons, empty } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { disposeSafe, getEnumerator } from "./fable_modules/fable-library-js.4.29.0/Util.js";

export class Store$2 extends Record {
    constructor(State, Reduce, RunEffect, Subscribers) {
        super();
        this.State = State;
        this.Reduce = Reduce;
        this.RunEffect = RunEffect;
        this.Subscribers = Subscribers;
    }
}

export function Store$2_$reflection(gen0, gen1) {
    return record_type("PointerMk18.Ui.Store.Store`2", [gen0, gen1], Store$2, () => [["State", gen0], ["Reduce", lambda_type(gen1, lambda_type(gen0, tuple_type(gen0, list_type(Effect_$reflection()))))], ["RunEffect", lambda_type(Store$2_$reflection(gen0, gen1), lambda_type(Effect_$reflection(), unit_type))], ["Subscribers", list_type(lambda_type(unit_type, unit_type))]]);
}

export function create(reduce, runEffect, init) {
    return new Store$2(init, reduce, runEffect, empty());
}

export function dispatch(store, message) {
    const patternInput = store.Reduce(message, store.State);
    store.State = patternInput[0];
    const enumerator = getEnumerator(patternInput[1]);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            store.RunEffect(store, enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]());
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    const enumerator_1 = getEnumerator(store.Subscribers);
    try {
        while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
            enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]()();
        }
    }
    finally {
        disposeSafe(enumerator_1);
    }
}

export function subscribe(store, fn) {
    store.Subscribers = cons(fn, store.Subscribers);
}

