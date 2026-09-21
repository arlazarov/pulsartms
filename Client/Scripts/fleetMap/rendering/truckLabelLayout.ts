import type {
  LabelledCluster,
  LabelledTruck,
  MarkPoint,
} from './truckClusters.ts';
import { sceneMetrics as metrics } from './sceneMetrics.ts';
import { markerProjection } from './markerProjection.ts';

// Where a label stands relative to the mark it names, in screen pixels.
export type LabelOffset = MarkPoint;
// A box on the screen: its middle, then half its width and half its height.
type Box = [number, number, number, number];

const offsets: readonly LabelOffset[] = Object.freeze<LabelOffset[]>([
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
function labelWidth(text: string, padding: number) {
  return [...text].reduce(
    (sum, char) => sum + metrics.truckLabelSize * (/\d/.test(char) ? 0.65 : 1),
    padding * 2 + 4,
  );
}

function space() {
  const occupied = new Map<string, { box: Box; owner: unknown }[]>();
  const cells = ([x, y, hw, hh]: Box) => {
    const keys: string[] = [];
    for (let cx = Math.floor((x - hw) / 128); cx <= (x + hw) / 128; cx++)
      for (let cy = Math.floor((y - hh) / 128); cy <= (y + hh) / 128; cy++)
        keys.push(`${cx}:${cy}`);
    return keys;
  };
  return {
    reserve(box: Box, owner: unknown = null) {
      const taken = { box, owner };
      for (const key of cells(box)) {
        if (!occupied.has(key)) occupied.set(key, []);
        occupied.get(key)!.push(taken);
      }
    },
    // A label sits beside the marker it names; that is where it belongs, not
    // a collision. Counting its own marker against it left every position
    // overlapping something, so no position ever scored clear, the choice
    // fell to whichever was tried first, and the label went under the truck
    // on a leader line and stayed there.
    overlap(box: Box, owner: unknown = null) {
      const [x, y, hw, hh] = box;
      const nearby = new Set(
        cells(box).flatMap(key => occupied.get(key) ?? []),
      );
      return [...nearby].reduce(
        (sum, taken) =>
          taken.owner !== null && taken.owner === owner
            ? sum
            : sum +
              Math.max(0, hw + taken.box[2] - Math.abs(x - taken.box[0])) *
                Math.max(0, hh + taken.box[3] - Math.abs(y - taken.box[1])),
        0,
      );
    },
  };
}

/**
 * Where every label on the map goes, in one pass.
 *
 * Marks stay where they are - a truck on its point, a badge on the stop's -
 * because the gap between them is how far the truck is from the stop. Only
 * what is written beside them moves, and only to get out of each other's
 * way. Stops are deliberately not obstacles: a truck parked on its own
 * delivery would send its unit number off across the map on a leader line,
 * which reads far worse than the overlap it avoids. The badges are drawn
 * over the trucks and under the labels instead.
 */
export function layoutMapLabels({
  vehicles = [],
  clusters = [],
  zoom,
  previous = [],
}: {
  vehicles?: LabelledTruck[];
  clusters?: LabelledCluster[];
  zoom: number;
  previous?: LabelledTruck[];
}) {
  const project = markerProjection(zoom);
  const area = space();
  const retained = new Map(previous.map(truck => [truck.unit, truck]));
  const points = new Map<LabelledTruck | LabelledCluster, [number, number]>(
    [...vehicles, ...clusters].map(item => {
      const [x, y] = project(item.position);
      const [dx, dy] = item.markerOffset ?? [0, 0];
      return [item, [x + dx, y + dy] as [number, number]];
    }),
  );
  // Markers stay where they are; only what is written beside them moves.
  for (const [item, point] of points)
    area.reserve(
      [...point, metrics.truckSize / 2, metrics.truckSize / 2],
      item,
    );
  const place = (
    item: LabelledTruck,
    text: string,
    padding: number[],
    old: LabelOffset | undefined,
  ) => {
    const [x, y] = points.get(item)!;
    const halfWidth = labelWidth(text, padding[0]) / 2;
    const halfHeight = metrics.truckLabelSize / 2 + padding[1] + 3;
    const box = ([dx, dy]: LabelOffset): Box => [
      x + dx,
      y + dy,
      halfWidth,
      halfHeight,
    ];
    // The place above the marker is tried first, always: a label that was
    // once pushed aside must come back when the way is clear. Where it was
    // last comes second, so that a label which still cannot have its own
    // place at least stops hopping between the alternatives.
    const candidates = old ? [offsets[0], old, ...offsets] : offsets;
    let chosen = candidates[0],
      best = Infinity;
    for (const candidate of candidates) {
      const overlap = area.overlap(box(candidate), item);
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
  const placed = new Map<LabelledTruck, LabelledTruck>();
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
  return { vehicles: vehicles.map(truck => placed.get(truck)!), clusters };
}

export function clusterText(cluster: { count: number }) {
  return `${cluster.count} trucks`;
}
