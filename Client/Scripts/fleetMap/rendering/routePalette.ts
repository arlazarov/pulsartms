export type RouteColor = readonly [number, number, number, number];

export const currentRouteColor: RouteColor = Object.freeze([40, 76, 220, 255]);

export const currentRouteLineColor: RouteColor = Object.freeze([
  0, 106, 235, 255,
]);

// Fixed map series mirror the named UI palette: the first three roads are
// the map-route-option roles in order, and the series runs on through the
// palette those roles are chosen from. The GPU takes numbers, not custom
// properties, so the values are written out here and held to the roles by
// tests/fleetMap/mapColors.test.js.
const palette = Object.freeze({
  violet700: rgba(124, 58, 237),
  teal700: rgba(32, 122, 99),
  rose700: rgba(159, 52, 80),
  amber700: rgba(176, 80, 9),
  brown700: rgba(128, 96, 50),
});
const futureColors = Object.freeze(Object.values(palette));

function rgba(red: number, green: number, blue: number): RouteColor {
  return Object.freeze([red, green, blue, 255]);
}

export function futureRouteColor(index: number): RouteColor {
  const position = Number.isFinite(index) ? Math.max(0, Math.trunc(index)) : 0;
  return futureColors[position % futureColors.length];
}

// One truck's plan is one road. The loads still to come are the blue of
// the road being driven, each a step lighter than the one before, so a
// chain reads as one route in order - by its numbers - and not as a handful
// of colours whose order has to be looked up, hidden under each other
// wherever the loads run on one corridor.
// The first step is the map-route-next role the map key shows; the rest
// step on from the road's own blue.
const chainColors = [
  rgba(64, 143, 240),
  ...[0.4, 0.52].map(
    white =>
      Object.freeze([
        ...currentRouteLineColor
          .slice(0, 3)
          .map(value => Math.round(value + (255 - value) * white)),
        255,
      ]) as unknown as RouteColor,
  ),
];
export function chainRouteColor(index: number): RouteColor {
  const step = Number.isFinite(index) ? Math.max(0, Math.trunc(index)) : 0;
  return chainColors[Math.min(step, chainColors.length - 1)];
}
