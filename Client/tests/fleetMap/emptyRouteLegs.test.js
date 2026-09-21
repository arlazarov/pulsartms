import test from 'node:test';
import assert from 'node:assert/strict';
import { emptyRouteLegs } from '../../Scripts/fleetMap/routes/emptyRouteLegs.ts';

test('renders server movement state without inferring cargo from stop labels', () => {
  const plan = {
    fromCurrentPosition: true,
    stops: [{ id: 'pu', job: 'Pick Up', stateAfter: 'Empty' }],
    route: { legs: [{}, {}, {}, {}] },
    segments: ['Loaded', 'Empty', 'Bobtail', 'Unknown'].map(cargoState => ({
      cargoState,
    })),
  };
  assert.deepEqual(emptyRouteLegs(plan), [false, true, true, false]);
  delete plan.segments;
  assert.deepEqual(emptyRouteLegs(plan), [false, false, false, false]);
});
