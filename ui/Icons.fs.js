import $00316 from "@carbon/icons/es/z-axis/16";
import $00316_1 from "@carbon/icons/es/circle--outline/16";
import $00316_2 from "@carbon/icons/es/db2--database/16";
import $00316_3 from "@carbon/icons/es/cube/16";
import $00316_4 from "@carbon/icons/es/square--outline/16";
import $00316_5 from "@carbon/icons/es/pen/16";
import $00316_6 from "@carbon/icons/es/shape--unite/16";
import $00316_7 from "@carbon/icons/es/move/16";
import $00316_8 from "@carbon/icons/es/rotate/16";
import $00316_9 from "@carbon/icons/es/join--full/16";
import $00316_10 from "@carbon/icons/es/join--inner/16";
import $00316_11 from "@carbon/icons/es/join--left/16";
import $00316_12 from "@carbon/icons/es/container-image--pull/16";
import $00316_13 from "@carbon/icons/es/circle-dash/16";
import $00316_14 from "@carbon/icons/es/triangle--outline/16";
import $00316_15 from "@carbon/icons/es/layers/16";
import { item } from "./fable_modules/fable-library-js.4.29.0/Array.js";
import { toString } from "./fable_modules/fable-library-js.4.29.0/Types.js";

const svgNs = "http://www.w3.org/2000/svg";

export const zAxis = $00316;

export const circleOutline = $00316_1;

export const db2Database = $00316_2;

export const cube = $00316_3;

export const squareOutline = $00316_4;

export const pen = $00316_5;

export const shapeUnite = $00316_6;

export const moveIcon = $00316_7;

export const rotateIcon = $00316_8;

export const joinFull = $00316_9;

export const joinInner = $00316_10;

export const joinLeft = $00316_11;

export const containerImage = $00316_12;

export const circleDash = $00316_13;

export const triangleOutline = $00316_14;

export const layers = $00316_15;

function buildSvgNode(desc) {
    const node = document.createElementNS(svgNs, desc.elem);
    const attrs = desc.attrs;
    if (!(attrs == null)) {
        const arr = Object.entries(attrs);
        for (let idx = 0; idx <= (arr.length - 1); idx++) {
            const forLoopVar = item(idx, arr);
            node.setAttribute(forLoopVar[0], toString(forLoopVar[1]));
        }
    }
    const content = desc.content;
    if (!(content == null)) {
        const arr_1 = content;
        for (let idx_1 = 0; idx_1 <= (arr_1.length - 1); idx_1++) {
            const child = item(idx_1, arr_1);
            node.appendChild(buildSvgNode(child));
        }
    }
    return node;
}

function buildSvg(desc) {
    const svg = document.createElementNS(svgNs, "svg");
    const arr = Object.entries(desc.attrs);
    for (let idx = 0; idx <= (arr.length - 1); idx++) {
        const forLoopVar = item(idx, arr);
        svg.setAttribute(forLoopVar[0], toString(forLoopVar[1]));
    }
    const arr_1 = desc.content;
    for (let idx_1 = 0; idx_1 <= (arr_1.length - 1); idx_1++) {
        const child = item(idx_1, arr_1);
        svg.appendChild(buildSvgNode(child));
    }
    return svg;
}

function descriptorFor(kind) {
    switch (kind.tag) {
        case 1:
            return db2Database;
        case 2:
            return circleOutline;
        case 3:
            return cube;
        case 4:
            return squareOutline;
        case 5:
            return moveIcon;
        case 6:
            return rotateIcon;
        case 7:
            return moveIcon;
        case 8:
            return joinFull;
        case 10:
            return joinInner;
        case 9:
            return joinLeft;
        case 11:
            return pen;
        case 12:
            return shapeUnite;
        case 13:
            return containerImage;
        case 14:
            return circleDash;
        case 15:
            return triangleOutline;
        default:
            return zAxis;
    }
}

function descriptorForTemplate(t) {
    switch (t.tag) {
        case 1:
            return db2Database;
        case 2:
            return cube;
        case 3:
            return squareOutline;
        case 4:
            return moveIcon;
        case 5:
            return rotateIcon;
        case 6:
            return moveIcon;
        case 7:
            return joinFull;
        case 8:
            return joinLeft;
        case 9:
            return joinInner;
        case 10:
            return pen;
        case 11:
            return shapeUnite;
        case 12:
            return containerImage;
        case 13:
            return circleDash;
        case 14:
            return triangleOutline;
        default:
            return circleOutline;
    }
}

export function forKind(kind) {
    return buildSvg(descriptorFor(kind));
}

export function forTemplate(t) {
    return buildSvg(descriptorForTemplate(t));
}

export function fallback() {
    return buildSvg(layers);
}

function descriptorForKindName(name) {
    switch (name) {
        case "Origin":
            return zAxis;
        case "Cylinder":
            return db2Database;
        case "Sphere":
            return circleOutline;
        case "Box":
            return cube;
        case "HalfPlane":
            return squareOutline;
        case "Translate":
            return moveIcon;
        case "Rotate":
            return rotateIcon;
        case "Move":
            return moveIcon;
        case "Union":
            return joinFull;
        case "Intersect":
            return joinInner;
        case "Subtract":
            return joinLeft;
        case "Sketch":
            return pen;
        case "FromSketch":
            return shapeUnite;
        case "Thicken":
            return containerImage;
        case "Shell":
            return circleDash;
        case "Mesh":
            return triangleOutline;
        default:
            return layers;
    }
}

export function forKindName(name) {
    return buildSvg(descriptorForKindName(name));
}

