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
