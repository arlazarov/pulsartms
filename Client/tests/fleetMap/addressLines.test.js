import test from 'node:test';
import assert from 'node:assert/strict';
import { addressLines } from '../../Scripts/fleetMap/ui/addressLines.js';

test('stop addresses separate the street from the city, region, postal code and country', () => {
  assert.deepEqual(addressLines('530 Henry St, Rome, NY, 13440, US'),
    { street: '530 Henry St', locality: 'Rome, NY 13440, US' });
  assert.deepEqual(addressLines('530 Henry St, Rome, NY 13440, US'),
    { street: '530 Henry St', locality: 'Rome, NY 13440, US' });
  assert.deepEqual(addressLines('308 Springhill Farm Rd, building 3, Fort Mill, SC, 29715, US'),
    { street: '308 Springhill Farm Rd, building 3', locality: 'Fort Mill, SC 29715, US' });
  assert.deepEqual(addressLines('100 Main St, Unit 4, Toronto, ON, M5V 2T6, Canada'),
    { street: '100 Main St, Unit 4', locality: 'Toronto, ON M5V 2T6, Canada' });
  assert.deepEqual(addressLines('100 Main St, Los Angeles, CA'),
    { street: '100 Main St', locality: 'Los Angeles, CA' });
  assert.deepEqual(addressLines('100 Main St, Toronto, ON M5V 2T6, CA'),
    { street: '100 Main St', locality: 'Toronto, ON M5V 2T6, CA' });
});

test('unstructured or incomplete addresses are preserved rather than inventing a street or locality', () => {
  for (const value of ['Warehouse entrance', 'Rome, NY, 13440, US', '100 Main St, Toronto',
    '100 Main St, Building 3, SC, 29715, US', '100 Main St, Rome, XX, 13440, US',
    'Toronto, ON M5V3A8, CA', '<script>unsafe</script>'])
    assert.deepEqual(addressLines(value), { street: value, locality: '' });
  assert.deepEqual(addressLines(null), { street: '', locality: '' });
  assert.deepEqual(addressLines('  '), { street: '', locality: '' });
});
