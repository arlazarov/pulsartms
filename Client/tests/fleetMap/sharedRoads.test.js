import test from 'node:test';
import assert from 'node:assert/strict';
import { markSharedRoads } from '../../Scripts/fleetMap/routes/sharedRoads.js';

const at = (latitude, longitude) => ({ latitude, longitude });
const road = (loadId, points, role = 'future') => ({ points, role, loadId });

// 11006 had three upcoming loads running the same highway between Charlotte
// and New York. One was drawn over the others, so a load could have both its
// badges and no road at all; moving one aside would have put it on a road it
// never takes.
test('a road two loads share is drawn in neither of their colours', () => {
  const corridor = [at(35.2, -80.8), at(36.5, -79.6), at(38.9, -77.0)];
  const lines = markSharedRoads([
    road('violet', [
      at(35.0, -81.0),
      at(35.1, -80.9),
      ...corridor,
      at(40.6, -74.1),
      at(40.7, -74.0),
    ]),
    road('rose', [
      at(35.4, -80.6),
      at(35.3, -80.7),
      ...corridor,
      at(40.9, -73.8),
      at(40.8, -73.9),
    ]),
  ]);
  for (const loadId of ['violet', 'rose']) {
    const own = lines.filter(line => line.loadId === loadId);
    assert.deepEqual(
      own.map(line => line.routeShared),
      [false, true, false],
      'its own road either side of the stretch it shares',
    );
    // The road is continuous: each run begins where the last one ended.
    for (let i = 1; i < own.length; i++)
      assert.deepEqual(own[i].points[0], own[i - 1].points.at(-1));
    assert.deepEqual(own.flatMap(line => line.points).at(-1), {
      latitude: loadId === 'violet' ? 40.7 : 40.8,
      longitude: loadId === 'violet' ? -74.0 : -73.9,
    });
  }
});

test('a load that runs alone keeps its colour the whole way', () => {
  const lines = markSharedRoads([
    road('violet', [at(35.2, -80.8), at(36.5, -79.6)]),
    road('rose', [at(44.9, -93.0), at(41.8, -87.6)]),
  ]);
  assert.equal(lines.length, 2);
  assert.ok(lines.every(line => line.routeShared === false));
});

// Two routings of one highway sample it at different points, and a divided
// road is one road to a truck, so the answer cannot turn on exact equality.
test('the same highway counts as shared though the points differ', () => {
  const lines = markSharedRoads([
    road('violet', [at(35.2001, -80.8002), at(36.5001, -79.6001)]),
    road('rose', [at(35.2004, -80.7998), at(36.4996, -79.6003)]),
  ]);
  assert.ok(lines.every(line => line.routeShared === true));
});

// Empty miles are already grey wherever they are, and they are not a load's
// road, so they are left out of the question entirely.
test('empty miles are left alone', () => {
  const empty = road('violet', [at(35.2, -80.8), at(36.5, -79.6)], 'deadhead');
  const lines = markSharedRoads([
    empty,
    road('rose', [at(35.2, -80.8), at(36.5, -79.6)]),
  ]);
  assert.equal(lines[0], empty, 'passed through untouched, not split');
  assert.equal(lines[1].routeShared, false, 'and never made a road shared');
});
