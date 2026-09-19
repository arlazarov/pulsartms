// @ts-check
/** @param {ArrayLike<number>} values @param {number} target */
export function lowerBound(values, target) {
  let low = 0, high = values.length;
  while (low < high) {
    const middle = low + Math.floor((high - low) / 2);
    if (values[middle] < target) low = middle + 1;
    else high = middle;
  }
  return low;
}

/** @param {ArrayLike<number>} cumulative @param {number} minimum @param {number} maximum */
export function segmentRange(cumulative, minimum, maximum) {
  const start = Math.max(1, lowerBound(cumulative, minimum));
  let low = 0, high = cumulative.length;
  while (low < high) {
    const middle = low + Math.floor((high - low) / 2);
    if (cumulative[middle] <= maximum) low = middle + 1;
    else high = middle;
  }
  return [start, Math.min(cumulative.length, low + 1)];
}
