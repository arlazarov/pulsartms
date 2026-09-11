import test from 'node:test';
import assert from 'node:assert/strict';
import { stopMarkerLabel } from '../../Scripts/fleetMap/rendering/stopMarkerLayout.js';
import { snapshotStops } from '../../Scripts/fleetMap/rendering/stopData.js';

test('compact markers show only ordered numbers while stop type remains in the card', () => {
  for (const job of ['Pickup', 'Pick Up', 'pick_up']) assert.equal(stopMarkerLabel(job, '2/4'), '2/4');
  for (const job of ['Delivery', 'Drop Off', 'drop_off']) assert.equal(stopMarkerLabel(job, '3'), '3');
  assert.equal(stopMarkerLabel(null, '5'), '5');
  assert.equal(stopMarkerLabel('Delivery', null), '');
});

test('coincident current and future badges separate while their geographic anchors stay exact', () => {
  const stops = [
    { id: 1, position: [-80, 40], number: '1', job: 'Delivery' },
    { id: 2, position: [-80.0001, 40], number: '2', job: 'Pickup', transientLabel: true },
    { id: 3, position: [-79, 40], number: '3', job: 'Delivery', transientLabel: true },
  ];
  const first = snapshotStops(stops);
  const byId = new Map(first.stopData.map(row => [row.id, row]));
  assert.ok(byId.get(1).markerOffsetX < 0);
  assert.ok(byId.get(2).markerOffsetX > 0);
  assert.equal(byId.get(3).markerOffsetX, 0);
  for (const stop of stops) assert.equal(byId.get(stop.id).position, stop.position);
  stops[1].highlighted = true;
  const selected = snapshotStops(stops, first.stopData);
  for (const row of selected.stopData) {
    assert.equal(row.markerOffsetX, byId.get(row.id).markerOffsetX, 'selection does not shuffle badges');
    assert.equal(row.markerOffsetY, byId.get(row.id).markerOffsetY);
  }
  assert.equal(snapshotStops(stops, selected.stopData).stopData, selected.stopData, 'unchanged polling reuses data');
  stops[1].visible = false;
  const hidden = snapshotStops(stops, selected.stopData);
  assert.equal(hidden.stopData.find(row => row.id === 1).markerOffsetX, 0, 'hiding next loads restores the centered badge');
});

test('coincident visits keep individual circles evenly spaced with the final odd circle centered', () => {
  const stops = Array.from({ length: 5 }, (_, id) => ({ id, position: [-80, 40],
    number: String(id + 8), job: id % 2 ? 'Pickup' : 'Delivery' }));
  const rows = snapshotStops(stops).stopData;
  assert.deepEqual(rows.map(row => row.markerLabel), ['8', '9', '10', '11', '12']);
  assert.equal(new Set(rows.map(row => `${row.markerOffsetX},${row.markerOffsetY}`)).size, 5);
  assert.equal(rows[1].markerOffsetX - rows[0].markerOffsetX, 40, 'digit count never changes circle spacing');
  assert.equal(rows[2].markerOffsetY, rows[0].markerOffsetY - 40);
  assert.equal(rows[4].markerOffsetX, 0);
});
