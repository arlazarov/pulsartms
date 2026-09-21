import test from 'node:test';
import assert from 'node:assert/strict';
import { distanceLabel } from '../../Scripts/fleetMap/ui/distanceLabel.ts';

test('route and station labels share unit conversion and rounding', () => {
  assert.equal(distanceLabel(0), '0 mi · 0 km');
  assert.equal(distanceLabel(10), '10 mi · 16 km');
  assert.equal(distanceLabel(0.6), '1 mi · 1 km');
});

test('single and dual distance units retain grouping and missing values', () => {
  assert.equal(distanceLabel(1877, 'miles'), '1,877 mi');
  assert.equal(distanceLabel(1877, 'kilometers'), '3,021 km');
  assert.equal(distanceLabel(1877, 'both'), '1,877 mi · 3,021 km');
  assert.equal(distanceLabel(0, 'kilometers'), '0 km');
  assert.equal(distanceLabel(10, 'invalid'), '10 mi · 16 km');
  for (const value of [null, NaN, Infinity, -1])
    assert.equal(distanceLabel(value), '—');
});
