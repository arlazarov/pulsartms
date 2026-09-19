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

export function layoutTruckLabels(vehicles, zoom, previous = []) {
  const project = markerProjection(zoom);
  const retained = new Map(previous.map(truck => [truck.unit, truck]));
  const occupied = new Map();
  const cells = box => {
    const [x, y, hw, hh] = box;
    const keys = [];
    for (let cx = Math.floor((x - hw) / 128); cx <= (x + hw) / 128; cx++)
      for (let cy = Math.floor((y - hh) / 128); cy <= (y + hh) / 128; cy++)
        keys.push(`${cx}:${cy}`);
    return keys;
  };
  const reserve = box => {
    for (const key of cells(box)) {
      if (!occupied.has(key)) occupied.set(key, []);
      occupied.get(key).push(box);
    }
  };
  const points = new Map(
    vehicles.map(truck => [truck, project(truck.position)]),
  );
  for (const point of points.values())
    reserve([...point, metrics.truckSize / 2, metrics.truckSize / 2]);
  const result = new Map();
  for (const truck of [...vehicles].sort(
    (a, b) =>
      Number(!!b.selected) - Number(!!a.selected) ||
      a.unit.localeCompare(b.unit),
  )) {
    const [x, y] = points.get(truck);
    // Conservative Arial glyph bounds, including padding and a collision gap.
    const width = [...truck.unit].reduce(
      (sum, char) =>
        sum + metrics.truckLabelSize * (/\d/.test(char) ? 0.65 : 1),
      metrics.truckLabelPadding[0] * 2 + 4,
    );
    const halfHeight =
      metrics.truckLabelSize / 2 + metrics.truckLabelPadding[1] + 3;
    const box = ([dx, dy]) => [x + dx, y + dy, width / 2, halfHeight];
    const score = offset => {
      const candidate = box(offset);
      const nearby = new Set(
        cells(candidate).flatMap(key => occupied.get(key) ?? []),
      );
      return [...nearby].reduce(
        (sum, [ox, oy, hw, hh]) =>
          sum +
          Math.max(0, width / 2 + hw - Math.abs(candidate[0] - ox)) *
            Math.max(0, halfHeight + hh - Math.abs(candidate[1] - oy)),
        0,
      );
    };
    const old = retained.get(truck.unit)?.labelOffset;
    const candidates = old ? [old, ...offsets] : offsets;
    let labelOffset = candidates[0],
      best = Infinity;
    for (const candidate of candidates) {
      const overlap = score(candidate);
      if (overlap < best) {
        labelOffset = candidate;
        best = overlap;
      }
      if (best === 0) break;
    }
    reserve(box(labelOffset));
    result.set(truck, { ...truck, labelOffset });
  }
  return vehicles.map(truck => result.get(truck));
}
