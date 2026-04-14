import type { ActionSketch, ArcData, RenderEntity } from "./api";
import type { Vec2 } from "./math";

function nextEntityId(sketch: ActionSketch, prefix: string): string {
  const taken = new Set(sketch.entities.map((entity) => entity.id));
  let index = 1;
  while (taken.has(`${prefix}${index}`)) index += 1;
  return `${prefix}${index}`;
}

function addPoint(sketch: ActionSketch, position: Vec2, id?: string): [ActionSketch, string] {
  const pointId = id ?? nextEntityId(sketch, "p");
  return [{
    ...sketch,
    entities: [...sketch.entities, { case: "REPoint", id: pointId, x: position[0], y: position[1] }],
  }, pointId];
}

function addLineEntity(sketch: ActionSketch, startId: string, endId: string, id?: string): ActionSketch {
  const lineId = id ?? nextEntityId(sketch, "l");
  return {
    ...sketch,
    entities: [...sketch.entities, { case: "RELine", id: lineId, startId, endId }],
  };
}

export function addLine(sketch: ActionSketch, start: Vec2, end: Vec2): ActionSketch {
  let next = sketch;
  let startId: string;
  [next, startId] = addPoint(next, start);
  let endId: string;
  [next, endId] = addPoint(next, end);
  return addLineEntity(next, startId, endId);
}

export function addRectangle(sketch: ActionSketch, a: Vec2, b: Vec2): ActionSketch {
  const [x0, y0] = a;
  const [x1, y1] = b;
  const corners: Vec2[] = [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];
  let next = sketch;
  const ids: string[] = [];
  for (const corner of corners) {
    let pointId: string;
    [next, pointId] = addPoint(next, corner);
    ids.push(pointId);
  }
  next = addLineEntity(next, ids[0], ids[1]);
  next = addLineEntity(next, ids[1], ids[2]);
  next = addLineEntity(next, ids[2], ids[3]);
  return addLineEntity(next, ids[3], ids[0]);
}

export function addCircle(sketch: ActionSketch, center: Vec2, radiusPoint: Vec2): ActionSketch {
  let next = sketch;
  let centerId: string;
  [next, centerId] = addPoint(next, center);
  const circleId = nextEntityId(next, "c");
  const radius = Math.max(1e-6, Math.hypot(radiusPoint[0] - center[0], radiusPoint[1] - center[1]));
  return {
    ...next,
    entities: [...next.entities, { case: "RECircle", id: circleId, center: centerId, radius }],
  };
}

function projectPointToCircle(center: Vec2, start: Vec2, point: Vec2): Vec2 {
  const radius = Math.max(1e-6, Math.hypot(start[0] - center[0], start[1] - center[1]));
  const dx = point[0] - center[0];
  const dy = point[1] - center[1];
  const length = Math.hypot(dx, dy);
  if (length < 1e-6) return [center[0] + radius, center[1]];
  return [center[0] + (dx / length) * radius, center[1] + (dy / length) * radius];
}

export function addArc(sketch: ActionSketch, center: Vec2, start: Vec2, end: Vec2): ActionSketch {
  let next = sketch;
  let centerId: string;
  [next, centerId] = addPoint(next, center);
  let startId: string;
  [next, startId] = addPoint(next, start);
  let endId: string;
  [next, endId] = addPoint(next, projectPointToCircle(center, start, end));
  const arcId = nextEntityId(next, "a");
  const cross = (start[0] - center[0]) * (end[1] - center[1]) - (start[1] - center[1]) * (end[0] - center[0]);
  const data: ArcData = { case: "ArcCenter", center: centerId, clockwise: cross < 0 };
  return {
    ...next,
    entities: [...next.entities, { case: "REArc", id: arcId, startId, endId, data }],
  };
}
