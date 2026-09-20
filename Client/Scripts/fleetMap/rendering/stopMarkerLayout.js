import { sceneMetrics as metrics } from './sceneMetrics.js';
import { markerProjection } from './markerProjection.js';

export function stopMarkerLabel(_job, number) {
  return String(number ?? '');
}

// Separate badges that cover each other, without moving their geographic
// anchors. What matters is whether they collide on screen, not whether the
// stops share a coordinate: the end of one load and the start of the next
// are a few miles apart and still one blot at the zoom a whole run is read
// at. Grouping by coordinate left exactly those pairs stacked.
export function layoutStopMarkers(rows, zoom) {
  const project = markerProjection(zoom);
  const width = metrics.stopBadgeDiameter + metrics.stopBadgeGap;
  const groups = [];
  for (const row of rows) {
    row.markerLabel = stopMarkerLabel(row.job, row.number);
    row.markerOffsetX = 0;
    row.markerOffsetY = 0 - metrics.stopBadgeOffset;
    const point = project(row.position);
    const near = groups.find(group =>
      group.some(
        member =>
          Math.abs(member.point[0] - point[0]) < width &&
          Math.abs(member.point[1] - point[1]) < width,
      ),
    );
    if (near) near.push({ row, point });
    else groups.push([{ row, point }]);
  }
  for (const group of groups) {
    if (group.length < 2 || group.length > metrics.stopBadgeCluster) continue;
    group.sort(
      (a, b) =>
        (parseInt(a.row.number, 10) || 0) - (parseInt(b.row.number, 10) || 0),
    );
    group.forEach(({ row }, index) => {
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
