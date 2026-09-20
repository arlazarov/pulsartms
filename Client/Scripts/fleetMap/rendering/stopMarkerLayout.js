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
// A truck parked at its own stop hid the badge for it underneath itself.
// Only that badge moves, and it moves the least it can: to the first place
// beside the truck where it touches nothing - not the truck, not another
// badge, and not the place above the truck, which is where its unit number
// goes. Everything else stays where it is. Sending the badge to one fixed
// side put it square on the stop standing there; standing every nearby
// badge in a row beside the truck took stops off the places they are at for
// the sake of one that was hidden. The dot marking where the stop really is
// stays put, so nothing is said that is not true.
export function layoutStopMarkers(rows, zoom, trucks = []) {
  const project = markerProjection(zoom);
  const width = metrics.stopBadgeDiameter + metrics.stopBadgeGap;
  const touches = (a, b) =>
    Math.abs(a[0] - b[0]) < width && Math.abs(a[1] - b[1]) < width;
  const groups = [];
  for (const row of rows) {
    row.markerLabel = stopMarkerLabel(row.job, row.number);
    row.markerOffsetX = 0;
    row.markerOffsetY = 0 - metrics.stopBadgeOffset;
    const point = project(row.position);
    const group = groups.find(members =>
      members.some(member => touches(member.point, point)),
    );
    if (group) group.push({ row, point });
    else groups.push([{ row, point }]);
  }
  const byNumber = (a, b) =>
    (parseInt(a.row.number, 10) || 0) - (parseInt(b.row.number, 10) || 0);
  for (const group of groups) {
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
  // Parked means standing. A truck driving past a stop is over it for a
  // moment and gone, and laying the badges out again for every frame of
  // that would rebuild the map for nothing anyone could read.
  const parked = trucks
    .filter(truck => truck.position && !(truck.speed > 0))
    .map(truck => project(truck.position));
  if (!parked.length) return;
  const members = groups.flat().sort(byNumber);
  const drawn = member => [
    member.point[0] + member.row.markerOffsetX,
    member.point[1] + member.row.markerOffsetY,
  ];
  // Beside first, then below; never above, where the unit number stands.
  const places = [
    [width, 0],
    [-width, 0],
    [0, width],
    [width, width],
    [-width, width],
  ];
  for (const member of members) {
    const truck = parked.find(at => touches(at, drawn(member)));
    if (!truck) continue;
    const taken = [
      ...parked,
      ...members.filter(other => other !== member).map(drawn),
    ];
    const [dx, dy] =
      places.find(([x, y]) => {
        const spot = [truck[0] + x, truck[1] + y];
        return !taken.some(at => touches(at, spot));
      }) ?? places[0];
    member.row.markerOffsetX = truck[0] + dx - member.point[0];
    member.row.markerOffsetY =
      truck[1] + dy - member.point[1] - metrics.stopBadgeOffset;
  }
}
