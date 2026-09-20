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

// 11006 stood at Charlotte with the last stop of one load and the first of
// the next a few miles apart, and their badges were one blot: separation
// grouped by coordinate, and those two do not share one. Zoomed in far
// enough to tell the two places apart, they stand where they are.
test('badges that cover each other are parted, at the zoom they cover it', () => {
  const stops = [
    { id: 'a', number: '3', position: [-80.84, 35.22] },
    { id: 'b', number: '7', position: [-80.7, 35.28] },
  ];
  const offsets = zoom =>
    snapshotStops(stops, [], [], zoom).stopData.map(row => row.markerOffsetX);
  assert.deepEqual(
    offsets(5),
    [-18, 18],
    'one blot at the zoom of a whole run',
  );
  assert.deepEqual(offsets(13), [0, 0], 'two places, told apart, left alone');
});

// A run whose stops line a corridor is not a pin-up at one place, and a
// tower of badges down the side of it says less than the stops themselves.
test('a corridor of stops is left where it is rather than stacked', () => {
  const stops = Array.from({ length: 12 }, (_, id) => ({
    id,
    number: String(id + 1),
    position: [-80 + id * 0.01, 40],
  }));
  const rows = snapshotStops(stops, [], [], 12).stopData;
  assert.deepEqual(
    [...new Set(rows.map(row => `${row.markerOffsetX},${row.markerOffsetY}`))],
    ['0,0'],
  );
});

// 11006 stood on its own delivery and the badge for it was underneath the
// truck, which is the one stop a dispatcher is looking for. It steps aside
// rather than up: above the truck is where its own unit number goes, and
// the two took turns covering each other there.
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

test('a badge under a parked truck stands beside it, and its anchor stays put', () => {
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const truck = { position: [-82.5501, 35.3799] };
  const [row] = snapshotStops(stops, [], [], 13, [truck]).stopData;
  const [x, y] = drawnAt(row, 13);
  const [tx, ty] = screen(truck.position, 13);
  assert.ok(Math.abs(x - (tx + 36)) < 0.01, 'one badge to the side of it');
  assert.ok(Math.abs(y - ty) < 0.01, 'level with it, not above');
  assert.deepEqual(row.position, stops[0].position, 'the stop has not moved');
});

// Sent to one fixed side, the badge came out from under the truck and landed
// on the stop standing there - "2" square on top of "3". Standing them all in
// a row beside the truck took "3" off the place it is at for the sake of one
// that was hidden. Only the hidden badge moves, to the first place beside
// the truck where it touches nothing.
test('only the hidden badge moves, to a place where it touches nothing', () => {
  const zoom = 9;
  const truck = { position: [-80.95, 35.22] };
  const stops = [
    { id: 'two', number: '2', position: [-80.95, 35.22] },
    // Clear of the truck where it really is, and exactly where "2" would
    // land if it simply stepped to the right.
    { id: 'three', number: '3', position: [-80.84, 35.22] },
  ];
  const rows = snapshotStops(stops, [], [], zoom, [truck]).stopData;
  const row = id => rows.find(item => item.id === id);
  assert.deepEqual(
    [row('three').markerOffsetX, row('three').markerOffsetY],
    [0, 0],
    'the neighbour stays where it is',
  );
  const two = drawnAt(row('two'), zoom);
  const three = drawnAt(row('three'), zoom);
  const [tx, ty] = screen(truck.position, zoom);
  const apart = (a, b) =>
    Math.abs(a[0] - b[0]) >= 36 || Math.abs(a[1] - b[1]) >= 36;
  assert.ok(apart(two, three), 'clear of its neighbour');
  assert.ok(apart(two, [tx, ty]), 'clear of the truck');
  assert.ok(two[1] >= ty - 0.01, 'and never above it, where the number goes');
  // The right was taken, so it stands on the left.
  assert.ok(Math.abs(two[0] - (tx - 36)) < 0.01);
});

// A truck driving past a stop is over it for a moment and gone.
test('a truck driving past moves nothing', () => {
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const [row] = snapshotStops(stops, [], [], 13, [
    { position: [-82.5501, 35.3799], speed: 45 },
  ]).stopData;
  assert.deepEqual([row.markerOffsetX, row.markerOffsetY], [0, 0]);
});

test('a truck nowhere near a stop moves nothing', () => {
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const rows = snapshotStops(stops, [], [], 13, [
    { position: [-80.84, 35.22] },
  ]).stopData;
  assert.equal(rows[0].markerOffsetX, 0);
});
