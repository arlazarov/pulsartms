// @ts-check
// Inputs must share one planar coordinate system; callers own geographic scaling.
/** @param {number} x @param {number} y @param {number} dx @param {number} dy */
export function segmentFraction(x, y, dx, dy) {
  const length = dx * dx + dy * dy;
  return length ? Math.max(0, Math.min(1, (x * dx + y * dy) / length)) : 0;
}

/** @param {number} x @param {number} y @param {number} dx @param {number} dy @param {number} fraction */
export function segmentDistanceSquared(x, y, dx, dy, fraction) {
  return (x - fraction * dx) ** 2 + (y - fraction * dy) ** 2;
}
