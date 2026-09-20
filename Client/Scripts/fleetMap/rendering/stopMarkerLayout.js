import { sceneMetrics as metrics } from './sceneMetrics.js';
import { markerProjection } from './markerProjection.js';

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
// 2. Two that touch part along the line between them, equally, just far
//    enough to clear. North of stays north of.
// 3. Stops at one place have no line between them, so they keep the
//    formation they always had: a pair, a triangle, rows of two, in stop
//    order. One place means one place on the screen - the same address, or
//    miles apart at a zoom where miles are a pixel.
// 4. What has run together with a standing truck hangs directly under it.
//    Not wherever is free - always under: the unit number above, the stops
//    below. It stops being layout and becomes a sign for "the truck is at
//    these stops". The truck and its number are fixed, and other badges
//    part from them by rule 2.
//
// Only a standing truck counts. One driving past a stop is over it for a
// moment and gone, and laying badges out for every frame of that would
// rebuild the map for nothing anyone could read.
const passes = 48;
const most = 80;
// Closer than this on screen, two stops are one place to the eye: there is
// no line between them worth parting along, only noise, and parting along
// noise shot a badge across two states.
const onePlace = 10;

export function layoutStopMarkers(rows, zoom, trucks = []) {
  const project = markerProjection(zoom);
  const radius = metrics.stopBadgeDiameter / 2;
  const apart = metrics.stopBadgeDiameter + metrics.stopBadgeGap;
  const clearOfTruck = radius + metrics.truckSize / 2 + metrics.stopBadgeGap;
  // A badge may come right up to the number - the pill has an edge of its
  // own - so the gap kept between badges is not asked for here. Asking for
  // it moved a badge that stood a pixel and a half clear of the number.
  const numberHalf = [36, 12];
  const clearOfNumber = radius;
  const byNumber = (a, b) =>
    (parseInt(a.row.number, 10) || 0) - (parseInt(b.row.number, 10) || 0);

  const clumps = [];
  const items = rows.map(row => {
    row.markerLabel = stopMarkerLabel(row.job, row.number);
    row.markerOffsetX = 0;
    row.markerOffsetY = 0 - metrics.stopBadgeOffset;
    const anchor = project(row.position);
    const item = { row, anchor, at: [...anchor], held: false };
    // Measured from the first of a clump, not from any of it, or a road of
    // stops chains into one clump the length of the road.
    const clump = clumps.find(
      members =>
        Math.hypot(
          members[0].anchor[0] - anchor[0],
          members[0].anchor[1] - anchor[1],
        ) < onePlace,
    );
    if (clump) clump.push(item);
    else clumps.push([item]);
    return item;
  });
  const parked = trucks
    .filter(truck => truck.position && !(truck.speed > 0))
    .map(truck => project(truck.position));
  const middle = members => [
    members.reduce((sum, item) => sum + item.anchor[0], 0) / members.length,
    members.reduce((sum, item) => sum + item.anchor[1], 0) / members.length,
  ];
  // The formation stops at one place have always had - a pair, a triangle,
  // rows of two - in stop order. Rising from its place, or hanging under a
  // truck.
  const form = (members, [x, y], hang) => {
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
      item.at = [x + across, hang ? y + clearOfTruck + rise : y - rise];
      item.held = hang;
    });
  };

  // Rule 4: everything that has run together with a standing truck hangs
  // under it as one formation. Rule 3 for the clumps left over.
  const free = [];
  const hung = new Map(parked.map(truck => [truck, []]));
  for (const clump of clumps) {
    const [x, y] = middle(clump);
    const truck = parked.find(
      at => Math.hypot(at[0] - x, at[1] - y) < clearOfTruck,
    );
    if (truck) hung.get(truck).push(...clump);
    else free.push(clump);
  }
  for (const [truck, members] of hung)
    if (members.length) form(members, truck, true);
  for (const clump of free)
    if (clump.length > 1) form(clump, middle(clump), false);

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
          if (gap >= apart - 0.01) continue;
          let length = gap;
          if (gap < 0.01) {
            // No line between them to part along; any fixed one will do.
            const angle = (i * 7 + j) * 2.399963;
            dx = Math.cos(angle);
            dy = Math.sin(angle);
            length = 1;
          }
          const push = apart - gap;
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
      for (const item of items) {
        if (item.held) continue;
        for (const truck of parked) {
          const dx = item.at[0] - truck[0],
            dy = item.at[1] - truck[1];
          const distance = Math.hypot(dx, dy);
          if (distance < clearOfTruck - 0.01) {
            const [ux, uy] =
              distance < 0.01 ? [0, 1] : [dx / distance, dy / distance];
            item.at = [
              truck[0] + ux * clearOfTruck,
              truck[1] + uy * clearOfTruck,
            ];
            moved = true;
          }
          // The unit number, measured as the pill it is: how far the badge
          // is from the nearest point of it, not from a box drawn round it.
          // A badge that only clips a corner moves a few pixels, not the
          // width of the corner.
          const lx = item.at[0] - truck[0],
            ly = item.at[1] - (truck[1] - metrics.truckLabelOffset);
          const ex = lx - Math.max(-numberHalf[0], Math.min(numberHalf[0], lx)),
            ey = ly - Math.max(-numberHalf[1], Math.min(numberHalf[1], ly));
          const reach = Math.hypot(ex, ey);
          if (reach < clearOfNumber - 0.01) {
            const short = clearOfNumber - reach;
            // The least it can move is straight away from the nearest point
            // of the number. That is taken unless it lands the badge on the
            // truck - down is where the truck is, the truck sends it
            // straight back up, and the two took turns with the badge. Then,
            // and when the badge sits on the number itself, out to the side
            // is the only way that ends.
            const away =
              reach > 0.01
                ? [
                    item.at[0] + (ex / reach) * short,
                    item.at[1] + (ey / reach) * short,
                  ]
                : null;
            if (
              away &&
              Math.hypot(away[0] - truck[0], away[1] - truck[1]) >=
                clearOfTruck - 0.01
            )
              item.at = away;
            else
              item.at[0] =
                truck[0] + (lx < 0 ? -1 : 1) * (numberHalf[0] + clearOfNumber);
            moved = true;
          }
        }
      }
      if (!moved) break;
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
