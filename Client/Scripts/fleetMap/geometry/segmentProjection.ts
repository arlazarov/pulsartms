// Inputs must share one planar coordinate system; callers own geographic scaling.
export function segmentFraction(
  x: number,
  y: number,
  dx: number,
  dy: number,
): number {
  const length = dx * dx + dy * dy;
  return length ? Math.max(0, Math.min(1, (x * dx + y * dy) / length)) : 0;
}

export function segmentDistanceSquared(
  x: number,
  y: number,
  dx: number,
  dy: number,
  fraction: number,
): number {
  return (x - fraction * dx) ** 2 + (y - fraction * dy) ** 2;
}
