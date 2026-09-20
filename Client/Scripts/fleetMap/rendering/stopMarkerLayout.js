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
//
// A truck parked at its own stop hid the badge for it underneath itself, so
// that badge steps aside - beside the truck, because above the truck is
// where its unit number goes. Stepping aside is part of the same layout as
// everything else: done afterwards on its own, the badge stepped out from
// under the truck and straight onto the stop standing next to it. So every
// badge that would touch the truck, or touch a badge that has stepped aside
// for it, stands in one row beside the truck in stop order. The dot marking
// where each stop really is stays put, so nothing is said that is not true.
export function layoutStopMarkers(rows, zoom, trucks = []) {
  const project = markerProjection(zoom);
  const width = metrics.stopBadgeDiameter + metrics.stopBadgeGap;
  const touches = (a, b) =>
    Math.abs(a[0] - b[0]) < width && Math.abs(a[1] - b[1]) < width;
  // Parked means standing. A truck driving past a stop is over it for a
  // moment and gone, and laying the badges out again for every frame of
  // that would rebuild the map for nothing anyone could read.
  const parked = trucks
    .filter(truck => truck.position && !(truck.speed > 0))
    .map(truck => project(truck.position));
  const groups = [];
  for (const row of rows) {
    row.markerLabel = stopMarkerLabel(row.job, row.number);
    row.markerOffsetX = 0;
    row.markerOffsetY = 0 - metrics.stopBadgeOffset;
    const point = project(row.position);
    const truck = parked.find(at => touches(at, point)) ?? null;
    // Where the badge will stand once it has stepped aside, which is what
    // its neighbours have to keep clear of.
    const stands = truck ? [truck[0] + width, truck[1]] : point;
    const member = { row, point, stands, truck };
    const group = groups.find(members =>
      members.some(
        other =>
          touches(other.point, point) ||
          touches(other.stands, stands) ||
          touches(other.stands, point) ||
          touches(other.point, stands),
      ),
    );
    if (group) group.push(member);
    else groups.push([member]);
  }
  for (const group of groups) {
    const byNumber = (a, b) =>
      (parseInt(a.row.number, 10) || 0) - (parseInt(b.row.number, 10) || 0);
    const truck = group.find(member => member.truck)?.truck;
    if (truck) {
      group.sort(byNumber);
      group.forEach(({ row, point }, index) => {
        row.markerOffsetX = truck[0] + width * (index + 1) - point[0];
        row.markerOffsetY = truck[1] - point[1] - metrics.stopBadgeOffset;
      });
      continue;
    }
    if (group.length < 2 || group.length > metrics.stopBadgeCluster) continue;
    group.sort(byNumber);
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
