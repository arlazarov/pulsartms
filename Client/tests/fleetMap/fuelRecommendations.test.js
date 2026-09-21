import test from 'node:test';
import assert from 'node:assert/strict';
import { fuelRecommendations } from '../../Scripts/fleetMap/stations/fuelRecommendations.ts';

test('gauges retain server arrival and departure fuel with configured tank capacity and units', () => {
  const plan = {
    tankGallons: 200,
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          buyGallons: 60,
          arrivalGallons: 40,
          departureGallons: 100,
          unit: 'L',
          milesAhead: 10,
        },
      ],
    },
  };
  const result = fuelRecommendations(plan, null);
  assert.equal(result.stops[0].arrivalGallons, 40);
  assert.equal(result.stops[0].departureGallons, 100);
  assert.equal(result.stops[0].tankGallons, 200);
  assert.equal(result.stops[0].unit, 'L');
  plan.fuelPlan.stops[0].arrivalGallons = 38;
  assert.notEqual(result.key, fuelRecommendations(plan, null).key);
});

test('each visit forwards its server-computed USD purchase cost and changes invalidate the popup key', () => {
  const stop = {
    stationId: 'a',
    buyGallons: 60,
    yourPrice: 9,
    purchaseCostUsd: 287.64,
    unit: 'L',
  };
  const plan = { fuelPlan: { stops: [stop] } };
  const result = fuelRecommendations(plan, null);
  assert.equal(result.stops[0].visits[0].purchaseCostUsd, 287.64);
  stop.purchaseCostUsd = 289.5;
  assert.notEqual(result.key, fuelRecommendations(plan, null).key);
  delete stop.purchaseCostUsd;
  assert.equal(
    fuelRecommendations(plan, null).stops[0].purchaseCostUsd,
    null,
    'legacy missing costs must not be calculated from client prices',
  );
});

test('future visits use server miles even when not on the current route', () => {
  const plan = {
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          visitKey: 'future-a',
          buyGallons: 30,
          currentRouteMile: null,
          milesAhead: 140,
        },
      ],
    },
  };
  const result = fuelRecommendations(plan, { progressMiles: 300 });
  assert.equal(result.stops[0].miles, 140);
  plan.fuelPlan.stops[0].milesAhead = 135;
  assert.notEqual(
    fuelRecommendations(plan, { progressMiles: 300 }).key,
    result.key,
  );
});

test('each physical station groups numbered visits and exposes its nearest upcoming purchase', () => {
  const plan = {
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          number: 2,
          visitKey: 'later',
          buyGallons: 50,
          milesAhead: 200,
        },
        {
          stationId: 'a',
          number: 1,
          visitKey: 'next',
          buyGallons: 20,
          currentRouteMile: 100,
          milesAhead: 10,
        },
        {
          stationId: 'b',
          visitKey: 'passed',
          buyGallons: 20,
          currentRouteMile: 70,
          milesAhead: -20,
        },
      ],
    },
  };
  const result = fuelRecommendations(plan, { progressMiles: 90 });
  assert.equal(result.stops.length, 1);
  assert.equal(result.stops[0].visitKey, 'next');
  assert.equal(result.stops[0].gallons, 20);
  assert.equal(result.stops[0].numbers, '1/2');
  assert.deepEqual(
    result.stops[0].visits.map(x => x.gallons),
    [20, 50],
  );
  const passed = fuelRecommendations(plan, { progressMiles: 101 });
  assert.equal(passed.stops[0].visitKey, 'later');
  assert.equal(passed.stops[0].miles, 200);
  assert.equal(passed.stops[0].numbers, '2');
  assert.notEqual(passed.key, result.key);
});

test('a changed return purchase invalidates the group key without changing its first visit', () => {
  const plan = {
    fuelPlan: {
      stops: [
        { stationId: 'a', number: 1, buyGallons: 20, milesAhead: 10 },
        { stationId: 'b', number: 2, buyGallons: 40, milesAhead: 100 },
        { stationId: 'a', number: 3, buyGallons: 50, milesAhead: 200 },
      ],
    },
  };
  const before = fuelRecommendations(plan, null);
  assert.deepEqual(
    before.stops.map(x => x.numbers),
    ['1/3', '2'],
  );
  plan.fuelPlan.stops[2].buyGallons = 60;
  assert.notEqual(before.key, fuelRecommendations(plan, null).key);
});

test('invalidated plans and non-purchases never show actionable recommendations', () => {
  const plan = {
    fuelPlan: {
      needsRefresh: true,
      stops: [{ stationId: 'a', buyGallons: 40, milesAhead: 10 }],
    },
  };
  assert.deepEqual(fuelRecommendations(plan, null), { key: '', stops: [] });
  plan.fuelPlan.needsRefresh = false;
  plan.fuelPlan.stops[0].buyGallons = 0;
  assert.deepEqual(fuelRecommendations(plan, null).stops, []);
});

test('planned stations carry their saved location and quote without requiring the all-stations layer', () => {
  const stop = {
    stationId: 'a',
    buyGallons: 116,
    point: { latitude: 40, longitude: -79 },
    name: 'Planned station',
    address: '10 Truck Road',
    yourPrice: 3.5,
    currency: 'USD',
    unit: 'US gal',
  };
  const result = fuelRecommendations({ fuelPlan: { stops: [stop] } }, null);
  for (const key of [
    'point',
    'name',
    'address',
    'yourPrice',
    'currency',
    'unit',
  ])
    assert.deepEqual(result.stops[0][key], stop[key]);
});

test('Next loads off filters visits before grouping the same station and on restores the original plan', () => {
  const plan = {
    dispatchId: 'current',
    tankGallons: 200,
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          dispatchId: 'current',
          number: 1,
          visitKey: 'current-a',
          buyGallons: 20,
          arrivalGallons: 40,
          departureGallons: 60,
          unit: 'gal',
          currentRouteMile: null,
          milesAhead: 10,
        },
        {
          stationId: 'a',
          dispatchId: 'next',
          number: 2,
          visitKey: 'return-a',
          buyGallons: 100,
          arrivalGallons: 100,
          departureGallons: 200,
          unit: 'gal',
          fillToTarget: true,
          currentRouteMile: null,
          milesAhead: 300,
        },
        {
          stationId: 'b',
          dispatchId: 'current',
          number: 3,
          visitKey: 'current-b',
          buyGallons: 40,
          milesAhead: 90,
        },
      ],
    },
  };
  const original = structuredClone(plan);
  const all = fuelRecommendations(plan, null);
  assert.deepEqual(all, fuelRecommendations(plan, null, true));
  assert.deepEqual(
    all.stops.map(stop => stop.numbers),
    ['1/2', '3'],
  );
  const current = fuelRecommendations(plan, null, false);
  assert.deepEqual(
    current.stops.map(stop => stop.numbers),
    ['1', '3'],
  );
  assert.deepEqual(current.stops[0].visits, [all.stops[0].visits[0]]);
  assert.deepEqual(current.stops[1], all.stops[1]);
  assert.notEqual(current.key, all.key);
  assert.deepEqual(fuelRecommendations(plan, null, true), all);
  assert.deepEqual(plan, original, 'visibility does not mutate the saved plan');
});

test('a current estimated visit with null current route mile remains visible while Next loads is off', () => {
  const plan = {
    dispatchId: 'current',
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          dispatchId: 'current',
          buyGallons: 30,
          currentRouteMile: null,
          milesAhead: 25,
        },
      ],
    },
  };
  const result = fuelRecommendations(plan, { progressMiles: 500 }, false);
  assert.equal(result.stops.length, 1);
  assert.equal(result.stops[0].miles, 25);
  assert.equal(result.stops[0].gallons, 30);
});

test('legacy ownership matches before-stop IDs from both reference and remaining current stops', () => {
  const plan = {
    dispatchId: 'current',
    referenceStops: [{ id: 'reference' }],
    stops: [{ id: 'remaining' }],
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          beforeStopId: 'reference',
          buyGallons: 20,
          currentRouteMile: null,
          milesAhead: 10,
        },
        {
          stationId: 'b',
          beforeStopId: 'remaining',
          buyGallons: 30,
          currentRouteMile: null,
          milesAhead: 20,
        },
        {
          stationId: 'c',
          beforeStopId: 'future',
          buyGallons: 40,
          currentRouteMile: 100,
          milesAhead: 30,
        },
      ],
    },
  };
  assert.deepEqual(
    fuelRecommendations(plan, null, false).stops.map(stop => stop.id),
    ['a', 'b'],
  );
  assert.equal(fuelRecommendations(plan, null, true).stops.length, 3);
});

test('explicit dispatch ownership overrides conflicting current stop or mileage hints', () => {
  const plan = {
    dispatchId: 'current',
    stops: [{ id: 'current-stop' }],
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          dispatchId: 'future',
          beforeStopId: 'current-stop',
          buyGallons: 20,
          currentRouteMile: 100,
          milesAhead: 10,
        },
        {
          stationId: 'b',
          dispatchId: 'current',
          beforeStopId: 'future-stop',
          buyGallons: 30,
          currentRouteMile: null,
          milesAhead: 20,
        },
      ],
    },
  };
  assert.deepEqual(
    fuelRecommendations(plan, null, false).stops.map(stop => stop.id),
    ['b'],
  );
});

test('unknown ownership is hidden off without treating a current route mile as ownership', () => {
  const plan = {
    dispatchId: 'current',
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          buyGallons: 20,
          currentRouteMile: 100,
          milesAhead: 10,
        },
        {
          stationId: 'b',
          buyGallons: 30,
          currentRouteMile: null,
          milesAhead: 20,
        },
      ],
    },
  };
  assert.deepEqual(fuelRecommendations(plan, null, false).stops, []);
  assert.equal(fuelRecommendations(plan, null).stops.length, 2);
});

test('empty legacy dispatch IDs require a known before-stop and cannot match each other', () => {
  const empty = '00000000-0000-0000-0000-000000000000';
  const plan = {
    dispatchId: empty,
    stops: [{ id: 'current-stop' }],
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          dispatchId: empty,
          beforeStopId: 'current-stop',
          buyGallons: 20,
          milesAhead: 10,
        },
        { stationId: 'b', dispatchId: empty, buyGallons: 30, milesAhead: 20 },
        {
          stationId: 'c',
          dispatchId: 'future',
          beforeStopId: 'current-stop',
          buyGallons: 40,
          milesAhead: 30,
        },
      ],
    },
  };
  assert.deepEqual(
    fuelRecommendations(plan, null, false).stops.map(stop => stop.id),
    ['a'],
  );
});

test('filtering an earlier future visit preserves fallback numbering of a current visit', () => {
  const plan = {
    dispatchId: 'current',
    fuelPlan: {
      stops: [
        {
          stationId: 'a',
          dispatchId: 'future',
          buyGallons: 20,
          milesAhead: 200,
        },
        {
          stationId: 'b',
          dispatchId: 'current',
          buyGallons: 30,
          milesAhead: 10,
        },
      ],
    },
  };
  assert.equal(fuelRecommendations(plan, null, false).stops[0].numbers, '2');
});

test('unreachable station remains visible without a fictional fuel purchase', () => {
  const result = fuelRecommendations(
    {
      dispatchId: 'load',
      stops: [],
      fuelRecommendations: {
        accessProblem: true,
        stations: [
          {
            stationId: 'unreachable',
            routeMile: 90,
            milesAhead: 90,
            shortfallGallons: 3.2,
            preferredReserveGallons: 25,
          },
        ],
      },
    },
    { progressMiles: 0 },
  );
  assert.equal(result.stops.length, 1);
  assert.equal(result.stops[0].accessOnly, true);
  assert.equal(result.stops[0].gallons, 0);
  assert.equal(result.stops[0].purchaseCostUsd, null);
  assert.match(result.stops[0].warning, /3.2 US gal short/);
});
