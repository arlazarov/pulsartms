import test from 'node:test';
import assert from 'node:assert/strict';
import {
  stopEtaDeadline,
  stopEtaLabels,
} from '../../Scripts/fleetMap/routes/stopEtaLabels.js';

const now = Date.parse('2026-09-08T12:00:00Z');
const stop = {
  dispatchId: 'load',
  stopId: 'pickup',
  arrival: '2026-09-10T14:00:00-04:00',
  timeZoneId: 'America/Toronto',
};

test('technical ETA diagnostics never become labels or change a retained forecast', () => {
  const ready = { validUntil: '2026-09-08T12:02:00Z', stops: [stop] };
  const labels = stopEtaLabels(ready, now);
  for (const unavailableReason of [
    'ETA unavailable: waiting for the saved preceding connection.',
    'ETA is recalculating.',
  ]) {
    for (const routeUpdatePending of [false, true]) {
      assert.equal(
        stopEtaLabels(
          { ...ready, stops: [], unavailableReason, routeUpdatePending },
          now,
        ).size,
        0,
      );
      assert.deepEqual(
        stopEtaLabels({ ...ready, unavailableReason, routeUpdatePending }, now),
        labels,
      );
    }
  }
});

test('stop ETA labels use the server stop identity and destination local time', () => {
  const labels = stopEtaLabels(
    {
      validUntil: '2026-09-08T12:02:00Z',
      stops: [
        stop,
        { ...stop, dispatchId: 'other', arrival: '2026-09-10T15:00:00-04:00' },
      ],
    },
    now,
  );
  assert.deepEqual(labels.get('load:pickup'), {
    text: 'ETA Sep 10, 02:00 PM local',
    arrivalText: 'Sep 10, 02:00 PM',
    statusText: '',
    tone: 'eta',
  });
  assert.deepEqual(labels.get('other:pickup'), {
    text: 'ETA Sep 10, 03:00 PM local',
    arrivalText: 'Sep 10, 03:00 PM',
    statusText: '',
    tone: 'eta',
  });
  assert.equal(labels.get('load:delivery'), undefined);
});

test('stop ETA colors use explicit appointment status instead of treating every estimate as on time', () => {
  const value = overrides =>
    stopEtaLabels(
      {
        validUntil: '2026-09-08T12:02:00Z',
        stops: [
          {
            ...stop,
            appointment: '2026-09-10T14:00:00-04:00',
            lateMinutes: 0,
            ...overrides,
          },
        ],
      },
      now,
    ).get('load:pickup');
  assert.deepEqual(value({}), {
    text: 'ETA Sep 10, 02:00 PM local',
    arrivalText: 'Sep 10, 02:00 PM',
    statusText: 'On time',
    tone: 'success',
  });
  assert.deepEqual(value({ lateMinutes: 1 }), {
    text: 'ETA Sep 10, 02:00 PM local · Late',
    arrivalText: 'Sep 10, 02:00 PM',
    statusText: 'Late',
    tone: 'danger',
  });
  for (const overrides of [
    { appointment: null },
    { appointment: undefined },
    { appointment: 'invalid' },
    { lateMinutes: null },
    { lateMinutes: undefined },
    { lateMinutes: -1 },
    { lateMinutes: NaN },
    { lateMinutes: Infinity },
    { lateMinutes: '0' },
  ]) {
    assert.deepEqual(
      value(overrides),
      {
        text: 'ETA Sep 10, 02:00 PM local',
        arrivalText: 'Sep 10, 02:00 PM',
        statusText: '',
        tone: 'eta',
      },
      JSON.stringify(overrides),
    );
  }
});

test('expired, unknown and invalid ETA values never acquire a label', () => {
  assert.equal(
    stopEtaLabels({ validUntil: '2026-09-08T12:00:00Z', stops: [stop] }, now)
      .size,
    0,
  );
  assert.equal(stopEtaLabels(null, now).size, 0);
  assert.equal(
    stopEtaLabels(
      {
        validUntil: '2026-09-08T12:02:00Z',
        stops: [
          { ...stop, arrival: 'invalid' },
          { ...stop, timeZoneId: 'invalid' },
          { ...stop, timeZoneId: '' },
          { ...stop, stopId: '' },
        ],
      },
      now,
    ).size,
    0,
  );
});

test('retained ETA during route refresh preserves its complete text and color', () => {
  for (const lateMinutes of [0, 1, 60, 185]) {
    const eta = {
      validUntil: '2026-09-08T12:02:00Z',
      stops: [
        { ...stop, appointment: '2026-09-10T14:00:00-04:00', lateMinutes },
      ],
    };
    const complete = stopEtaLabels(eta, now);
    const pending = stopEtaLabels({ ...eta, routeUpdatePending: true }, now);
    assert.deepEqual(pending, complete);
    assert.equal(
      pending.get('load:pickup').statusText,
      lateMinutes ? 'Late' : 'On time',
    );
  }
});

test('unknown, invalid or differently identified pending status cannot invent lateness', () => {
  const eta = { validUntil: '2026-09-08T12:02:00Z', routeUpdatePending: true };
  for (const value of [
    { ...stop, lateMinutes: 185 },
    { ...stop, appointment: 'invalid', lateMinutes: 185 },
    ...[null, undefined, -1, NaN, Infinity, '185'].map(lateMinutes => ({
      ...stop,
      appointment: '2026-09-10T14:00:00-04:00',
      lateMinutes,
    })),
  ]) {
    const label = stopEtaLabels({ ...eta, stops: [value] }, now).get(
      'load:pickup',
    );
    assert.equal(label.previousStatusText, undefined);
    assert.equal(label.statusText, '');
    assert.equal(label.tone, 'eta');
  }
  const labels = stopEtaLabels(
    {
      ...eta,
      stops: [
        {
          ...stop,
          dispatchId: 'other',
          appointment: '2026-09-10T14:00:00-04:00',
          lateMinutes: 185,
        },
      ],
    },
    now,
  );
  assert.equal(labels.get('load:pickup'), undefined);
  assert.equal(labels.get('other:pickup').statusText, 'Late');
  assert.equal(labels.get('other:delivery'), undefined);
});

test('only an explicitly pending forecast gets the bounded grace after its original expiry', () => {
  const eta = {
    validUntil: '2026-09-08T12:00:00Z',
    routeUpdatePending: true,
    stops: [stop],
  };
  const deadline = now + 15 * 60_000;
  assert.equal(stopEtaDeadline(eta), deadline);
  for (const time of [now, now + 60_000, deadline - 1]) {
    assert.equal(stopEtaLabels(eta, time).get('load:pickup').statusText, '');
    assert.equal(
      stopEtaDeadline(eta),
      deadline,
      'reads cannot extend the original grace deadline',
    );
  }
  assert.equal(stopEtaLabels(eta, deadline).size, 0);
  assert.equal(
    stopEtaLabels({ ...eta, routeUpdatePending: false }, now).size,
    0,
  );
  assert.equal(stopEtaLabels({ ...eta, validUntil: 'invalid' }, now).size, 0);
});
