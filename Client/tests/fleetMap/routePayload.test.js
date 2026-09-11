import test from 'node:test';
import assert from 'node:assert/strict';
import { mergeRoutePayload } from '../../Scripts/fleetMap/routes/routePayload.js';

test('metadata refresh preserves only matching geometry and replaces nullable metadata', () => {
  const full = { id: 'plan', dispatchId: 'current', truckId: 'truck', version: 2, stops: [{ id: 'old' }],
    route: { legs: [{ points: [{ latitude: 40, longitude: -80 }] }] },
    referenceRoute: { legs: [] }, referenceStops: [{ id: 'old-reference' }], fuelPlan: { stops: [] } };
  const payload = { id: 'plan', dispatchId: 'current', truckId: 'truck', version: 2, geometryOmitted: true,
    stops: [{ id: 'new' }], referenceStops: null, fuelPlan: null };
  const merged = mergeRoutePayload(full, payload);
  assert.ok(merged.accepted);
  assert.equal(merged.plan.route, full.route);
  assert.equal(merged.plan.referenceRoute, full.referenceRoute);
  assert.equal(merged.plan.stops, payload.stops);
  assert.equal(merged.plan.referenceStops, null);
  assert.equal(merged.plan.fuelPlan, null);
  assert.equal(merged.plan.geometryOmitted, false);
  assert.equal(merged.plan.dispatchId, payload.dispatchId);
  assert.equal(full.stops[0].id, 'old', 'input state is not mutated');
  for (const change of [{ id: 'other' }, { version: 3 }, { truckId: 'other' }])
    assert.deepEqual(mergeRoutePayload(full, { ...payload, ...change }), { accepted: false });
  assert.deepEqual(mergeRoutePayload(null, payload), { accepted: false });
  assert.deepEqual(mergeRoutePayload(full, null), { accepted: true, plan: null });
});
