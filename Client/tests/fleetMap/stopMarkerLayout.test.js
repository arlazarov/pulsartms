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
// the next a few miles apart, and their badges were one blot. Coming closer
// parts them, so at the zoom of a whole run they are gathered under a count,
// the way trucks are; close enough to tell the two places apart, each stands
// where it is.
test('stops the camera can part are gathered under a count until it does', () => {
  const stops = [
    { id: 'a', number: '3', position: [-80.84, 35.22] },
    { id: 'b', number: '7', position: [-80.7, 35.28] },
  ];
  const far = snapshotStops(stops, [], [], 5);
  assert.deepEqual(far.stopData, [], 'neither circle stands under the other');
  assert.equal(far.stopClusters.length, 1);
  assert.equal(far.stopClusters[0].count, 2);
  assert.deepEqual(
    far.stopClusters[0].members.map(row => row.id),
    ['a', 'b'],
    'a click goes in to where these part',
  );
  const near = snapshotStops(stops, [], [], 13);
  assert.deepEqual(near.stopClusters, []);
  assert.deepEqual(
    near.stopData.map(row => row.markerOffsetX),
    [0, 0],
  );
});

// A stop the dispatcher has picked is the one thing on the map they are
// looking at; it is never folded away into a count.
test('a picked stop is never folded into a count', () => {
  const stops = [
    { id: 'a', number: '3', position: [-80.84, 35.22], highlighted: true },
    { id: 'b', number: '7', position: [-80.7, 35.28] },
  ];
  const far = snapshotStops(stops, [], [], 5);
  assert.deepEqual(far.stopClusters, []);
  assert.equal(far.stopData.length, 2);
});

// Measured from the first of a group, not from any of it, or a corridor of
// stops chains into one count the length of the road.
test('a corridor of stops gathers in pairs, not into one count for the road', () => {
  const stops = Array.from({ length: 12 }, (_, id) => ({
    id,
    number: String(id + 1),
    position: [-80 + id * 0.01, 40],
  }));
  const { stopClusters } = snapshotStops(stops, [], [], 12);
  assert.ok(stopClusters.length > 1);
  assert.ok(stopClusters.every(cluster => cluster.count <= 3));
});

// 11006 stood on its own delivery and the badge for it was underneath the
// truck, which is the one stop a dispatcher is looking for. It steps aside
// rather than up: above the truck is where its own unit number goes, and
// the two took turns covering each other there.
test('a badge under a parked truck steps aside, and its anchor stays put', () => {
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const trucks = [{ position: [-82.5501, 35.3799] }];
  const clear = snapshotStops(stops, [], [], 13).stopData[0];
  const [row] = snapshotStops(stops, [], [], 13, trucks).stopData;
  assert.equal(row.markerOffsetX, 36, 'aside, where the label is not');
  assert.equal(row.markerOffsetY, clear.markerOffsetY);
  assert.deepEqual(row.position, stops[0].position, 'the stop has not moved');
});

test('a truck nowhere near a stop moves nothing', () => {
  const stops = [{ id: 'a', number: '2', position: [-82.55, 35.38] }];
  const rows = snapshotStops(stops, [], [], 13, [
    { position: [-80.84, 35.22] },
  ]).stopData;
  assert.equal(rows[0].markerOffsetX, 0);
});
