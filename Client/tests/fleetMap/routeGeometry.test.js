import test from 'node:test';
import assert from 'node:assert/strict';
import { routeGeometry } from '../../Scripts/fleetMap/geometry/routeGeometry.ts';

test('route geometry preserves provider mileage, joined vertices and stop anchors', () => {
  const p = longitude => ({ latitude: 40, longitude });
  const { path, cumulative, anchors } = routeGeometry([
    { miles: 120, points: [p(-80), p(-79.5), p(-79)] },
    { miles: 200, points: [p(-79), p(-78)] },
  ]);
  assert.deepEqual(
    path.map(p => p.lng),
    [-80, -79.5, -79, -78],
  );
  assert.deepEqual(cumulative, [0, 60, 120, 320]);
  assert.deepEqual(anchors, [2, 3]);
});

test('empty and coincident geometry stays finite without invented distance', () => {
  assert.deepEqual(routeGeometry([]), {
    path: [],
    cumulative: [],
    anchors: [],
  });
  const point = { latitude: 40, longitude: -80 };
  assert.deepEqual(
    routeGeometry([{ miles: 10, points: [point, point] }]).cumulative,
    [0, 0],
  );
});
