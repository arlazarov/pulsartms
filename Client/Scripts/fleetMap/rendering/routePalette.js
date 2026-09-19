// @ts-check

/** @typedef {readonly [number, number, number, number]} RouteColor */

/** @type {RouteColor} */
export const currentRouteColor = Object.freeze([40, 76, 220, 255]);

/** @type {RouteColor} */
export const currentRouteLineColor = Object.freeze([0, 106, 235, 255]);

// Fixed map series mirror the named UI palette.
const palette = Object.freeze({
  violet700: rgba(124, 58, 237),
  teal700: rgba(32, 122, 99),
  rose700: rgba(159, 52, 80),
  amber700: rgba(176, 80, 9),
  brown700: rgba(128, 96, 50),
});
const futureColors = Object.freeze(Object.values(palette));

/** @param {number} red @param {number} green @param {number} blue @returns {RouteColor} */
function rgba(red, green, blue) {
  return Object.freeze([red, green, blue, 255]);
}

/** @param {number} index @returns {RouteColor} */
export function futureRouteColor(index) {
  const position = Number.isFinite(index) ? Math.max(0, Math.trunc(index)) : 0;
  return futureColors[position % futureColors.length];
}
