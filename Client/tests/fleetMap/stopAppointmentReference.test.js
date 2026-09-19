import test from 'node:test';
import assert from 'node:assert/strict';
import { stopAppointmentReference } from '../../Scripts/fleetMap/routes/stopAppointmentReference.js';

const shared =
  'Shipper appointment confirmation number: PU123456. Receiver appointment confirmation number: DL654321. Shipper BOL: 42845601. Service for Load sentinel.';

test('explicit appointment references respect the selected pickup or delivery role', () => {
  assert.deepEqual(stopAppointmentReference(shared, 'Pick Up'), ['PU123456']);
  assert.deepEqual(stopAppointmentReference(shared, 'Drop Off'), ['DL654321']);
  assert.deepEqual(stopAppointmentReference(shared, 'unknown'), []);
  const example =
    'Shipper BOL: 42845601. Receiver appointment confirmation number: T380213479058.';
  assert.deepEqual(stopAppointmentReference(example, 'Delivery'), [
    'T380213479058',
  ]);
  assert.deepEqual(stopAppointmentReference(example, 'Pickup'), []);
  assert.deepEqual(
    stopAppointmentReference(
      "Receiver's appointment confirmation number: DL1234",
      'Delivery',
    ),
    ['DL1234'],
  );
});

test('generic references belong to the stop and preserve distinct exact IDs', () => {
  const notes =
    "Appointment #: AP123456; appt no. 'ABC-123'; Appointment confirmation: AP123456. Appt # abc-123.";
  assert.deepEqual(stopAppointmentReference(notes, 'Pickup'), [
    'AP123456',
    'ABC-123',
  ]);
  assert.deepEqual(stopAppointmentReference(notes, 'Delivery'), [
    'AP123456',
    'ABC-123',
  ]);
  assert.deepEqual(
    stopAppointmentReference(
      'Pickup: Appt confirmation #: PU-98765. Delivery: Appointment no: DL_54321.',
      'Pickup',
    ),
    ['PU-98765'],
  );
});

test('other identifiers, dates, times, unsupported prose and ambiguous values never become appointment references', () => {
  for (const notes of [
    null,
    '',
    'Shipper BOL: 42845601. Load number: 1373. Order: 565606595.',
    'Service for Load. General information.',
    'Use appointment number: 123456.',
    'Appointment: 123456.',
    'Appt #: 2026-09-09.',
    'Appt #: 09/09/2026.',
    'Appt #: 20260909.',
    'Appt #: 09:00.',
    'Appt #: Sep92026.',
    'Appt #: 2026.',
    'Appt #: TBD.',
    'Appt #: 12345 confirmed.',
    'Appt #: BOL12345.',
    'Appt #: LOAD12345.',
    'Appt #: ORDER12345.',
    'Appt #: A123.45.',
    'Shipper (pickup) appointment number: PU1234.',
    'Appt #: <img src=x onerror=alert(1)>.',
  ])
    assert.deepEqual(
      stopAppointmentReference(notes, 'Pickup'),
      [],
      String(notes),
    );
});

test('bounded scans deduplicate references and never publish a partial identifier', () => {
  assert.deepEqual(
    stopAppointmentReference(
      Array.from({ length: 6 }, (_, i) => `Appt #: AP123${i}.`).join(' '),
      'Pickup',
    ),
    ['AP1230', 'AP1231', 'AP1232', 'AP1233'],
  );
  assert.deepEqual(
    stopAppointmentReference(`Appt #: ${'A'.repeat(64)}123.`, 'Pickup'),
    [],
  );
  const prefix = `${' '.repeat(4096 - 'Appt #: AP123'.length)}Appt #: AP123`;
  assert.deepEqual(stopAppointmentReference(`${prefix}456.`, 'Pickup'), []);
  assert.deepEqual(
    stopAppointmentReference(`${' '.repeat(4096)}Appt #: AP123456.`, 'Pickup'),
    [],
  );
});
