import { coordinates } from '../geometry/coordinates.ts';

// One reported position of a truck: where it was, when, how fast and which
// way it was pointing.
export type TruckPoint = {
  latitude: number;
  longitude: number;
  gpsTime: number;
  speed: number;
  heading: number;
};

export function normalizeHeading(value: number): number {
  return ((value % 360) + 360) % 360;
}

export function headingDifference(from: number, to: number): number {
  return ((normalizeHeading(to) - normalizeHeading(from) + 540) % 360) - 180;
}

export function createTruckPoint(
  value:
    | {
        latitude?: unknown;
        longitude?: unknown;
        updatedAt?: string;
        speed?: unknown;
        heading?: unknown;
      }
    | null
    | undefined,
): TruckPoint | null {
  const position = coordinates(value?.latitude, value?.longitude);
  const gpsTime = Date.parse(value?.updatedAt ?? '');
  if (!position || !Number.isFinite(gpsTime)) return null;

  return {
    latitude: position.lat,
    longitude: position.lng,
    gpsTime,
    speed: Number.isFinite(Number(value?.speed))
      ? Math.max(0, Number(value?.speed))
      : 0,
    heading: Number.isFinite(Number(value?.heading))
      ? normalizeHeading(Number(value?.heading))
      : 0,
  };
}

export function mergeTruckPoints(
  existing: TruckPoint[],
  incoming: TruckPoint[],
  current: TruckPoint | null,
  renderTime: number,
): TruckPoint[] {
  const points = new Map(existing.map(point => [point.gpsTime, point]));
  for (const point of incoming) points.set(point.gpsTime, point);
  if (current) points.set(current.gpsTime, current);
  const sorted = [...points.values()].sort((a, b) => a.gpsTime - b.gpsTime);
  let first = 0;
  while (
    first < sorted.length - 2 &&
    sorted[first + 1].gpsTime < renderTime - 60000
  )
    first++;
  return sorted.slice(Math.max(first, sorted.length - 300));
}
