import { store as store_1 } from "./AppStore.fs.js";
import { subscribe, dispatch as dispatch_1 } from "./Store.fs.js";
import { DocumentPipeline_documentView, DocumentPipeline_paletteView } from "../core/Editor/DocumentPipeline.fs.js";
import { length } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { mount as mount_1 } from "../viewer/Viewer.fs.js";
import { Message, Editor_serializedModel } from "../core/Editor/Editor.fs.js";
import { some } from "./fable_modules/fable-library-js.4.29.0/Option.js";
import { printf, toText } from "./fable_modules/fable-library-js.4.29.0/String.js";
import { render } from "./Shell.fs.js";
import { syncSlotValues } from "./ParamsPanel.fs.js";
import { syncSubtitles } from "./ActionList.fs.js";
import { sync } from "./CommandPalette.fs.js";
import { installGlobals } from "./Benchmarks.fs.js";
import { register } from "./Shortcuts.fs.js";

const store = store_1;

function dispatch(msg) {
    dispatch_1(store, msg);
}

function getPaletteState() {
    return DocumentPipeline_paletteView(store.State);
}

function getDocActionCount() {
    return length(DocumentPipeline_documentView(store.State).Actions);
}

function getPaletteOpen() {
    return getPaletteState().IsOpen;
}

function mountViewer(root) {
    return mount_1(root);
}

const viewerHost = (() => {
    const host = document.createElement("div");
    host.className = "panel-center-host";
    return host;
})();

function onSave() {
    const model = Editor_serializedModel(store.State);
    const json = JSON.stringify(model, undefined, some(2));
    let baseName;
    const trimmed = model.Name.trim().toLocaleLowerCase();
    baseName = (((trimmed === "") ? true : (trimmed === "untitled")) ? "pointer-model" : trimmed);
    const blob = new Blob([json], {
        type: "application/json",
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = toText(printf("%s.json"))(baseName);
    link.click();
    URL.revokeObjectURL(url);
}

function onLoad() {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = "application/json,.json";
    input.addEventListener("change", (_arg) => {
        const files = input.files;
        if (files.length > 0) {
            const file = files[0];
            const reader = new FileReader();
            reader.onload = ((_arg_1) => {
                const text = reader.result;
                try {
                    dispatch(new Message(46, [JSON.parse(text)]));
                }
                catch (ex) {
                    console.error(some("Failed to load: " + ex.message));
                }
            });
            reader.readAsText(file);
        }
    });
    input.click();
}

function renderInto(root) {
    const shell = render((msg) => {
        dispatch(msg);
    }, DocumentPipeline_documentView(store.State), viewerHost, () => {
        onSave();
    }, () => {
        onLoad();
    });
    root.innerHTML = "";
    root.appendChild(shell);
}

function uiSignature(state) {
    return toText(printf("%A|%A|%A|%A|%A|%A|%A|%A|%A"))(state.Doc.SelectedId)(state.SketchEditMode)(state.SketchTool)(state.SelectedTargets)(state.HoveredTarget)(state.EditingDimension)(state.ConstraintPlacementMode)(state.ConstraintPlacementDraft)(state.ConstraintPlacementCursor);
}

let lastCompiled = store.State.Compiled;

let lastSlotValues = store.State.SlotValues;

let lastUiSignature = uiSignature(store.State);

function onStateChange(root, unitVar) {
    const state = store.State;
    const compiledChanged = !(lastCompiled === state.Compiled);
    const slotValuesChanged = !(lastSlotValues === state.SlotValues);
    const nextUiSignature = uiSignature(state);
    if (compiledChanged ? true : (nextUiSignature !== lastUiSignature)) {
        renderInto(root);
    }
    else if (slotValuesChanged) {
        const doc = DocumentPipeline_documentView(state);
        syncSlotValues(root, state);
        syncSubtitles(root, doc);
    }
    sync((msg) => {
        dispatch(msg);
    }, getPaletteState, getDocActionCount);
    lastCompiled = state.Compiled;
    lastSlotValues = state.SlotValues;
    lastUiSignature = nextUiSignature;
}

function mount() {
    const root = document.getElementById("app");
    if (root == null) {
        throw new Error("Missing #app element");
    }
    installGlobals();
    subscribe(store, () => {
        onStateChange(root, undefined);
    });
    register((msg) => {
        dispatch(msg);
    }, () => DocumentPipeline_documentView(store.State), getPaletteOpen, () => {
        onSave();
    }, () => {
        onLoad();
    });
    renderInto(root);
    mountViewer(viewerHost);
}

mount();

