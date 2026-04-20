import { elText, el } from "./Dom.fs.js";
import { render as render_1 } from "./TopBar.fs.js";
import { render as render_2 } from "./ActionList.fs.js";
import { render as render_3 } from "./SketchAuthoringPanel.fs.js";
import { render as render_4 } from "./ParamsPanel.fs.js";

export function render(dispatch, doc, viewerHost, onSave, onLoad) {
    const root = el("div", "ui-root");
    root.appendChild(render_1(dispatch, onSave, onLoad));
    const layout = el("div", "layout");
    layout.appendChild(render_2(dispatch, doc));
    const center = el("div", "panel panel-center");
    center.appendChild(viewerHost);
    const matchValue = render_3(dispatch, doc);
    if (matchValue == null) {
    }
    else {
        const overlay = matchValue;
        center.appendChild(overlay);
    }
    layout.appendChild(center);
    const right = el("div", "panel");
    const rightHeader = el("div", "panel-header");
    rightHeader.appendChild(elText("h2", "", "Properties"));
    right.appendChild(rightHeader);
    right.appendChild(render_4(dispatch, doc));
    layout.appendChild(right);
    root.appendChild(layout);
    return root;
}

