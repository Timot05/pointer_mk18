import "../../user-interface/src/styles.css";
import "./styles.css";
import { createAppEventHub } from "./events";
import { mountUserInterface } from "../../user-interface/src/main";
import { mountViewer } from "../../viewer-codex/src/mount";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app");

const viewerHost = document.createElement("div");
viewerHost.className = "app-viewer-host";
const events = createAppEventHub();

void mountUserInterface(root, {
  centerContent: viewerHost,
  onViewerStateDirty: () => events.emit("viewer-state-dirty"),
  onViewerModelDirty: () => events.emit("viewer-model-dirty"),
  subscribeDocumentDirty: (listener) => events.subscribe("document-dirty", listener),
});

void mountViewer(viewerHost, {
  polling: false,
  onDocumentDirty: () => events.emit("document-dirty"),
  subscribeViewerStateDirty: (listener) => events.subscribe("viewer-state-dirty", listener),
  subscribeViewerModelDirty: (listener) => events.subscribe("viewer-model-dirty", listener),
});
