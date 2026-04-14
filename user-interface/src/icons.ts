// ---------------------------------------------------------------------------
// Carbon icon rendering + action kind → icon mapping
// ---------------------------------------------------------------------------

import ZAxis from "@carbon/icons/es/z-axis/16";
import CircleOutline from "@carbon/icons/es/circle--outline/16";
import Db2Database from "@carbon/icons/es/db2--database/16";
import Cube from "@carbon/icons/es/cube/16";
import SquareOutline from "@carbon/icons/es/square--outline/16";
import Pen from "@carbon/icons/es/pen/16";
import ShapeUnite from "@carbon/icons/es/shape--unite/16";
import MoveIcon from "@carbon/icons/es/move/16";
import RotateIcon from "@carbon/icons/es/rotate/16";
import JoinFull from "@carbon/icons/es/join--full/16";
import JoinInner from "@carbon/icons/es/join--inner/16";
import JoinLeft from "@carbon/icons/es/join--left/16";
import ContainerImagePull from "@carbon/icons/es/container-image--pull/16";
import CircleDash from "@carbon/icons/es/circle-dash/16";
import TriangleOutline from "@carbon/icons/es/triangle--outline/16";
import Layers from "@carbon/icons/es/layers/16";

import type { ActionKind } from "./api";

type CarbonIcon = typeof Layers;

const kindIcons: Record<string, CarbonIcon> = {
  Origin: ZAxis,
  Cylinder: Db2Database,
  Sphere: CircleOutline,
  Box: Cube,
  HalfPlane: SquareOutline,
  Translate: MoveIcon,
  Rotate: RotateIcon,
  Move: MoveIcon,
  Union: JoinFull,
  Intersect: JoinInner,
  Subtract: JoinLeft,
  Sketch: Pen,
  FromSketch: ShapeUnite,
  Thicken: ContainerImagePull,
  Shell: CircleDash,
  Mesh: TriangleOutline,
};

function buildSvgNode(desc: CarbonIcon): SVGElement {
  const el = document.createElementNS("http://www.w3.org/2000/svg", desc.elem);
  if (desc.attrs) {
    for (const [k, v] of Object.entries(desc.attrs)) {
      el.setAttribute(k, String(v));
    }
  }
  if (desc.content) {
    for (const child of desc.content) {
      el.appendChild(buildSvgNode(child));
    }
  }
  return el;
}

function buildSvg(desc: CarbonIcon): SVGElement {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  for (const [k, v] of Object.entries(desc.attrs)) {
    svg.setAttribute(k, String(v));
  }
  for (const child of desc.content) {
    svg.appendChild(buildSvgNode(child));
  }
  return svg;
}

export function renderIconForKind(kindCase: string): SVGElement {
  return buildSvg(kindIcons[kindCase] ?? Layers);
}

export function renderIcon(kind: ActionKind): SVGElement {
  return buildSvg(kindIcons[kind.case] ?? Layers);
}
