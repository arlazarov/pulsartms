import test from 'node:test';
import assert from 'node:assert/strict';
import { markSharedRoads } from '../../Scripts/fleetMap/routes/sharedRoads.js';

const at = (latitude, longitude) => ({ latitude, longitude });
const road = (loadId, points, role = 'future') => ({ points, role, loadId });
// A real road is described by many points, and two routings of one highway
// never choose the same ones.
const leg = (from, to, count = 40) =>
  Array.from({ length: count }, (_, index) => {
    const along = index / (count - 1);
    return at(
      from.latitude + (to.latitude - from.latitude) * along,
      from.longitude + (to.longitude - from.longitude) * along,
    );
  });

// 11006 had three upcoming loads running the same highway between Charlotte
// and New York. One was drawn over the others, so a load could have both its
// badges and no road at all; moving one aside would have put it on a road it
// never takes.
test('a road two loads share is drawn in neither of their colours', () => {
  const start = at(35.2, -80.8);
  const finish = at(38.9, -77.0);
  const lines = markSharedRoads([
    road('violet', [
      ...leg(at(34.4, -81.6), start),
      ...leg(start, finish, 90),
      ...leg(finish, at(40.7, -74.0)),
    ]),
    road('rose', [
      ...leg(at(36.0, -80.0), start, 55),
      ...leg(start, finish, 70),
      ...leg(finish, at(41.4, -73.2), 55),
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
  }
});

test('a load that runs alone keeps its colour the whole way', () => {
  const lines = markSharedRoads([
    road('violet', leg(at(35.2, -80.8), at(36.5, -79.6))),
    road('rose', leg(at(44.9, -93.0), at(41.8, -87.6))),
  ]);
  assert.equal(lines.length, 2);
  assert.ok(lines.every(line => line.routeShared === false));
});

// Two routings of one highway sample it at different points, and a divided
// road is one road to a truck, so the answer cannot turn on exact equality.
test('the same highway counts as shared though the points differ', () => {
  const from = at(35.2, -80.8);
  const to = at(36.5, -79.6);
  const lines = markSharedRoads([
    road('violet', leg(from, to, 31)),
    road('rose', leg(from, to, 47)),
  ]);
  assert.equal(lines.length, 2, 'one unbroken road apiece, not a dotted seam');
  assert.ok(lines.every(line => line.routeShared === true));
});

// Empty miles are already grey wherever they are, and they are not a load's
// road, so they are left out of the question entirely.
test('empty miles are left alone', () => {
  const path = leg(at(35.2, -80.8), at(36.5, -79.6));
  const empty = road('violet', path, 'deadhead');
  const lines = markSharedRoads([empty, road('rose', path)]);
  assert.equal(lines[0], empty, 'passed through untouched, not split');
  assert.equal(lines[1].routeShared, false, 'and never made a road shared');
});

// The map crawled. Two samplings of one highway agree at their points only
// by accident, so asking point by point cut each road into hundreds of
// pieces, and every piece is a drawn object of its own.
test('one road stays one drawn thing, not hundreds', () => {
  const highway = (count, loadId) =>
    road(loadId, leg(at(35.0, -80.8), at(40.5, -74.0), count));
  const lines = markSharedRoads([
    highway(1200, 'violet'),
    highway(900, 'rose'),
    road('teal', leg(at(44.9, -93.0), at(41.8, -87.6), 600)),
  ]);
  assert.equal(lines.length, 3);
});
