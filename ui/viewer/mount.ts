import viewerCss from "./styles.css?inline";
import { ViewerApp, type ViewerStartOptions } from "./viewer";

export async function mountViewer(root: HTMLElement, options: ViewerStartOptions = {}): Promise<ViewerApp> {
  const shadow = root.shadowRoot ?? root.attachShadow({ mode: "open" });
  shadow.innerHTML = "";

  const style = document.createElement("style");
  style.textContent = viewerCss;
  shadow.appendChild(style);

  const container = document.createElement("div");
  container.style.width = "100%";
  container.style.height = "100%";
  shadow.appendChild(container);

  const app = new ViewerApp(container);
  await app.start(options);
  return app;
}
