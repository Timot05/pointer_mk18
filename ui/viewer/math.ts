export type Vec2 = [number, number];
export type Vec3 = [number, number, number];

export function add2(a: Vec2, b: Vec2): Vec2 {
  return [a[0] + b[0], a[1] + b[1]];
}

export function sub2(a: Vec2, b: Vec2): Vec2 {
  return [a[0] - b[0], a[1] - b[1]];
}

export function scale2(v: Vec2, s: number): Vec2 {
  return [v[0] * s, v[1] * s];
}

export function dot2(a: Vec2, b: Vec2): number {
  return a[0] * b[0] + a[1] * b[1];
}

export function len2(v: Vec2): number {
  return Math.hypot(v[0], v[1]);
}

export function norm2(v: Vec2): Vec2 {
  const l = len2(v);
  return l < 1e-9 ? [0, 0] : [v[0] / l, v[1] / l];
}

export function perp(v: Vec2): Vec2 {
  return [-v[1], v[0]];
}

export function add3(a: Vec3, b: Vec3): Vec3 {
  return [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
}

export function sub3(a: Vec3, b: Vec3): Vec3 {
  return [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
}

export function scale3(v: Vec3, s: number): Vec3 {
  return [v[0] * s, v[1] * s, v[2] * s];
}

export function dot3(a: Vec3, b: Vec3): number {
  return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

export function cross3(a: Vec3, b: Vec3): Vec3 {
  return [
    a[1] * b[2] - a[2] * b[1],
    a[2] * b[0] - a[0] * b[2],
    a[0] * b[1] - a[1] * b[0],
  ];
}

export function len3(v: Vec3): number {
  return Math.hypot(v[0], v[1], v[2]);
}

export function norm3(v: Vec3): Vec3 {
  const l = len3(v);
  return l < 1e-9 ? [0, 0, 0] : [v[0] / l, v[1] / l, v[2] / l];
}

export function clamp(v: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, v));
}
