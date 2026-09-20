import test from 'node:test';
import assert from 'node:assert/strict';
import { stationQuantity } from '../../Scripts/fleetMap/stations/stationQuantity.js';
test('station quantity uses country and converts US gallons to liters for Canada', () => {
  assert.equal(stationQuantity(50, { country: 'Canada' }), '190 L');
  assert.equal(stationQuantity(50, { country: 'CA' }), '190 L');
  assert.equal(
    stationQuantity(50, { country: 'US', region: 'CA' }),
    '50 US gal',
  );
  assert.equal(stationQuantity(null, { country: 'CA' }), '');
});

test('quoted native volume unit takes precedence over missing or incorrect country', () => {
  assert.equal(stationQuantity(50, { country: '' }, 'L'), '190 L');
  assert.equal(stationQuantity(50, { country: 'US' }, 'litre'), '190 L');
  assert.equal(stationQuantity(50, { country: 'CA' }, 'gal'), '50 US gal');
  assert.equal(stationQuantity(50.4, {}, 'gal', Math.round), '50 US gal');
});

test('full refill hides quantities in both countries', async () => {
  const { stationPurchase } = await import(
    '../../Scripts/fleetMap/stations/stationQuantity.js'
  );
  assert.equal(
    stationPurchase({ gallons: 86, full: true }, { country: 'US' }),
    'Fill up',
  );
  assert.equal(
    stationPurchase({ gallons: 50, full: true }, { country: 'CA' }),
    'Fill up',
  );
  assert.equal(
    stationPurchase({ gallons: 50, full: false }, { country: 'US' }),
    'Buy 50 US gal',
  );
});

// The map badge is read at a glance from a distance, so it says one thing:
// which stop of the fuel plan this is. How much is bought there rode along
// for a while and made the badge a sentence across the map.
test('a planned stop badge names its place in the order and nothing else', async () => {
  const { fuelVisitLabel } = await import(
    '../../Scripts/fleetMap/stations/stationQuantity.js'
  );
  assert.equal(fuelVisitLabel({ numbers: '2', gallons: 119.6 }), '2');
  assert.equal(fuelVisitLabel({ numbers: '1/3', gallons: 50 }), '1/3');
  // A station with nothing planned carries no badge at all, rather than an
  // empty one.
  assert.equal(fuelVisitLabel({ numbers: '5' }), '5');
  assert.equal(fuelVisitLabel({ gallons: 40 }), undefined);
  assert.equal(fuelVisitLabel(null), undefined);
});
