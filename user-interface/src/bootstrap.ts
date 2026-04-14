import "./styles.css";
import { mountUserInterface } from "./main";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app");

void mountUserInterface(root);
