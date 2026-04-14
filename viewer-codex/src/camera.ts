import { add3, clamp, cross3, dot3, norm3, scale3, sub3, type Vec3 } from "./math";

export const HALF_FOV = 0.3927;

export interface CameraState {
  azimuth: number;
  elevation: number;
  distance: number;
  target: Vec3;
}

export function cameraEye(camera: CameraState): Vec3 {
  return [
    camera.target[0] + camera.distance * Math.cos(camera.elevation) * Math.cos(camera.azimuth),
    camera.target[1] + camera.distance * Math.cos(camera.elevation) * Math.sin(camera.azimuth),
    camera.target[2] + camera.distance * Math.sin(camera.elevation),
  ];
}

export function viewBasis(camera: CameraState): { eye: Vec3; forward: Vec3; right: Vec3; up: Vec3 } {
  const eye = cameraEye(camera);
  const forward = norm3(sub3(camera.target, eye));
  const right = norm3(cross3(forward, [0, 0, 1]));
  const up = norm3(cross3(right, forward));
  return { eye, forward, right, up };
}

export function orbit(camera: CameraState, dx: number, dy: number): void {
  camera.azimuth -= dx * 0.01;
  camera.elevation = clamp(camera.elevation + dy * 0.01, -1.4, 1.4);
}

export function pan(camera: CameraState, dx: number, dy: number, height: number): void {
  const { right, up } = viewBasis(camera);
  const worldPerPx = (2 * camera.distance * Math.tan(HALF_FOV)) / Math.max(height, 1);
  camera.target = add3(
    add3(camera.target, scale3(right, -dx * worldPerPx)),
    scale3(up, dy * worldPerPx),
  );
}

export function zoom(camera: CameraState, deltaY: number): void {
  const next = camera.distance * Math.exp(deltaY * 0.0012);
  camera.distance = clamp(next, 6, 400);
}

export function screenToRay(
  width: number,
  height: number,
  camera: CameraState,
  x: number,
  y: number,
): { origin: Vec3; direction: Vec3 } {
  const ndcX = (x / Math.max(width, 1)) * 2 - 1;
  const ndcY = 1 - (y / Math.max(height, 1)) * 2;
  const aspect = width / Math.max(height, 1);
  const tanHalf = Math.tan(HALF_FOV);
  const { eye, forward, right, up } = viewBasis(camera);
  const direction = norm3(add3(add3(forward, scale3(right, ndcX * aspect * tanHalf)), scale3(up, ndcY * tanHalf)));
  return { origin: eye, direction };
}

export function rayPlaneIntersection(
  ray: { origin: Vec3; direction: Vec3 },
  origin: Vec3,
  xAxis: Vec3,
  yAxis: Vec3,
): [number, number] | null {
  const normal = norm3(cross3(xAxis, yAxis));
  const denom = dot3(ray.direction, normal);
  if (Math.abs(denom) < 1e-6) return null;
  const t = dot3(sub3(origin, ray.origin), normal) / denom;
  if (t <= 0) return null;
  const hit = add3(ray.origin, scale3(ray.direction, t));
  const local = sub3(hit, origin);
  return [dot3(local, xAxis), dot3(local, yAxis)];
}
