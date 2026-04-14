import { mountViewer } from "./mount";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app");

void mountViewer(root, { polling: true });
