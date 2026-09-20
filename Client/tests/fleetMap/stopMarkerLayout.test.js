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
  const zoom = 5;
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

// Not wherever is free - always under: the unit number above, the stop
// below, so a badge under a truck is a sign for "the truck is at this stop".
test('the stop a truck stands on stands directly under the truck', () => {
  const zoom = 13;
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const truck = { position: [-82.5501, 35.3799] };
  const [row] = snapshotStops(stops, [], [], zoom, [truck]).stopData;
  const [x, y] = drawnAt(row, zoom);
  const [tx, ty] = screen(truck.position, zoom);
  assert.ok(Math.abs(x - tx) < 0.05, 'directly under, not beside');
  assert.ok(Math.abs(y - (ty + 33)) < 0.05, 'clear of the truck by the gap');
  assert.deepEqual(row.position, stops[0].position, 'the stop has not moved');
});

// Sent to a fixed side the badge landed on the stop standing there, "2"
// square on "3"; stood in a row the neighbours left their places for it.
test('the neighbours of a parked truck stay where they are', () => {
  const zoom = 9;
  const truck = { position: [-80.95, 35.22] };
  const stops = [
    { id: 'two', number: '2', position: [-80.95, 35.22] },
    { id: 'three', number: '3', position: [-80.84, 35.22] },
  ];
  const rows = snapshotStops(stops, [], [], zoom, [truck]).stopData;
  const row = id => rows.find(item => item.id === id);
  assert.deepEqual(
    [row('three').markerOffsetX, row('three').markerOffsetY],
    [0, 0],
  );
  assert.ok(
    gap(drawnAt(row('two'), zoom), drawnAt(row('three'), zoom)) >= 36 - 0.05,
  );
});

// A stop beside a parked truck parts from the truck and from its number
// the way it would from another badge, and never ends up on either.
test('a stop beside a parked truck clears the truck and the number above it', () => {
  const zoom = 12;
  const truck = { position: [-80.95, 35.22] };
  const [tx, ty] = screen(truck.position, zoom);
  const stops = [
    // A little north-east of the truck: on it, and under its number.
    { id: 'near', number: '5', position: [-80.946, 35.2235] },
  ];
  const [row] = snapshotStops(stops, [], [], zoom, [truck]).stopData;
  const at = drawnAt(row, zoom);
  assert.ok(gap(at, [tx, ty]) >= 33 - 0.05, 'clear of the truck');
  const inNumber =
    Math.abs(at[0] - tx) < 36 + 17 && Math.abs(at[1] - (ty - 30)) < 12 + 17;
  assert.equal(inNumber, false, 'and not standing on the unit number');
});

// A truck driving past a stop is over it for a moment and gone.
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
