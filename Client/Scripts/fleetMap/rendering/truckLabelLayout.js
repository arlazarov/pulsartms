import { sceneMetrics as metrics } from './sceneMetrics.js';
import { markerProjection } from './markerProjection.js';

const offsets = Object.freeze([
  [0, -metrics.truckLabelOffset],
  [0, metrics.truckLabelOffset],
  [-60, 0],
  [60, 0],
  [-60, -45],
  [60, -45],
  [-60, 45],
  [60, 45],
  [0, -75],
  [0, 75],
]);

// Conservative Arial glyph bounds, including padding and a collision gap.
function labelWidth(text, padding) {
  return [...text].reduce(
    (sum, char) => sum + metrics.truckLabelSize * (/\d/.test(char) ? 0.65 : 1),
    padding * 2 + 4,
  );
}

function space() {
  const occupied = new Map();
  const cells = ([x, y, hw, hh]) => {
    const keys = [];
    for (let cx = Math.floor((x - hw) / 128); cx <= (x + hw) / 128; cx++)
      for (let cy = Math.floor((y - hh) / 128); cy <= (y + hh) / 128; cy++)
        keys.push(`${cx}:${cy}`);
    return keys;
  };
  return {
    reserve(box) {
      for (const key of cells(box)) {
        if (!occupied.has(key)) occupied.set(key, []);
        occupied.get(key).push(box);
      }
    },
    overlap(box) {
      const [x, y, hw, hh] = box;
      const nearby = new Set(
        cells(box).flatMap(key => occupied.get(key) ?? []),
      );
      return [...nearby].reduce(
        (sum, [ox, oy, ohw, ohh]) =>
          sum +
          Math.max(0, hw + ohw - Math.abs(x - ox)) *
            Math.max(0, hh + ohh - Math.abs(y - oy)),
        0,
      );
    },
  };
}

/**
 * Where every label on the map goes, in one pass.
 *
 * Only truck labels move. Stops and cluster badges mark real places and stay
 * on them, so they are obstacles: a truck label that would cover a stop, or
 * the count of trucks gathered nearby, steps aside instead.
 */
export function layoutMapLabels({
  vehicles = [],
  clusters = [],
  stops = [],
  zoom,
  previous = [],
}) {
  const project = markerProjection(zoom);
  const area = space();
  const retained = new Map(previous.map(truck => [truck.unit, truck]));
  const points = new Map(
    [...vehicles, ...clusters].map(item => [item, project(item.position)]),
  );
  // Markers stay where they are; only what is written beside them moves.
  for (const point of points.values())
    area.reserve([...point, metrics.truckSize / 2, metrics.truckSize / 2]);
  for (const stop of stops) {
    const [x, y] = project(stop.position);
    area.reserve([
      x + (stop.markerOffsetX ?? 0),
      y + (stop.markerOffsetY ?? 0),
      metrics.stopBadgeDiameter / 2,
      metrics.stopBadgeDiameter / 2,
    ]);
  }

  const place = (item, text, padding, old) => {
    const [x, y] = points.get(item);
    const halfWidth = labelWidth(text, padding[0]) / 2;
    const halfHeight = metrics.truckLabelSize / 2 + padding[1] + 3;
    const box = ([dx, dy]) => [x + dx, y + dy, halfWidth, halfHeight];
    const candidates = old ? [old, ...offsets] : offsets;
    let chosen = candidates[0],
      best = Infinity;
    for (const candidate of candidates) {
      const overlap = area.overlap(box(candidate));
      if (overlap < best) {
        chosen = candidate;
        best = overlap;
      }
      if (best === 0) break;
    }
    area.reserve(box(chosen));
    return chosen;
  };

  // The truck a dispatcher is looking at is placed first and therefore keeps
  // the plain position above its marker; aggregates settle around it.
  const ordered = [...vehicles].sort(
    (a, b) =>
      Number(!!b.selected) - Number(!!a.selected) ||
      a.unit.localeCompare(b.unit),
  );
  const selected = ordered.filter(truck => truck.selected);
  const rest = ordered.filter(truck => !truck.selected);
  const placed = new Map();
  for (const truck of selected)
    placed.set(truck, {
      ...truck,
      labelOffset: place(
        truck,
        truck.unit,
        metrics.truckLabelPadding,
        retained.get(truck.unit)?.labelOffset,
      ),
    });
  for (const truck of rest)
    placed.set(truck, {
      ...truck,
      labelOffset: place(
        truck,
        truck.unit,
        metrics.truckLabelPadding,
        retained.get(truck.unit)?.labelOffset,
      ),
    });
  return { vehicles: vehicles.map(truck => placed.get(truck)), clusters };
}

export function clusterText(cluster) {
  return `${cluster.count} trucks`;
}
