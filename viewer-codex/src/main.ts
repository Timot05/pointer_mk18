import "./styles.css";
import { ViewerApp } from "./viewer";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app");

const app = new ViewerApp(root);
void app.start();
