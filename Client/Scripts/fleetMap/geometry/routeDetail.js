import { segmentFraction, segmentDistanceSquared } from './segmentProjection.js';

export const fullRouteDetailZoom = 14;

export function routeDetailIndices(path, zoom, anchors = []) {
  if (path.length < 3 || zoom >= fullRouteDetailZoom) return path.map((_, i) => i);
  const latitude = path[0].lat * Math.PI / 180;
  const scale = Math.max(.087, Math.cos(latitude));
  const tolerance = Math.min(1000, Math.max(10, .6 * 156543.03 * scale / 2 ** zoom));
  const boundaries = [...new Set([0, ...anchors, path.length - 1])].filter(i => i >= 0 && i < path.length).sort((a, b) => a - b);
  const keep = new Set(boundaries);
  const pending = boundaries.slice(1).map((end, i) => [boundaries[i], end]);
  while (pending.length) {
    const [start, end] = pending.pop();
    const a = path[start], b = path[end];
    const dx = (b.lng - a.lng) * scale * 111320, dy = (b.lat - a.lat) * 111320;
    let largest = tolerance * tolerance, selected = -1;
    for (let i = start + 1; i < end; i++) {
      const x = (path[i].lng - a.lng) * scale * 111320, y = (path[i].lat - a.lat) * 111320;
      const t = segmentFraction(x, y, dx, dy);
      const distance = segmentDistanceSquared(x, y, dx, dy, t);
      if (distance > largest) { largest = distance; selected = i; }
    }
    if (selected < 0) continue;
    keep.add(selected);
    pending.push([start, selected], [selected, end]);
  }
  return [...keep].sort((a, b) => a - b);
}
