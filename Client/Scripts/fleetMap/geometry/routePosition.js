import { segmentFraction, segmentDistanceSquared } from './segmentProjection.js';

// Search bounds are segment-end indices. Distance uses latitude-scaled degrees.
export function routePosition(position, path, cumulative, start, end) {
  const scale = Math.cos(position.latitude * Math.PI / 180);
  let best = Infinity, segment = null, miles = null;
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
