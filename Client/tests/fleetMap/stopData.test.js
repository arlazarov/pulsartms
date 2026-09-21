import test from 'node:test';
import assert from 'node:assert/strict';
import { snapshotStops } from '../../Scripts/fleetMap/rendering/stopData.ts';

test('current stops cover future stops until a future stop is highlighted', () => {
  const current = { position: [1, 2], number: '1' };
  const next = { position: [1, 2], number: '2', transientLabel: true };
  const stops = new Set([current, next]);
  assert.deepEqual(
    snapshotStops(stops).stopData.map(s => s.number),
    ['2', '1'],
  );
  next.highlighted = true;
  assert.deepEqual(
    snapshotStops(stops).stopData.map(s => s.number),
    ['1', '2'],
  );
  next.highlighted = false;
  assert.deepEqual(
    snapshotStops(stops).stopData.map(s => s.number),
    ['2', '1'],
  );
});

test('hovered next-load label preserves current distance labels without rebuilding stop geometry', () => {
  const current = {
    position: [1, 2],
    number: '1',
    distance: 'Current distance',
  };
  const next = {
    position: [1, 2],
    number: '4',
    distance: null,
    transientLabel: true,
    onHover() {},
  };
  const stops = new Set([current, next]);
  const first = snapshotStops(stops);
  next.distance = 'Next load';
  const hovered = snapshotStops(stops, first.stopData, first.distanceData);
  assert.equal(hovered.stopData, first.stopData);
  assert.deepEqual(
    hovered.distanceData.map(x => x.text),
    ['Current distance', 'Next load'],
  );
  assert.equal(hovered.distanceData[1].transient, true);
  next.distance = null;
  assert.deepEqual(
    snapshotStops(stops).distanceData.map(x => x.text),
    ['Current distance'],
  );
});
