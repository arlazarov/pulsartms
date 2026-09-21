import test from 'node:test';
import assert from 'node:assert/strict';
test('clock times distinguish midnight and noon while preserving the scheduled local time', () => {
  for (const [scheduledTime, expected] of [
    ['00:00', '12:00 AM'],
    ['12:00', '12:00 PM'],
    ['14:00', '02:00 PM'],
    ['23:59', '11:59 PM'],
  ])
    assert.equal(stopAppointment({ scheduledTime }), expected);
});
import { stopAppointment } from '../../Scripts/fleetMap/routes/stopAppointment.ts';

test('same-day appointment windows show the calendar date once', () => {
  const start = {
    scheduledDate: '2026-09-09',
    scheduledTime: '07:00:00',
    scheduledTime2: '14:00:59',
  };
  assert.equal(
    stopAppointment({ ...start, scheduledDate2: '2026-09-09' }),
    'Sep 9 · 07:00 AM – 02:00 PM',
  );
  assert.equal(stopAppointment(start), 'Sep 9 · 07:00 AM – 02:00 PM');
  assert.equal(
    stopAppointment({
      scheduledDate: '2026-09-09',
      scheduledDate2: '2026-09-09',
    }),
    'Sep 9',
  );
});

test('cross-day and cross-year appointment windows preserve their date boundaries', () => {
  assert.equal(
    stopAppointment({
      scheduledDate: '2026-09-09',
      scheduledTime: '00:00:00',
      scheduledDate2: '2026-09-10',
      scheduledTime2: '05:15:59',
    }),
    'Sep 9 · 12:00 AM – Sep 10 · 05:15 AM',
  );
  assert.equal(
    stopAppointment({
      scheduledDate: '2026-12-31',
      scheduledTime: '23:00:00',
      scheduledDate2: '2027-01-01',
      scheduledTime2: '01:00:00',
    }),
    'Dec 31, 2026 · 11:00 PM – Jan 1, 2027 · 01:00 AM',
  );
});

test('identical appointment endpoints do not create a redundant time range', () => {
  assert.equal(
    stopAppointment({
      scheduledDate: '2026-09-09',
      scheduledTime: '07:00:00',
      scheduledDate2: '2026-09-09',
      scheduledTime2: '07:00:59',
    }),
    'Sep 9 · 07:00 AM',
  );
});

test('midnight and fractional seconds retain the supplied local clock without timezone conversion', t => {
  const original = process.env.TZ;
  t.after(() => {
    if (original === undefined) delete process.env.TZ;
    else process.env.TZ = original;
  });
  const appointment = {
    scheduledDate: '2026-01-01',
    scheduledTime: '00:00:59.9999999',
  };
  for (const zone of ['America/Los_Angeles', 'Pacific/Kiritimati']) {
    process.env.TZ = zone;
    assert.equal(stopAppointment(appointment), 'Jan 1 · 12:00 AM');
  }
});

test('missing or invalid appointment parts remain explicit without normalizing impossible dates', () => {
  assert.equal(stopAppointment({}), '—');
  assert.equal(
    stopAppointment({ scheduledDate: '2026-02-30', scheduledTime: '25:00:00' }),
    '—',
  );
  assert.equal(
    stopAppointment({ scheduledDate: '2026-2-03', scheduledTime: '09:60:00' }),
    '—',
  );
  assert.equal(
    stopAppointment({ scheduledDate: '2026-09-09', scheduledTime: 'invalid' }),
    'Sep 9',
  );
  assert.equal(
    stopAppointment({ scheduledDate: 'invalid', scheduledTime: '07:00:00' }),
    '07:00 AM',
  );
  assert.equal(
    stopAppointment({ scheduledDate: '2028-02-29', scheduledTime: '07:00:00' }),
    'Feb 29 · 07:00 AM',
  );
  assert.equal(
    stopAppointment({ scheduledDate: '2026-02-29', scheduledTime: '07:00:00' }),
    '07:00 AM',
  );
});
