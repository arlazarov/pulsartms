import test from 'node:test';
import assert from 'node:assert/strict';
import { distanceLabel } from '../../Scripts/fleetMap/ui/distanceLabel.js';

test('route and station labels share unit conversion and rounding', () => {
  assert.equal(distanceLabel(0), '0 mi · 0 km');
  assert.equal(distanceLabel(10), '10 mi · 16 km');
  assert.equal(distanceLabel(.6), '1 mi · 1 km');
});
