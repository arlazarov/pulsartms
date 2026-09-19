import { sceneMetrics as metrics } from './sceneMetrics.js';
import { markerProjection } from './markerProjection.js';

export function clusterTrucks(vehicles, zoom) {
  if (!Number.isFinite(zoom) || zoom >= metrics.truckClusterMaxZoom)
    return { vehicles, clusters: [] };
  const radius = metrics.truckClusterRadius;
  const project = markerProjection(zoom);
  const buckets = new Map(),
    groups = [],
    separate = [];
  for (const truck of [...vehicles].sort((a, b) =>
    a.unit.localeCompare(b.unit),
  )) {
    if (truck.selected) {
      separate.push(truck);
      continue;
    }
    const [x, y] = project(truck.position),
      bx = Math.floor(x / radius),
      by = Math.floor(y / radius);
    let group,
      nearest = radius;
    for (let dx = -1; dx <= 1; dx++)
      for (let dy = -1; dy <= 1; dy++)
        for (const candidate of buckets.get(`${bx + dx}:${by + dy}`) ?? []) {
          const distance = Math.hypot(x - candidate.x, y - candidate.y);
          if (distance < nearest) {
            group = candidate;
            nearest = distance;
          }
        }
    if (group) group.members.push(truck);
    else {
      group = { x, y, members: [truck] };
      groups.push(group);
      const key = `${bx}:${by}`;
      if (!buckets.has(key)) buckets.set(key, []);
      buckets.get(key).push(group);
    }
  }
  const clusters = [];
  for (const group of groups) {
    if (group.members.length === 1) {
      separate.push(group.members[0]);
      continue;
    }
    clusters.push({
      members: group.members,
      count: group.members.length,
      pixelOffset: [0, 0],
      position: [0, 1].map(
        axis =>
          group.members.reduce((sum, truck) => sum + truck.position[axis], 0) /
          group.members.length,
      ),
    });
  }
  return { vehicles: separate, clusters };
}

export function clusterExpansionZoom(members, zoom) {
  for (
    let target = Math.floor(zoom) + 1;
    target < metrics.truckClusterMaxZoom;
    target++
  )
    if (clusterTrucks(members, target).clusters.length === 0) return target;
  return metrics.truckClusterMaxZoom;
}

export function clusterCamera(members, width, height, padding = 70) {
  if (!members.length || !(width > 0 && height > 0)) return null;
  const inset =
    typeof padding === 'number'
      ? { left: padding, right: padding, top: padding, bottom: padding }
      : padding;
  const availableWidth = Math.max(1, width - inset.left - inset.right);
  const availableHeight = Math.max(1, height - inset.top - inset.bottom);
  const points = members.map(truck => markerProjection(0)(truck.position));
  const xs = points.map(point => point[0]);
  const ys = points.map(point => point[1]);
  const west = Math.min(...xs),
    east = Math.max(...xs);
  const north = Math.min(...ys),
    south = Math.max(...ys);
  const zoom = Math.max(
    0,
    Math.min(
      18,
      Math.log2(availableWidth / Math.max(east - west, 0.000001)),
      Math.log2(availableHeight / Math.max(south - north, 0.000001)),
    ),
  );
  const scale = 2 ** zoom;
  const x = (west + east) / 2 + (inset.right - inset.left) / (2 * scale);
  const y = (north + south) / 2 + (inset.bottom - inset.top) / (2 * scale);
  return {
    center: {
      lng: (x / 256) * 360 - 180,
      lat:
        (Math.atan(Math.sinh(Math.PI * (1 - (2 * y) / 256))) * 180) / Math.PI,
    },
    zoom,
  };
}
