import test from 'node:test';
import assert from 'node:assert/strict';
import { stopMarkerLabel } from '../../Scripts/fleetMap/rendering/stopMarkerLayout.js';
import { snapshotStops } from '../../Scripts/fleetMap/rendering/stopData.js';

test('compact markers show only ordered numbers while stop type remains in the card', () => {
  for (const job of ['Pickup', 'Pick Up', 'pick_up'])
    assert.equal(stopMarkerLabel(job, '2/4'), '2/4');
  for (const job of ['Delivery', 'Drop Off', 'drop_off'])
    assert.equal(stopMarkerLabel(job, '3'), '3');
  assert.equal(stopMarkerLabel(null, '5'), '5');
  assert.equal(stopMarkerLabel('Delivery', null), '');
});

test('coincident current and future badges separate while their geographic anchors stay exact', () => {
  const stops = [
    { id: 1, position: [-80, 40], number: '1', job: 'Delivery' },
    {
      id: 2,
      position: [-80.0001, 40],
      number: '2',
      job: 'Pickup',
      transientLabel: true,
    },
    {
      id: 3,
      position: [-79, 40],
      number: '3',
      job: 'Delivery',
      transientLabel: true,
    },
  ];
  const first = snapshotStops(stops);
  const byId = new Map(first.stopData.map(row => [row.id, row]));
  assert.ok(byId.get(1).markerOffsetX < 0);
  assert.ok(byId.get(2).markerOffsetX > 0);
  assert.equal(byId.get(3).markerOffsetX, 0);
  for (const stop of stops)
    assert.equal(byId.get(stop.id).position, stop.position);
  stops[1].highlighted = true;
  const selected = snapshotStops(stops, first.stopData);
  for (const row of selected.stopData) {
    assert.equal(
      row.markerOffsetX,
      byId.get(row.id).markerOffsetX,
      'selection does not shuffle badges',
    );
    assert.equal(row.markerOffsetY, byId.get(row.id).markerOffsetY);
  }
  assert.equal(
    snapshotStops(stops, selected.stopData).stopData,
    selected.stopData,
    'unchanged polling reuses data',
  );
  stops[1].visible = false;
  const hidden = snapshotStops(stops, selected.stopData);
  assert.equal(
    hidden.stopData.find(row => row.id === 1).markerOffsetX,
    0,
    'hiding next loads restores the centered badge',
  );
});

test('coincident visits keep individual circles evenly spaced with the final odd circle centered', () => {
  const stops = Array.from({ length: 5 }, (_, id) => ({
    id,
    position: [-80, 40],
    number: String(id + 8),
    job: id % 2 ? 'Pickup' : 'Delivery',
  }));
  const rows = snapshotStops(stops).stopData;
  assert.deepEqual(
    rows.map(row => row.markerLabel),
    ['8', '9', '10', '11', '12'],
  );
  assert.equal(
    new Set(rows.map(row => `${row.markerOffsetX},${row.markerOffsetY}`)).size,
    5,
  );
  assert.equal(
    rows[1].markerOffsetX - rows[0].markerOffsetX,
    36,
    'digit count never changes circle spacing',
  );
  assert.equal(rows[2].markerOffsetY, rows[0].markerOffsetY - 36);
  assert.equal(rows[4].markerOffsetX, 0);
});

test('three shared-site visits form a compact triangle with equal non-overlapping gaps', () => {
  const stops = ['1', '3', '4'].map(number => ({
    id: number,
    number,
    position: [-80, 40],
  }));
  const rows = snapshotStops(stops).stopData;
  for (let i = 0; i < rows.length; i++) {
    assert.equal(rows[i].position, stops[i].position);
    for (let j = i + 1; j < rows.length; j++) {
      const distance = Math.hypot(
        rows[i].markerOffsetX - rows[j].markerOffsetX,
        rows[i].markerOffsetY - rows[j].markerOffsetY,
      );
      assert.ok(
        Math.abs(distance - 36) < 0.001,
        '34px circles retain a 2px gap on every side',
      );
    }
  }
  assert.ok(Math.max(...rows.map(row => -row.markerOffsetY)) < 32);
});

// A badge stands on the place it marks, and moves only when it must and only
// as far as it must. These pin the four rules that replaced a grid, a jump
// and a row that between them sent the badges every which way.
const screen = (position, zoom) => {
  const world = 256 * 2 ** zoom;
  const sin = Math.sin((position[1] * Math.PI) / 180);
  return [
    ((position[0] + 180) / 360) * world,
    (0.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * world,
  ];
};
const drawnAt = (row, zoom) => {
  const [x, y] = screen(row.position, zoom);
  return [x + row.markerOffsetX, y + row.markerOffsetY];
};
const gap = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1]);

test('a badge that touches nothing stands on its anchor', () => {
  const rows = snapshotStops(
    [
      { id: 'a', number: '3', position: [-80.84, 35.22] },
      { id: 'b', number: '7', position: [-80.7, 35.28] },
    ],
    [],
    [],
    13,
  ).stopData;
  assert.deepEqual(
    rows.map(row => [row.markerOffsetX, row.markerOffsetY]),
    [
      [0, 0],
      [0, 0],
    ],
  );
});

// 11006 stood at Charlotte with the last stop of one load and the first of
// the next a few miles apart, and their badges were one blot. They used to
// be set out in a grid that had nothing to do with where the stops are.
test('two that touch part along the line between them, equally, just far enough', () => {
  // Close enough to touch, far enough apart that the line between them
  // means something.
  const zoom = 8;
  const stops = [
    { id: 'a', number: '3', position: [-80.84, 35.22] },
    // East and north of the first.
    { id: 'b', number: '7', position: [-80.7, 35.28] },
  ];
  const rows = snapshotStops(stops, [], [], zoom).stopData;
  const [a, b] = ['a', 'b'].map(id => rows.find(row => row.id === id));
  const [at, bt] = [drawnAt(a, zoom), drawnAt(b, zoom)];
  assert.ok(Math.abs(gap(at, bt) - 36) < 0.05, 'a badge and its gap apart');
  assert.ok(bt[0] > at[0] && bt[1] < at[1], 'east and north of stays so');
  assert.ok(
    Math.abs(a.markerOffsetX + b.markerOffsetX) < 0.05 &&
      Math.abs(a.markerOffsetY + b.markerOffsetY) < 0.05,
    'each gives way by the same amount',
  );
});

test('stops along a road stay on the line of it, in order', () => {
  const zoom = 12;
  const stops = Array.from({ length: 12 }, (_, id) => ({
    id,
    number: String(id + 1),
    position: [-80 + id * 0.01, 40],
  }));
  const rows = snapshotStops(stops, [], [], zoom).stopData;
  const drawn = stops.map(stop =>
    drawnAt(
      rows.find(row => row.id === stop.id),
      zoom,
    ),
  );
  assert.ok(rows.every(row => row.markerOffsetY === 0));
  for (let i = 1; i < drawn.length; i++)
    assert.ok(drawn[i][0] > drawn[i - 1][0], 'never out of stop order');
});

// Of the two marks on one point only one can keep it, and it must be the
// stop's: a truck is known by the unit number it carries, while a number
// beside a place means nothing unless it is on that place. Hung under the
// truck instead, the badge sat over open ground with the point it marks
// hidden under the truck above it.
// The ring does not ask whether the truck has arrived. Asked that, 11006 -
// parked three hundred metres short of its delivery - was drawn as a ring at
// every zoom, so zooming in on it took the truck off the yard it stands in
// and put it on the dock. The ring says these are one place at this zoom;
// zoom in far enough and it opens into two marks.
test('the ring opens into two marks when there is room for both', () => {
  const truck = { position: [-78.9, 35.9], speed: 0, engine: 'Off' };
  const stops = [{ id: 's', number: '2', position: [-78.8963, 35.9] }];
  const ringed = snapshotStops(stops, [], [], 11, [truck]).stopData[0];
  // The ring is the truck, so it is the truck's colour: this one is shut
  // down for the night at the dock.
  assert.equal(ringed.standing, '#64748b');
  assert.equal(truck.merged, true, 'and the truck is that ring');
  const near = snapshotStops(stops, [], [], 13, [truck]).stopData[0];
  assert.equal(near.standing, undefined);
  assert.equal(near.stacked, true, 'a crescent of truck shows behind it');
  const apart = snapshotStops(stops, [], [], 15, [truck]).stopData[0];
  assert.equal(apart.stacked, false, 'and further in, two plain marks');
  assert.equal(truck.merged, false, 'the truck is back on its own yard');
  assert.deepEqual([apart.markerOffsetX, apart.markerOffsetY], [0, 0]);
});

test('a truck standing on a stop becomes a ring around its badge', () => {
  const zoom = 13;
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const truck = { position: [-82.5501, 35.3799], engine: 'on' };
  const [row] = snapshotStops(stops, [], [], zoom, [truck]).stopData;
  assert.deepEqual(
    [row.markerOffsetX, row.markerOffsetY],
    [0, 0],
    'the badge stands on the stop, not beside it',
  );
  assert.deepEqual(row.position, stops[0].position, 'the stop has not moved');
  // One mark on one point: the badge in a ring of the truck's colour, with
  // the truck's own icon not drawn and its unit number over the ring.
  assert.equal(row.standing, '#16a34a');
  assert.equal(truck.merged, true);
  const [dx, dy] = truck.markerOffset;
  assert.ok(Math.hypot(dx, dy) < 2, 'the unit number sits over the badge');
  const off = snapshotStops(stops, [], [], zoom, [{ ...truck, engine: 'off' }])
    .stopData[0];
  // The ring is the truck, so it says what the truck says: green with the
  // engine running, grey with it shut down.
  assert.equal(off.standing, '#64748b');
});

// The badge a truck stands on gives way to nothing: its neighbour parts
// from it the whole way, by rule 2. Sent to a fixed side instead, the badge
// used to land on the stop standing there - "2" square on "3".
test('the badge a truck stands on holds its ground and its neighbour parts', () => {
  const zoom = 9;
  const truck = { position: [-80.95, 35.22] };
  const stops = [
    { id: 'two', number: '2', position: [-80.95, 35.22] },
    { id: 'three', number: '3', position: [-80.84, 35.22] },
  ];
  const rows = snapshotStops(stops, [], [], zoom, [truck]).stopData;
  const row = id => rows.find(item => item.id === id);
  assert.deepEqual(
    [row('two').markerOffsetX, row('two').markerOffsetY],
    [0, 0],
  );
  assert.ok(row('three').markerOffsetX > 0, 'the neighbour gives way east');
  assert.ok(
    gap(drawnAt(row('two'), zoom), drawnAt(row('three'), zoom)) >= 36 - 0.05,
  );
});

// How far a badge is drawn from a truck is how far the stop is from the
// truck, and "he is nearly there" is read off that gap. 54777 was parked a
// mile and a half short of its delivery in Gansevoort; at a state-wide zoom
// the two marks overlapped, so the badge was pushed a badge's width clear of
// the truck - off the place it names, and a mile and a half drawn as ten.
test('a badge never gives way to a truck, so the gap stays the distance', () => {
  const zoom = 10;
  const truck = { position: [-73.71563, 43.167812], engine: 'on' };
  const stops = [
    { id: 'ace', number: '2', position: [-73.7418915, 43.1753532] },
  ];
  const [row] = snapshotStops(stops, [], [], zoom, [truck]).stopData;
  assert.deepEqual([row.markerOffsetX, row.markerOffsetY], [0, 0]);
  assert.equal(row.standing, undefined, 'a mile and a half is not "at it"');
  const [tx, ty] = screen(truck.position, zoom);
  const [sx, sy] = screen(stops[0].position, zoom);
  const drawn = drawnAt(row, zoom);
  assert.ok(Math.hypot(sx - tx, sy - ty) < 34, 'the marks do overlap here');
  assert.ok(
    Math.hypot(drawn[0] - sx, drawn[1] - sy) < 0.05,
    'and the badge is still drawn on its own point',
  );
  // Drawn over the truck, it says so: the wider white rim is what makes the
  // disc behind it read as a truck and not a smudge.
  assert.equal(row.stacked, true);
  const apart = snapshotStops(stops, [], [], 13, [truck]).stopData[0];
  assert.equal(apart.stacked, false, 'zoomed in, they no longer touch');
});

// Further out still, the badge covers the truck altogether, and a white rim
// with nothing behind it says the stop stands alone. So the badge wears the
// truck as a ring - the same mark as a truck actually standing on the stop,
// which at that zoom is what the picture is saying either way.
test('a badge that would hide a truck wears it as a ring instead', () => {
  const truck = { position: [-73.71563, 43.167812], engine: 'on' };
  const stops = [
    { id: 'ace', number: '2', position: [-73.7418915, 43.1753532] },
  ];
  for (const zoom of [7, 8, 9]) {
    const [row] = snapshotStops(stops, [], [], zoom, [truck]).stopData;
    assert.equal(row.standing, '#16a34a', `zoom ${zoom}`);
    assert.deepEqual([row.markerOffsetX, row.markerOffsetY], [0, 0]);
  }
});

// A truck driving past a stop is over it for a moment and gone.
// A truck that drove off from a stop kept its ring and was never drawn as
// a truck again: what is cleared each pass is every truck, not the parked.
test('a truck that leaves a stop is a truck again', () => {
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const truck = { position: [-82.5501, 35.3799], engine: 'on' };
  assert.equal(
    snapshotStops(stops, [], [], 13, [truck]).stopData[0].standing,
    '#16a34a',
  );
  assert.equal(truck.merged, true);
  truck.speed = 40;
  truck.position = [-82.4, 35.3];
  const [row] = snapshotStops(stops, [], [], 13, [truck]).stopData;
  assert.equal(row.standing, undefined, 'the badge is a badge again');
  assert.equal(truck.merged, false, 'and the truck is drawn again');
  assert.equal(truck.markerOffset, null);
});

test('a truck driving past moves nothing', () => {
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const [row] = snapshotStops(stops, [], [], 13, [
    { position: [-82.5501, 35.3799], speed: 45 },
  ]).stopData;
  assert.deepEqual([row.markerOffsetX, row.markerOffsetY], [0, 0]);
});

test('the same stops always give the same layout', () => {
  const stops = () => [
    { id: 'a', number: '3', position: [-80.84, 35.22] },
    { id: 'b', number: '7', position: [-80.7, 35.28] },
    { id: 'c', number: '6', position: [-80.76, 35.2] },
  ];
  const once = snapshotStops(stops(), [], [], 5).stopData;
  const again = snapshotStops(stops(), [], [], 5).stopData;
  assert.deepEqual(
    once.map(row => [row.markerOffsetX, row.markerOffsetY]),
    again.map(row => [row.markerOffsetX, row.markerOffsetY]),
  );
});

// Zooming out, "3", "6" and "7" changed places: the middle zooms parted them
// by where they are, the far ones set them out in a grid by stop number, and
// the constellation rearranged itself as the camera crossed from one to the
// other. It is the same constellation at every zoom now, only tighter -
// which way two stops part is read from the ground, not from the screen.
test('the stops keep their places relative to each other at every zoom', () => {
  const truck = { position: [-80.95, 35.22] };
  const stops = () => [
    { id: '2', number: '2', position: [-80.95, 35.22] },
    { id: '3', number: '3', position: [-80.77, 35.25] },
    { id: '6', number: '6', position: [-80.47, 35.21] },
    { id: '7', number: '7', position: [-80.72, 35.3] },
  ];
  for (const zoom of [4, 5, 6, 7, 8, 9, 10]) {
    const rows = snapshotStops(stops(), [], [], zoom, [truck]).stopData;
    const [tx, ty] = screen(truck.position, zoom);
    const at = id =>
      drawnAt(
        rows.find(row => row.id === id),
        zoom,
      );
    const [two, three, six, seven] = ['2', '3', '6', '7'].map(at);
    // The truck's own stop on its point; the rest east of it, as on the
    // ground.
    assert.ok(
      Math.abs(two[0] - tx) < 0.05 && Math.abs(two[1] - ty) < 0.05,
      `zoom ${zoom}: 2`,
    );
    assert.ok(three[0] > tx, `zoom ${zoom}: 3 east of the truck`);
    assert.ok(six[0] > three[0], `zoom ${zoom}: 6 east of 3`);
    assert.ok(seven[1] < three[1], `zoom ${zoom}: 7 north of 3`);
    assert.ok(seven[1] < six[1], `zoom ${zoom}: 7 north of 6`);
    for (const [a, b] of [
      [three, six],
      [three, seven],
      [six, seven],
      [two, three],
    ])
      assert.ok(gap(a, b) >= 36 - 0.5, `zoom ${zoom}: nothing overlaps`);
  }
});

// Which stop a truck is standing on is a distance on the ground. Asked of
// the screen, every stop in the county was "at the truck" once the camera
// was far enough out.
test('only the stop the truck is at holds it aside, however far out', () => {
  const truck = { position: [-80.95, 35.22] };
  const stops = [
    { id: 'here', number: '2', position: [-80.951, 35.2205] },
    { id: 'miles', number: '3', position: [-80.77, 35.25] },
  ];
  const rows = snapshotStops(stops, [], [], 4, [truck]).stopData;
  const [tx, ty] = screen(truck.position, 4);
  const here = drawnAt(
    rows.find(row => row.id === 'here'),
    4,
  );
  const miles = drawnAt(
    rows.find(row => row.id === 'miles'),
    4,
  );
  assert.ok(Math.abs(here[0] - tx) < 0.05 && Math.abs(here[1] - ty) < 0.05);
  // One ring to a truck, around the stop it is nearest. Far enough out a
  // truck covers half a state's worth of badges, and ringing every one of
  // them says it is standing on all of them at once - and holds them all
  // where they are, on top of each other.
  const rings = rows.filter(row => row.standing).map(row => row.id);
  assert.deepEqual(rings, ['here']);
  assert.ok(miles[0] > tx, 'the far one is beside the truck, where it lies');
});

test('no badge is ever thrown far from the place it marks', () => {
  for (const zoom of [4, 5, 6, 7, 8, 9, 10, 11, 12]) {
    const truck = { position: [-80.95, 35.22] };
    const stops = [
      { id: '2', number: '2', position: [-80.95, 35.22] },
      { id: '3', number: '3', position: [-80.77, 35.25] },
      { id: '6', number: '6', position: [-80.47, 35.21] },
      { id: '7', number: '7', position: [-80.72, 35.3] },
    ];
    const rows = snapshotStops(stops, [], [], zoom, [truck]).stopData;
    for (const row of rows)
      assert.ok(
        Math.hypot(row.markerOffsetX, row.markerOffsetY) < 36 * 3,
        `zoom ${zoom}, stop ${row.number}`,
      );
  }
});
