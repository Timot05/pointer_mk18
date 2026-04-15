import "../../user-interface/src/styles.css";
import "./styles.css";
import { mountUserInterface } from "../../user-interface/src/main";
import { mountViewer } from "../../viewer-codex/src/mount";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app");

const viewerHost = document.createElement("div");
viewerHost.className = "app-viewer-host";

void mountUserInterface(root, {
  centerContent: viewerHost,
});

void mountViewer(viewerHost);
