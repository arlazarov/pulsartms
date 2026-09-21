import type { MapPoint } from '../contracts.d.ts';

// A point the map can use, or nothing: what comes over the wire may be null,
// a string, or a number outside the world.
export function coordinates(
  latitude: unknown,
  longitude: unknown,
): MapPoint | null {
  if (latitude == null || longitude == null) return null;
  const lat = Number(latitude);
  const lng = Number(longitude);
  return Number.isFinite(lat) &&
    Math.abs(lat) <= 90 &&
    Number.isFinite(lng) &&
    Math.abs(lng) <= 180
    ? { lat, lng }
    : null;
}
