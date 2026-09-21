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
