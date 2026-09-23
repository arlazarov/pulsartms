import test from 'node:test';
import assert from 'node:assert/strict';
import {
  createFuelVisit,
  fuelGaugeValue,
  plannedPrice,
} from '../../Scripts/fleetMap/stations/stationFuelVisit.ts';

test('planned prices retain the station arrival day and distinguish fallback quotes', () => {
  const visit = {
    yourPrice: 3.25,
    currency: 'USD',
    unit: 'US gal',
    priceDate: '2026-09-14',
    estimatedArrival: '2026-09-15T00:30:00-07:00',
    priceEstimated: true,
  };
  // A named figure, the way every other fact on these cards is written,
  // rather than one sentence about itself.
  assert.deepEqual(plannedPrice(visit), {
    label: 'Estimated price',
    value: '3.250 USD/US gal',
    note: 'quoted 2026-09-14',
  });
  assert.deepEqual(plannedPrice({ ...visit, priceEstimated: false }), {
    label: 'Arrival price',
    value: '3.250 USD/US gal',
    note: 'on 2026-09-15',
  });
  assert.equal(plannedPrice({}), null);
});

test('fuel gauges use physical tank capacity rather than the fill target', () => {
  assert.equal(fuelGaugeValue(180, 200), 90);
  assert.equal(fuelGaugeValue(40, 200), 20);
  assert.equal(fuelGaugeValue(0, 200), 0);
  assert.equal(fuelGaugeValue(200, 200), 100);
});

test('missing or invalid gauge inputs never invent an empty or full tank', () => {
  for (const [gallons, capacity] of [
    [null, 200],
    [40, null],
    [40, 0],
    [-1, 200],
    [201, 200],
    [NaN, 200],
    [40, Infinity],
    [undefined, undefined],
  ])
    assert.equal(fuelGaugeValue(gallons, capacity), null);
});

test('unreachable visit shows its deficit instead of purchase or fuel gauges', t => {
  const previous = globalThis.document;
  globalThis.document = {
    createElement: () => ({
      children: [],
      append(...children) {
        this.children.push(...children);
      },
      setAttribute() {},
    }),
  };
  t.after(() => {
    globalThis.document = previous;
  });
  const row = createFuelVisit(
    {
      number: 1,
      miles: 90,
      accessOnly: true,
      warning: 'Cannot reach: 3.2 US gal short.',
    },
    { country: 'US' },
    { unit: 'US gal' },
  );
  assert.equal(row.children.length, 2);
  assert.equal(row.children[1].textContent, 'Cannot reach: 3.2 US gal short.');
  assert.ok(row.children.every(child => !child.className.includes('__levels')));
});

// One word beside the stop's name, only when it is true.
test('a visit handed to the driver says so, and says when the plan moved since', t => {
  const previous = globalThis.document;
  globalThis.document = {
    createElement: () => ({
      children: [],
      append(...children) {
        this.children.push(...children);
      },
      setAttribute() {},
    }),
  };
  t.after(() => {
    globalThis.document = previous;
  });
  const badge = sent => {
    const row = createFuelVisit(
      { number: 1, miles: 40, accessOnly: true, sent },
      { country: 'US' },
      { unit: 'US gal' },
    );
    const name = row.children[0].children[0];
    return name.children
      .filter(child => child.className.includes('__sent'))
      .map(child => [child.textContent, child.className]);
  };
  assert.deepEqual(badge(undefined), []);
  assert.deepEqual(badge(null), []);
  assert.deepEqual(badge({ changed: false }), [
    ['Sent', 'fleet-fuel-visit__sent'],
  ]);
  assert.deepEqual(badge({ changed: true }), [
    [
      'Changed since sent',
      'fleet-fuel-visit__sent fleet-fuel-visit__sent--changed',
    ],
  ]);
  // Accepted is still only sent; the provider's later word is shown as
  // said, and a changed plan outranks any delivery.
  assert.deepEqual(badge({ changed: false, delivery: 'accepted' }), [
    ['Sent', 'fleet-fuel-visit__sent'],
  ]);
  assert.deepEqual(badge({ changed: false, delivery: 'read' }), [
    ['Read', 'fleet-fuel-visit__sent'],
  ]);
  assert.deepEqual(badge({ changed: false, delivery: 'failed' }), [
    ['Not delivered', 'fleet-fuel-visit__sent fleet-fuel-visit__sent--changed'],
  ]);
  assert.deepEqual(badge({ changed: true, delivery: 'read' }), [
    [
      'Changed since sent',
      'fleet-fuel-visit__sent fleet-fuel-visit__sent--changed',
    ],
  ]);
});
