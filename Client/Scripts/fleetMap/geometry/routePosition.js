// @ts-check
import { segmentFraction, segmentDistanceSquared } from './segmentProjection.js';

// Search bounds are segment-end indices. Distance uses latitude-scaled degrees.
/**
 * @param {import('../contracts.d.ts').RoutePoint} position
 * @param {import('../contracts.d.ts').MapPoint[]} path
 * @param {number[]} cumulative
 * @param {number} start
 * @param {number} end
 */
export function routePosition(position, path, cumulative, start, end) {
  const scale = Math.cos(position.latitude * Math.PI / 180);
  let best = Infinity;
  /** @type {number | null} */ let segment = null;
  /** @type {number | null} */ let miles = null;
  for (let i = start; i < end; i++) {
    const a = path[i - 1], b = path[i];
    const dx = (b.lng - a.lng) * scale, dy = b.lat - a.lat;
    const x = (position.longitude - a.lng) * scale, y = position.latitude - a.lat;
    const t = segmentFraction(x, y, dx, dy);
    const distance = segmentDistanceSquared(x, y, dx, dy, t);
    if (distance < best) {
      best = distance;
      segment = i;
      miles = cumulative[i - 1] + t * (cumulative[i] - cumulative[i - 1]);
    }
  }
  return miles !== null && best < (2 / 69) ** 2 ? { segment, miles } : null;
}
