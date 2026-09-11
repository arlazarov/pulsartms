import test from 'node:test';
import assert from 'node:assert/strict';
import { routePosition } from '../../Scripts/fleetMap/geometry/routePosition.js';

const path = [{lat: 0, lng: 0}, {lat: 0, lng: 1}, {lat: 0, lng: 2}];
const cumulative = [0, 10, 20];

test('route position interpolates progress within the selected search window', () => {
  assert.deepEqual(routePosition({latitude: 0, longitude: .5}, path, cumulative, 1, 3), {segment: 1, miles: 5});
  assert.deepEqual(routePosition({latitude: 0, longitude: 1.5}, path, cumulative, 2, 3), {segment: 2, miles: 15});
  assert.equal(routePosition({latitude: 0, longitude: .5}, path, cumulative, 2, 3), null);
});

test('route position rejects distant points and empty search windows', () => {
  assert.equal(routePosition({latitude: 1, longitude: .5}, path, cumulative, 1, 3), null);
  assert.equal(routePosition({latitude: 0, longitude: .5}, path, cumulative, 1, 1), null);
});

test('route position handles duplicate points and selects the first equal match', () => {
  assert.deepEqual(routePosition({latitude: 0, longitude: 0}, [path[0], path[0], path[1]], [0, 0, 10], 1, 3), {segment: 1, miles: 0});
});
