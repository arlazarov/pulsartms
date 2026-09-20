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
// 3. Stops at the very same address have no line between them, so they keep
//    the formation they always had: a pair, a triangle, rows of two.
// 4. Where a truck is standing on a stop, the badge keeps the point and the
//    truck steps aside - up and to the right, far enough to clear, always
//    the same way. Of the two marks only one can have the point, and it
//    must be the stop's: a truck is known by the unit number it carries and
//    is read as being wherever it is drawn, while a number beside a place
//    means nothing unless it is on that place. Hung under the truck instead,
//    the badge sat over open ground with the point it marks hidden under
//    the truck above it.
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
  const apart = metrics.stopBadgeDiameter + metrics.stopBadgeGap;
  const clearOfTruck = radius + metrics.truckSize / 2 + metrics.stopBadgeGap;
  // A badge may come right up to the number - the pill has an edge of its
  // own - so the gap kept between badges is not asked for here. Asking for
  // it moved a badge that stood a pixel and a half clear of the number.
  const numberHalf = [36, 12];
  const clearOfNumber = radius;
  const byNumber = (a, b) =>
    (parseInt(a.row.number, 10) || 0) - (parseInt(b.row.number, 10) || 0);

  const ground = markerProjection(groundZoom);
  // The one direction a truck ever steps, so that arriving at a stop looks
  // the same everywhere on the map.
  const aside = [Math.SQRT1_2 * clearOfTruck, -Math.SQRT1_2 * clearOfTruck];
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
  const parked = trucks
    .filter(truck => truck.position && !(truck.speed > 0))
    .map(truck => ({
      truck,
      at: project(truck.position),
      ground: ground(truck.position),
    }));
  // The formation stops at one address have always had - a pair, a
  // triangle, rows of two - in stop order, rising from the place they mark.
  const form = (members, [x, y]) => {
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

  // Rule 3, and then rule 4: a truck standing on a stop steps aside from it,
  // and the badges there are held on their own ground.
  for (const group of places.values()) {
    if (group.length > 1) form(group, group[0].anchor);
    const truck = parked.find(
      standing =>
        Math.hypot(
          standing.ground[0] - group[0].ground[0],
          standing.ground[1] - group[0].ground[1],
        ) < atTheStop,
    );
    if (!truck) continue;
    // One step, however many stops of the load are at this address.
    if (!truck.moved) {
      truck.moved = true;
      truck.at = [truck.at[0] + aside[0], truck.at[1] + aside[1]];
    }
    for (const item of group) item.held = true;
  }
  // The truck rows carry the step to the layers that draw them, so the unit
  // number goes with the truck and a badge is drawn where the truck was.
  for (const { truck, moved } of parked)
    truck.markerOffset = moved
      ? [Math.round(aside[0]), Math.round(aside[1])]
      : null;

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
      // A standing truck parts badges from itself the same way, as one more
      // push among the others. Set on its clearance outright each pass, it
      // fought the pushes between badges, and the badge nearest the truck on
      // the ground did not end up nearest it on the screen.
      items.forEach((item, index) => {
        if (item.held) return;
        for (const { at: truck, ground: truckGround } of parked) {
          const dx = item.at[0] - truck[0],
            dy = item.at[1] - truck[1];
          const distance = Math.hypot(dx, dy);
          if (distance >= clearOfTruck - 0.01) continue;
          // Away from the truck the way the stop lies from it on the
          // ground, so a stop east of the truck is east of it at any zoom.
          const gx = item.ground[0] - truckGround[0],
            gy = item.ground[1] - truckGround[1];
          const far = Math.hypot(gx, gy);
          const [ux, uy] =
            far > 0.5
              ? [gx / far, gy / far]
              : distance < 0.01
                ? [0, 1]
                : [dx / distance, dy / distance];
          pushes[index][0] += ux * (clearOfTruck - distance) * 2;
          pushes[index][1] += uy * (clearOfTruck - distance) * 2;
          moved = true;
        }
      });
      items.forEach((item, index) => {
        item.at[0] += pushes[index][0] * 0.5;
        item.at[1] += pushes[index][1] * 0.5;
      });
      for (const item of items) {
        if (item.held) continue;
        for (const { at: truck } of parked) {
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
