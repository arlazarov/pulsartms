import test from 'node:test';
import assert from 'node:assert/strict';
import { fuelGaugeValue } from '../../Scripts/fleetMap/stations/stationFuelVisit.js';

test('fuel gauges use physical tank capacity rather than the fill target', () => {
  assert.equal(fuelGaugeValue(180, 200), 90);
  assert.equal(fuelGaugeValue(40, 200), 20);
  assert.equal(fuelGaugeValue(0, 200), 0);
  assert.equal(fuelGaugeValue(200, 200), 100);
});

test('missing or invalid gauge inputs never invent an empty or full tank', () => {
  for (const [gallons, capacity] of [[null, 200], [40, null], [40, 0], [-1, 200],
    [201, 200], [NaN, 200], [40, Infinity], [undefined, undefined]])
    assert.equal(fuelGaugeValue(gallons, capacity), null);
});
