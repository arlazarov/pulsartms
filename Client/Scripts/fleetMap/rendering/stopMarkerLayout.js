import { sceneMetrics as metrics } from './sceneMetrics.js';
import { markerProjection } from './markerProjection.js';

export function stopMarkerLabel(_job, number) {
  return String(number ?? '');
}

export function stopClusterText(cluster) {
  return `${cluster.count} stops`;
}

// What to do with badges that cover each other, decided against the screen:
// the end of one load and the start of the next are a few miles apart and
// still one blot at the zoom a whole run is read at.
//
// Stops that the camera can pull apart by coming closer are gathered under
// one count, the way trucks are - "2 stops" says more at that zoom than two
// circles standing on each other, and a click goes in to where they part.
// Stops at the very same place never part however close the camera comes,
// so those stand side by side instead, without moving their anchors. A stop
// the dispatcher has picked is never folded away into a count.
export function layoutStopMarkers(rows, zoom, trucks = []) {
  const project = markerProjection(zoom);
  const close = markerProjection(metrics.stopClusterPartZoom);
  const width = metrics.stopBadgeDiameter + metrics.stopBadgeGap;
  const near = (a, b) =>
    Math.abs(a[0] - b[0]) < width && Math.abs(a[1] - b[1]) < width;
  const groups = [];
  for (const row of rows) {
    row.markerLabel = stopMarkerLabel(row.job, row.number);
    row.markerOffsetX = 0;
    row.markerOffsetY = 0 - metrics.stopBadgeOffset;
    const point = project(row.position);
    // Measured from the first of the group, not from any of it: a corridor
    // of stops otherwise chains into one group the length of the road.
    const group = groups.find(members => near(members[0].point, point));
    if (group) group.push({ row, point });
    else groups.push([{ row, point }]);
  }
  const clusters = [];
  const clustered = new Set();
  for (const group of groups) {
    if (group.length < 2) continue;
    const first = close(group[0].row.position);
    const onePlace = group.every(member =>
      near(first, close(member.row.position)),
    );
    if (!onePlace && !group.some(member => member.row.highlighted)) {
      const members = group.map(member => member.row);
      clusters.push({
        id: members.map(row => row.id).join('+'),
        count: members.length,
        members,
        position: [0, 1].map(
          axis =>
            members.reduce((sum, row) => sum + row.position[axis], 0) /
            members.length,
        ),
      });
      for (const row of members) clustered.add(row);
      continue;
    }
    if (group.length > metrics.stopBadgeCluster) continue;
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
  // A truck parked at its own stop hid the badge for it underneath itself.
  // The badge steps aside rather than up: above the truck is where its own
  // unit number goes, and the two were covering each other in turn. The dot
  // marking where the stop really is stays put, so nothing is said that is
  // not true.
  const parked = trucks
    .filter(truck => truck.position)
    .map(truck => project(truck.position));
  if (parked.length)
    for (const group of groups)
      for (const member of group)
        if (
          !clustered.has(member.row) &&
          parked.some(
            truck =>
              Math.abs(truck[0] - member.point[0] - member.row.markerOffsetX) <
                width &&
              Math.abs(truck[1] - member.point[1] - member.row.markerOffsetY) <
                width,
          )
        )
          member.row.markerOffsetX += width;
  return { clusters, clustered };
}
