import { kbdHint, elText, el } from "./Dom.fs.js";
import { Message } from "../core/Editor/Editor.fs.js";

const modKey = (() => {
    const platform = navigator.platform.toLowerCase();
    return (platform.indexOf("mac") >= 0) ? "⌘" : "Ctrl";
})();

function dropdownItem(label, shortcut) {
    const btn = el("button", "topbar-dropdown-item");
    btn.appendChild(elText("span", "", label));
    if (shortcut == null) {
    }
    else {
        const keys = shortcut;
        btn.appendChild(kbdHint(keys));
    }
    return btn;
}

export function render(dispatch, onSave, onLoad) {
    const topbar = el("div", "topbar");
    topbar.appendChild(elText("span", "topbar-logo", "pointer mk18"));
    const fileMenu = el("div", "topbar-menu");
    const fileBtn = elText("button", "topbar-button", "File");
    const fileDropdown = el("div", "topbar-dropdown");
    fileDropdown.style.display = "none";
    const saveBtn = dropdownItem("Save", modKey + "S");
    saveBtn.addEventListener("click", (_arg) => {
        fileDropdown.style.display = "none";
        onSave();
    });
    const loadBtn = dropdownItem("Load", modKey + "O");
    loadBtn.addEventListener("click", (_arg_1) => {
        fileDropdown.style.display = "none";
        onLoad();
    });
    const clearBtn = dropdownItem("Clear", undefined);
    clearBtn.addEventListener("click", (_arg_2) => {
        fileDropdown.style.display = "none";
        dispatch(new Message(47, []));
    });
    fileBtn.addEventListener("click", (e) => {
        e.stopPropagation();
        fileDropdown.style.display = ((fileDropdown.style.display === "none") ? "flex" : "none");
    });
    document.addEventListener("click", (_arg_3) => {
        fileDropdown.style.display = "none";
    });
    fileDropdown.appendChild(saveBtn);
    fileDropdown.appendChild(loadBtn);
    fileDropdown.appendChild(clearBtn);
    fileMenu.appendChild(fileBtn);
    fileMenu.appendChild(fileDropdown);
    topbar.appendChild(fileMenu);
    topbar.appendChild(el("span", "topbar-spacer"));
    return topbar;
}

