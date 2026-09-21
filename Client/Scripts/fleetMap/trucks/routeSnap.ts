import type { MapPoint } from '../contracts.d.ts';
import { segmentRange } from '../geometry/routeSearch.ts';
import type { TruckPoint } from './truckPoints.ts';
import { headingDifference } from './truckPoints.ts';

// Visual correction only. Never feed these coordinates back into GPS or ETA.
// How far the drawn truck is nudged onto the road it is driving, as an
// offset in degrees. Visual only: nothing reads it back.
export type RouteNudge = { latitude: number; longitude: number };

export function matchRoute(
  position: TruckPoint | null,
  path: MapPoint[],
  cumulative: number[],
  progress: number,
): RouteNudge | null {
  if (
    !position ||
    position.speed < 5 ||
    !Number.isFinite(position.heading) ||
    !Number.isFinite(progress)
  )
    return null;
  const scale = Math.cos((position.latitude * Math.PI) / 180);
  const [start, end] = segmentRange(cumulative, progress - 5, progress + 5);
  let best = 40 * 40,
    result = null;
  for (let i = start; i < end; i++) {
    const a = path[i - 1],
      b = path[i];
    const dx = (b.lng - a.lng) * scale * 111320,
      dy = (b.lat - a.lat) * 111320;
    const length = dx * dx + dy * dy;
    if (
      !length ||
      Math.abs(
        headingDifference(
          position.heading,
          (Math.atan2(dx, dy) * 180) / Math.PI,
        ),
      ) > 35
    )
      continue;
    const x = (position.longitude - a.lng) * scale * 111320,
      y = (position.latitude - a.lat) * 111320;
    const t = (x * dx + y * dy) / length;
    if (t < 0 || t > 1) continue;
    const distance = (x - t * dx) ** 2 + (y - t * dy) ** 2;
    if (distance >= best) continue;
    best = distance;
    // Fade out near the boundary instead of jumping between GPS and route.
    const weight = Math.min(1, (40 - Math.sqrt(distance)) / 15);
    result = {
      latitude: (a.lat + t * (b.lat - a.lat) - position.latitude) * weight,
      longitude: (a.lng + t * (b.lng - a.lng) - position.longitude) * weight,
    };
  }
  return result;
}

export function createRouteSnapper() {
  let key: string | null = null,
    lastMatch = -Infinity,
    lastFrame: number | null = null;
  let target: RouteNudge | null = null,
    latitude = 0,
    longitude = 0;
  return (
    position: TruckPoint,
    routeKey: string | null,
    match: () => RouteNudge | null,
    now: number,
  ) => {
    if (key !== routeKey) {
      key = routeKey;
      lastMatch = -Infinity;
      lastFrame = now;
      target = null;
      latitude = 0;
      longitude = 0;
    }
    if (now - lastMatch >= 100) {
      target = match();
      lastMatch = now;
    }
    const amount =
      1 - Math.exp(-Math.min(100, Math.max(0, now - (lastFrame ?? now))) / 200);
    lastFrame = now;
    latitude += ((target?.latitude ?? 0) - latitude) * amount;
    longitude += ((target?.longitude ?? 0) - longitude) * amount;
    return {
      ...position,
      latitude: position.latitude + latitude,
      longitude: position.longitude + longitude,
    };
  };
}
