import test from 'node:test';
import assert from 'node:assert/strict';
import {
  corridorBudget,
  stationsFarFromRoads,
} from '../../Scripts/fleetMap/rendering/stationCorridor.ts';

// Every station the company can buy at is on the map, and at an overview
// they were the map. The ones along the plan are told from the rest by a
// distance on the ground; with no road drawn there is nothing to be far
// from.
const road = (role, ...points) => ({
  routeRole: role,
  path: points.map(([lng, lat]) => ({ lng, lat })),
});
const station = (lng, lat) => ({ position: [lng, lat], color: [0, 0, 0] });

test('a station within reach of a road the truck will drive is not far', () => {
  const onRoad = station(-80.0, 36.0);
  const beside = station(-80.1, 36.05);
  const away = station(-81.5, 36.0);
  const far = stationsFarFromRoads(
    [onRoad, beside, away],
    [road('current', [-80.5, 36], [-79.5, 36])],
  );
  assert.equal(far.has(onRoad), false);
  assert.equal(far.has(beside), false);
  assert.equal(far.has(away), true, 'ninety miles off the road');
  assert.ok(far.has(away) && [...far][0] === away, "the scene's own mark");
});

test('upcoming and empty roads count, the road behind the truck does not', () => {
  const s = station(-80.0, 36.0);
  for (const role of ['future', 'deadhead', 'current-empty'])
    assert.equal(
      stationsFarFromRoads([s], [road(role, [-80.5, 36], [-79.5, 36])]).has(s),
      false,
      role,
    );
  // Beside the road behind the truck, and ninety miles from the one ahead.
  for (const role of ['traveled', 'traveled-empty', 'preview'])
    assert.equal(
      stationsFarFromRoads(
        [s],
        [
          road(role, [-80.5, 36], [-79.5, 36]),
          road('current', [-82, 36], [-81.5, 36]),
        ],
      ).has(s),
      true,
      role,
    );
});

test('with no road drawn no station is far', () => {
  const s = station(-80.0, 36.0);
  assert.equal(stationsFarFromRoads([s], []).size, 0);
  // The renderer's own [lng, lat] points are read the same way.
  assert.equal(
    stationsFarFromRoads([s], [{ routeRole: 'current', path: [[-80, 36]] }])
      .size,
    0,
  );
  assert.equal(stationsFarFromRoads([s], [{ routeRole: 'current' }]).size, 0);
});

// Longitude shrinks with latitude. Searched as many cells to the side as
// to the north, a station at sixty degrees north stood two raw cells from
// the road and was hidden though the ground between was well inside reach.
test('the search widens with latitude, so a near station stays near far north', () => {
  const s = station(-80.11, 60);
  const far = stationsFarFromRoads(
    [s],
    [road('current', [-79.8, 59.95], [-79.8, 60.05])],
  );
  assert.equal(far.has(s), false, '0.155 degrees on the ground, in reach');
  // The same raw separation at the equator is the full 0.31 degrees: far.
  const e = station(-80.11, 0);
  assert.equal(
    stationsFarFromRoads(
      [e],
      [road('current', [-79.8, -0.05], [-79.8, 0.05])],
    ).has(e),
    true,
  );
});

// Reach is measured to the stretch itself, so a station beside the middle
// of a long straight run is as near as one beside its end.
test('reach is to the stretch, not to its ends', () => {
  const s = station(-80.0, 36.17);
  assert.equal(
    stationsFarFromRoads([s], [road('current', [-82, 36], [-78, 36])]).has(s),
    false,
    'two miles past a point 0.17 degrees south of the road',
  );
  const off = station(-80.0, 36.19);
  assert.equal(
    stationsFarFromRoads([off], [road('current', [-82, 36], [-78, 36])]).has(
      off,
    ),
    true,
  );
});

// A road is filed by its length, not by the rectangle it spans, and a
// road that is not made of places, or is too long to file within the
// budget, decides nothing: every station is drawn in full.
test('a long diagonal costs its length, and past the budget nothing is hidden', () => {
  const beside = station(-100.0, 40.1);
  const away = station(-60.0, 20.0);
  // Coast to coast diagonally: some four hundred cells, not a hundred
  // thousand.
  const start = performance.now();
  const far = stationsFarFromRoads(
    [beside, away],
    [road('current', [-125, 25], [-70, 48])],
  );
  assert.ok(performance.now() - start < 200);
  assert.equal(far.has(away), true);
  // Beside the line between the two, on the ground.
  const on = station(-100, 25 + ((48 - 25) * 25) / 55 + 0.1);
  assert.equal(
    stationsFarFromRoads([on], [road('current', [-125, 25], [-70, 48])]).has(
      on,
    ),
    false,
  );
  assert.equal(
    stationsFarFromRoads([away], [road('current', [-125, 25], [-70, 48])], 100)
      .size,
    0,
    'past the budget no station is hidden',
  );
  assert.ok(corridorBudget >= 100000);
});

test('a point that is not a place breaks the road there and hides nothing', () => {
  const s = station(-80.0, 36.0);
  // With another road filed, the broken one decides nothing about s.
  for (const bad of [
    [Infinity, 36],
    [NaN, 36],
    [-80, 95],
    [200, 36],
  ])
    assert.equal(
      stationsFarFromRoads(
        [s],
        [
          road('current', [-82, 36], bad, [-78, 36]),
          road('current', [-90, 30], [-89, 30]),
        ],
      ).has(s),
      true,
      `${bad}: the stretches through it are not filed`,
    );
  assert.equal(
    stationsFarFromRoads(
      [s],
      [road('current', [-82, 36], [-80.1, 36], [Infinity, 36])],
    ).has(s),
    false,
    'the stretch before it still counts',
  );
  const nowhere = station(NaN, 36);
  assert.equal(
    stationsFarFromRoads(
      [nowhere],
      [road('current', [-82, 36], [-78, 36])],
    ).has(nowhere),
    false,
  );
});
