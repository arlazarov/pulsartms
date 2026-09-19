import { sceneMetrics as metrics } from './sceneMetrics.js';

export function stopMarkerLabel(_job, number) {
  return String(number ?? '');
}

// Separate coincident badges in screen space without moving their geographic anchors.
export function layoutStopMarkers(rows) {
  const groups = new Map();
  for (const row of rows) {
    const key = row.position.map(value => value.toFixed(3)).join(',');
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(row);
    row.markerLabel = stopMarkerLabel(row.job, row.number);
    row.markerOffsetX = 0;
    row.markerOffsetY = 0 - metrics.stopBadgeOffset;
  }
  for (const group of groups.values()) {
    if (group.length < 2) continue;
    group.sort(
      (a, b) => (parseInt(a.number, 10) || 0) - (parseInt(b.number, 10) || 0),
    );
    const width = metrics.stopBadgeDiameter + metrics.stopBadgeGap;
    group.forEach((row, index) => {
      const finalOdd = index === group.length - 1 && group.length % 2;
      row.markerOffsetX = finalOdd ? 0 : ((index % 2 ? 1 : -1) * width) / 2;
      const rowIndex = Math.floor(index / 2);
      // The centered last badge forms an equilateral triangle with the pair below.
      row.markerOffsetY -= finalOdd
        ? (rowIndex - 1) * width + (Math.sqrt(3) * width) / 2
        : rowIndex * width;
    });
  }
}
