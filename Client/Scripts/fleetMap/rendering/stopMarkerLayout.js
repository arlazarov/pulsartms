import { sceneMetrics as metrics } from './sceneMetrics.js';
import { markerProjection } from './markerProjection.js';
import { truckColor } from './truckAppearance.js';

export function stopMarkerLabel(_job, number) {
  return String(number ?? '');
}

// Where every stop badge stands, in one pass with one idea: a badge stands
// on the place it marks, and moves only when it must and only as far as it
// must.
//
// This replaced three mechanisms that did not know about each other - a grid
// that chained touching badges together and set them out in a pattern that
// had nothing to do with where the stops are, a separate jump out from under
// a parked truck, and a row tried in between. Each was reasonable and
// together the badges went every which way.
//
// 1. A badge stands on its anchor while it touches nothing.
// 2. Two badges that touch part along the line between them, equally, just
//    far enough to clear. North of stays north of. Nothing else moves a
//    badge - a truck least of all: how far a badge is drawn from a truck is
//    how far the stop is from the truck, and a dispatcher reads "he is
//    nearly there" off that gap. Parting from the truck by a badge's width
//    turned a mile and a half into ten.
// 3. Stops at the very same address have no line between them, so they keep
//    the formation they always had: a pair, a triangle, rows of two.
// 4. Where a truck is standing on a stop the two become one mark on that
//    point: the badge inside a ring of the truck's colour, with the unit
//    number above it as ever. Two marks on one point meant one of them had
//    to be moved off the place it names - under the truck, or aside from
//    it - and whichever moved then pointed at nothing.
//
// The picture is the same constellation at every zoom, only tighter. Which
// way two stops part is read from where they are on the ground, not from
// the screen, so "7" is north-east of "3" however far out the camera is;
// and which stop a truck is standing on is a distance on the ground too. A
// grid by stop number was tried for the far zooms, and the badges changed
// places as the camera crossed from one way of laying out to the other.
//
// Only a standing truck counts. One driving past a stop is over it for a
// moment and gone, and laying badges out for every frame of that would
// rebuild the map for nothing anyone could read.
const passes = 48;
const most = 80;
// Where things are on the ground is read at one fixed zoom, where a pixel is
// about thirty metres: directions and the question "is the truck at this
// stop" must not change with the camera.
const groundZoom = 12;
const atTheStop = 20;

export function layoutStopMarkers(rows, zoom, trucks = []) {
  const project = markerProjection(zoom);
  const radius = metrics.stopBadgeDiameter / 2;
  const byNumber = (a, b) =>
    (parseInt(a.row.number, 10) || 0) - (parseInt(b.row.number, 10) || 0);

  const ground = markerProjection(groundZoom);
  // How wide a badge is depends on whether it is wearing a truck.
  const spread = item =>
    (item.row.standing
      ? metrics.stopBadgeStandingDiameter
      : metrics.stopBadgeDiameter) / 2;
  const places = new Map();
  const items = rows.map(row => {
    row.markerLabel = stopMarkerLabel(row.job, row.number);
    row.markerOffsetX = 0;
    row.markerOffsetY = 0 - metrics.stopBadgeOffset;
    const anchor = project(row.position);
    const item = {
      row,
      anchor,
      at: [...anchor],
      ground: ground(row.position),
      held: false,
    };
    const key = row.position.map(value => value.toFixed(3)).join(',');
    if (!places.has(key)) places.set(key, []);
    places.get(key).push(item);
    return item;
  });
  // Every truck starts the pass as a truck drawn on its own; one standing
  // on a stop is then merged into it. Cleared only for the parked, a truck
  // that drove off from a stop kept the ring and was never drawn again.
  for (const truck of trucks) {
    truck.merged = false;
    truck.markerOffset = null;
  }
  const parked = trucks
    .filter(truck => truck.position && !(truck.speed > 0))
    .map(truck => ({
      truck,
      at: project(truck.position),
      ground: ground(truck.position),
    }));
  // The formation stops at one address have always had - a pair, a
  // triangle, rows of two - in stop order, rising from the place they mark.
  const form = (members, [x, y], apart) => {
    members.sort(byNumber);
    members.forEach((item, index) => {
      const finalOdd = index === members.length - 1 && members.length % 2;
      const across =
        members.length < 2 || finalOdd ? 0 : ((index % 2 ? 1 : -1) * apart) / 2;
      const rowIndex = Math.floor(index / 2);
      // The centered last badge forms an equilateral triangle with the pair
      // beside it.
      const rise =
        finalOdd && members.length > 1
          ? (rowIndex - 1) * apart + (Math.sqrt(3) * apart) / 2
          : rowIndex * apart;
      item.at = [x + across, y - rise];
    });
  };

  // Rule 3, and then rule 4: a truck standing on a stop is drawn as a ring
  // around that stop's badge, which keeps its point and gives way to nothing.
  for (const group of places.values()) {
    const truck = parked.find(
      standing =>
        Math.hypot(
          standing.ground[0] - group[0].ground[0],
          standing.ground[1] - group[0].ground[1],
        ) < atTheStop,
    );
    if (truck) {
      truck.holds ??= group[0];
      for (const item of group) {
        item.held = true;
        item.row.standing = truckColor(truck.truck.engine, truck.truck.speed);
      }
    }
    // Ringed badges stand further apart than bare ones, so the formation is
    // measured after it is known which of the two they are.
    if (group.length > 1)
      form(group, group[0].anchor, spread(group[0]) * 2 + metrics.stopBadgeGap);
  }
  // The unit number belongs over the mark the truck has become, so the truck
  // rows carry the way from where the truck is to the badge it is drawn in.
  // Its own icon is not drawn at all while it is there.
  for (const { truck, at, holds } of parked) {
    truck.merged = !!holds;
    truck.markerOffset = holds
      ? [Math.round(holds.at[0] - at[0]), Math.round(holds.at[1] - at[1])]
      : null;
  }

  // Rule 2, against each other and against what does not move. Every push
  // of a pass is added up before any is made, and half of it is made: a
  // badge with three neighbours was otherwise pushed three times over.
  if (items.length <= most)
    for (let pass = 0; pass < passes; pass++) {
      let moved = false;
      const pushes = items.map(() => [0, 0]);
      for (let i = 0; i < items.length; i++)
        for (let j = i + 1; j < items.length; j++) {
          const a = items[i],
            b = items[j];
          if (a.held && b.held) continue;
          let dx = b.at[0] - a.at[0],
            dy = b.at[1] - a.at[1];
          const gap = Math.hypot(dx, dy);
          const needed = spread(a) + spread(b) + metrics.stopBadgeGap;
          if (gap >= needed - 0.01) continue;
          // Which way they part is where they are on the ground, so it is
          // the same way at every zoom. If other pushes have carried one
          // past the other, this is also what carries it back: parting the
          // way they happened to lie kept them in the wrong order for good.
          const gx = b.ground[0] - a.ground[0],
            gy = b.ground[1] - a.ground[1];
          if (Math.hypot(gx, gy) > 0.5) {
            dx = gx;
            dy = gy;
          }
          let length = Math.hypot(dx, dy);
          if (length < 0.01) {
            // No line between them to part along; any fixed one will do.
            const angle = (i * 7 + j) * 2.399963;
            dx = Math.cos(angle);
            dy = Math.sin(angle);
            length = 1;
          }
          const push = needed - gap;
          const share = a.held || b.held ? 1 : 0.5;
          const step = [(dx / length) * push, (dy / length) * push];
          if (!a.held) {
            pushes[i][0] -= step[0] * share;
            pushes[i][1] -= step[1] * share;
          }
          if (!b.held) {
            pushes[j][0] += step[0] * share;
            pushes[j][1] += step[1] * share;
          }
          moved = true;
        }
      items.forEach((item, index) => {
        item.at[0] += pushes[index][0] * 0.5;
        item.at[1] += pushes[index][1] * 0.5;
      });
      if (!moved) break;
    }

  // A badge that ends up drawn over a truck says so, and is drawn with the
  // rim that makes the two read as two. Only a standing truck is asked
  // about: one driving past covers a badge for a frame and is gone.
  for (const item of items) {
    if (item.row.standing) continue;
    item.row.stacked = parked.some(
      ({ at: truck }) =>
        Math.hypot(item.at[0] - truck[0], item.at[1] - truck[1]) <
        radius + metrics.truckSize / 2,
    );
  }

  for (const { row, anchor, at } of items) {
    // Rounded so that the same stops give the same numbers and an unchanged
    // badge is recognised as unchanged; "|| 0" keeps a minus off zero.
    row.markerOffsetX = Math.round((at[0] - anchor[0]) * 1e4) / 1e4 || 0;
    row.markerOffsetY =
      Math.round((at[1] - anchor[1] - metrics.stopBadgeOffset) * 1e4) / 1e4 ||
      0;
  }
}
