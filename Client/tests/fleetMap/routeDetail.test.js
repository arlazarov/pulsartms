import test from 'node:test';
import assert from 'node:assert/strict';
import { routeDetailIndices } from '../../Scripts/fleetMap/geometry/routeDetail.js';

test('zoomed-out geometry preserves endpoints and stop boundaries without changing source', () => {
  const path = Array.from({ length: 10001 }, (_, i) => ({
    lat: 40,
    lng: -80 + i / 10000,
  }));
  assert.deepEqual(routeDetailIndices(path, 5, [5000]), [0, 5000, 10000]);
  assert.equal(path.length, 10001);
  assert.equal(routeDetailIndices(path, 14, [5000]).length, path.length);
});

test('detail preserves significant road bends and handles short routes', () => {
  const path = [
    { lat: 40, lng: -80 },
    { lat: 41, lng: -80 },
    { lat: 41, lng: -79 },
  ];
  assert.deepEqual(routeDetailIndices(path, 5), [0, 1, 2]);
  assert.deepEqual(routeDetailIndices([], 5), []);
  assert.deepEqual(routeDetailIndices(path.slice(0, 1), 5), [0]);
});
