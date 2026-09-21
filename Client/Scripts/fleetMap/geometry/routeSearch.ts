// The first index whose value reaches the target, in a list that only grows.
export function lowerBound(values: number[], target: number): number {
  let low = 0,
    high = values.length;
  while (low < high) {
    const middle = low + Math.floor((high - low) / 2);
    if (values[middle] < target) low = middle + 1;
    else high = middle;
  }
  return low;
}

// The stretch of a road between two distances along it, as a pair of
// indices into the cumulative miles.
export function segmentRange(
  cumulative: number[],
  minimum: number,
  maximum: number,
): [number, number] {
  const start = Math.max(1, lowerBound(cumulative, minimum));
  let low = 0,
    high = cumulative.length;
  while (low < high) {
    const middle = low + Math.floor((high - low) / 2);
    if (cumulative[middle] <= maximum) low = middle + 1;
    else high = middle;
  }
  return [start, Math.min(cumulative.length, low + 1)];
}
