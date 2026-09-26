import type { StationMark } from './stationLayers.ts';

// Which stations stand along the truck's plan. Every station the company
// can buy at is on the map, and at an overview they were the map: a coat of
// price-coloured dots over the whole country, with the ones the plan might
// use lost among them. A station within reach of a road the truck is
// going to drive is drawn in full; the rest step back, and at an overview
// are not drawn at all.
//
// Reach is a distance on the ground, about twelve miles, so it is the same
// at every zoom. It is measured to the road's stretches, exactly, in
// degrees of latitude with longitude scaled by the cosine at the station.
// Each stretch is filed under the grid cells it crosses, walked along its
// own length - not the rectangle it spans, which for a long diagonal is
// area, not length - and each station asks only the cells its reach can
// touch: as many longitude cells as the scale makes it. The plan is
// thousands of points and the stations thousands more; asking each about
// each was the cost of a redraw. The filing has a budget; past it nothing
// is decided, and every station is drawn in full rather than one hidden
// for the road being too long or too broken to read.

type Road = { routeRole?: string; path?: unknown[] };
type Stretch = [number, number, number, number];

// Degrees of latitude.
const reach = 0.18;
const cell = reach;
const driven = new Set(['current', 'future', 'deadhead', 'current-empty']);
// The projection clamps the map here; so does this.
const latitudeLimit = 85;
// Cell filings, across all the roads: a coast-to-coast plan is a few
// thousand.
export const corridorBudget = 200000;

const scaleAt = (lat: number) =>
  Math.cos(
    (Math.max(-latitudeLimit, Math.min(latitudeLimit, lat)) * Math.PI) / 180,
  );
const valid = (lng: number, lat: number) =>
  Number.isFinite(lng) &&
  Number.isFinite(lat) &&
  Math.abs(lng) <= 180 &&
  Math.abs(lat) <= 90;

function distanceToStretch(
  x: number,
  y: number,
  [ax, ay, bx, by]: Stretch,
  scale: number,
): number {
  const px = x * scale,
    sx = ax * scale,
    ex = bx * scale;
  const dx = ex - sx,
    dy = by - ay;
  const length = dx * dx + dy * dy;
  const t =
    length === 0
      ? 0
      : Math.max(0, Math.min(1, ((px - sx) * dx + (y - ay) * dy) / length));
  return Math.hypot(px - (sx + t * dx), y - (ay + t * dy));
}

// The stations the plan passes nowhere near, by identity: the marks are
// the scene's own objects, and a copy would not be the station that was
// picked. With no road drawn there is nothing to be far from, and every
// station is drawn in full as before.
export function stationsFarFromRoads(
  stations: StationMark[],
  roads: Iterable<Road>,
  budget = corridorBudget,
): Set<StationMark> {
  const grid = new Map<string, Stretch[]>();
  const at = (value: number) => Math.floor(value / cell);
  let filed = 0;
  const file = (x: number, y: number, stretch: Stretch) => {
    const key = `${x},${y}`;
    const bucket = grid.get(key);
    if (bucket && bucket[bucket.length - 1] === stretch) return true;
    if (bucket) bucket.push(stretch);
    else grid.set(key, [stretch]);
    return ++filed <= budget;
  };
  for (const road of roads) {
    if (!road.path || !driven.has(road.routeRole ?? '')) continue;
    // A road's points come as the provider's {lat, lng} or as the
    // renderer's [lng, lat]. A point that is not a place breaks the road
    // there.
    const path = road.path.map(point =>
      Array.isArray(point)
        ? { lng: point[0] as number, lat: point[1] as number }
        : (point as { lat: number; lng: number }),
    );
    for (let i = 1; i < path.length; i++) {
      const a = path[i - 1],
        b = path[i];
      if (!valid(a.lng, a.lat) || !valid(b.lng, b.lat)) continue;
      const stretch: Stretch = [a.lng, a.lat, b.lng, b.lat];
      // Walked in steps of one cell along its longer axis, so every cell
      // it crosses holds it and a stretch costs its length.
      const steps = Math.max(
        1,
        Math.ceil(
          Math.max(Math.abs(b.lng - a.lng), Math.abs(b.lat - a.lat)) / cell,
        ),
      );
      for (let s = 0; s <= steps; s++) {
        const t = s / steps;
        const x = at(a.lng + (b.lng - a.lng) * t),
          y = at(a.lat + (b.lat - a.lat) * t);
        if (!file(x, y, stretch)) return new Set();
      }
    }
  }
  const far = new Set<StationMark>();
  if (grid.size === 0) return far;
  for (const station of stations) {
    const [lng, lat] = station.position;
    if (!valid(lng, lat)) continue;
    const scale = scaleAt(lat);
    // Reach in raw longitude grows as the cosine shrinks; the search
    // widens with it. A stretch filed under a cell may still run through
    // the cells beside it on the way, which the ring of one covers.
    const across = Math.ceil(1 / scale) + 1;
    const x = at(lng),
      y = at(lat);
    let near = false;
    search: for (let dx = -across; dx <= across; dx++)
      for (let dy = -2; dy <= 2; dy++)
        for (const stretch of grid.get(`${x + dx},${y + dy}`) ?? [])
          if (distanceToStretch(lng, lat, stretch, scale) <= reach) {
            near = true;
            break search;
          }
    if (!near) far.add(station);
  }
  return far;
}
