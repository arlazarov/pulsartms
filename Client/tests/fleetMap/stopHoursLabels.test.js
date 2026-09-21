import test from 'node:test';
import assert from 'node:assert/strict';
import { stopEtaLabels } from '../../Scripts/fleetMap/routes/stopEtaLabels.ts';
import { alternativeTone } from '../../Scripts/fleetMap/routes/stopHoursLabels.ts';

const now = Date.parse('2026-09-08T12:00:00Z');
const stop = {
  dispatchId: 'load',
  stopId: 'pickup',
  arrival: '2026-09-10T12:00:00-04:00',
  timeZoneId: 'America/Toronto',
  appointment: '2026-09-10T18:00:00-04:00',
  lateMinutes: 0,
  hours: {
    cycleVerified: true,
    cycleAtArrivalMinutes: -480,
    cycleAfterStopMinutes: -600,
    drivingShortfallMinutes: 480,
    firstCycleShortageAt: '2026-09-10T04:00:00-04:00',
    alternatives: [],
  },
};
const forecast = {
  validUntil: '2026-09-08T12:02:00Z',
  stops: [stop],
  cycleAtCalculation: {
    remainingMinutes: 600,
    nextRecapAt: '2026-09-11T00:00:00-04:00',
    nextRecapMinutes: 185,
    homeTimeZoneId: 'America/Toronto',
    recapVerified: true,
  },
};
const label = (value = stop, eta = forecast) =>
  stopEtaLabels({ ...eta, stops: [value] }, now).get('load:pickup');
const withHours = hours => ({ ...stop, hours: { ...stop.hours, ...hours } });

test('observed arrival displays current cycle without using departure hours', () => {
  const value = withHours({
    cycleAtArrivalMinutes: null,
    currentCycleMinutes: 3837,
    cycleAfterStopMinutes: 4200,
    firstCycleShortageAt: null,
    drivingShortfallMinutes: 0,
  });
  const original = structuredClone(value);
  const actual = label(value);
  assert.equal(actual.hours[0].value, '+63h 57m');
  assert.equal(actual.hours[0].title, 'Current cycle remaining at this stop');
  assert.notEqual(actual.statusText, 'Cycle unknown');
  assert.deepEqual(value, original);
});

test('alternative tone follows known lateness instead of recap or reset kind', () => {
  for (const kind of ['recap', 'restart']) {
    for (const [lateMinutes, expected] of [
      [0, 'success'],
      [10, 'danger'],
      [null, 'neutral'],
      [undefined, 'neutral'],
      [-1, 'neutral'],
      [NaN, 'neutral'],
      [Infinity, 'neutral'],
      ['0', 'neutral'],
    ]) {
      assert.equal(alternativeTone({ kind, lateMinutes }), expected);
    }
  }
});

test('road-only on-time arrival with cycle debt is explicitly short, never green', () => {
  const actual = label();
  assert.equal(actual.etaLabel, 'ETA');
  assert.equal(actual.statusText, 'Cycle short');
  assert.equal(actual.tone, 'danger');
  assert.deepEqual(actual.hours.slice(0, 1), [
    {
      label: 'Cycle remaining',
      value: '−8h 00m',
      tone: 'danger',
      title: 'Estimated cycle remaining on arrival',
    },
  ]);
  assert.equal(actual.hours[1].value, 'Sep 11');
  assert.equal(actual.hours[1].credit, '+3h 05m');
  assert.equal(actual.hours[1].title, undefined);
});

test('positive negative equal and unknown service balances never add a departure row or alter server data', () => {
  for (const [arrival, departure, verified, expected] of [
    [2056, 2241, true, '+34h 16m'],
    [-480, -600, true, '−8h 00m'],
    [2056, 2056, true, '+34h 16m'],
    [-60, -60, true, '−1h 00m'],
    [0, 0, true, '0h 00m'],
    [null, null, true, '—'],
    [60, null, true, '+1h 00m'],
    [null, 60, true, '—'],
    [120, -60, false, '—'],
  ]) {
    const value = withHours({
      cycleAtArrivalMinutes: arrival,
      cycleAfterStopMinutes: departure,
      cycleVerified: verified,
      alternatives: [
        { kind: 'recap', arrival: '2026-09-10T17:00:00-04:00', lateMinutes: 0 },
      ],
    });
    const original = structuredClone(value);
    const actual = label(value);
    assert.deepEqual(
      actual.hours.map(row => row.label),
      verified
        ? ['Cycle remaining', 'Next recap', 'With recap']
        : ['Cycle remaining', 'Next recap'],
    );
    assert.equal(actual.hours[0].value, expected);
    assert.doesNotMatch(JSON.stringify(actual.hours), /After service/i);
    assert.deepEqual(value, original);
  }
});

test('a positive final cycle cannot hide an earlier driving shortage', () => {
  const actual = label(
    withHours({ cycleAtArrivalMinutes: 120, cycleAfterStopMinutes: 0 }),
  );
  assert.equal(actual.statusText, 'Cycle short');
  assert.equal(actual.tone, 'danger');
  assert.equal(actual.hours[0].value, '+2h 00m');
});

test('destination service debt does not retroactively invalidate arrival', () => {
  const actual = label(
    withHours({
      cycleAtArrivalMinutes: 60,
      cycleAfterStopMinutes: -60,
      drivingShortfallMinutes: 0,
      firstCycleShortageAt: null,
    }),
  );
  assert.equal(actual.statusText, 'On time');
  assert.equal(actual.tone, 'success');
  assert.equal(actual.hours[0].value, '+1h 00m');
  assert.equal(actual.hours[0].tone, 'neutral');
  assert.doesNotMatch(JSON.stringify(actual.hours), /After service|−1h 00m/);
});

test('unknown cycle history does not certify on-time feasibility or offer alternatives', () => {
  const actual = label(
    withHours({
      cycleVerified: false,
      alternatives: [
        {
          kind: 'restart',
          arrival: '2026-09-10T14:00:00-04:00',
          lateMinutes: 0,
        },
      ],
    }),
  );
  assert.equal(actual.statusText, 'Cycle unknown');
  assert.equal(actual.tone, 'eta');
  assert.equal(actual.hours[0].value, '—');
  assert.ok(actual.hours.every(row => !row.status));
  for (const invalid of [undefined, null, NaN, Infinity, '60', 1.5]) {
    assert.equal(
      label(withHours({ cycleAtArrivalMinutes: invalid })).statusText,
      'Cycle unknown',
    );
  }
});

test('unknown or short cycle does not hide independently known road lateness', () => {
  for (const verified of [false, true]) {
    const actual = label({
      ...withHours({ cycleVerified: verified }),
      lateMinutes: 60,
    });
    assert.equal(
      actual.statusText,
      verified ? 'Late · Cycle short' : 'Late · Cycle unknown',
    );
    assert.equal(actual.tone, 'danger');
  }
});

test('recap remains conditional and reset alternatives are not displayed', () => {
  const actual = label(
    withHours({
      alternatives: [
        { kind: 'recap', arrival: '2026-09-10T17:00:00-04:00', lateMinutes: 0 },
        {
          kind: 'restart',
          arrival: '2026-09-10T18:00:00-04:00',
          lateMinutes: 0,
        },
      ],
    }),
  );
  assert.equal(actual.arrivalText, 'Sep 10 · 12:00 PM');
  assert.equal(actual.statusText, 'Cycle short');
  assert.deepEqual(actual.hours.slice(-1), [
    {
      label: 'With recap',
      value: 'Sep 10 · 05:00 PM',
      status: 'On time with recap',
      tone: 'success',
    },
  ]);
  assert.ok(actual.hours.every(row => row.label !== 'If reset'));
  const late = label(
    withHours({
      alternatives: [
        {
          kind: 'recap',
          arrival: '2026-09-11T08:00:00-04:00',
          lateMinutes: 840,
        },
        {
          kind: 'restart',
          arrival: '2026-09-11T10:00:00-04:00',
          lateMinutes: 960,
        },
      ],
    }),
  );
  assert.equal(late.hours.at(-1).label, 'With recap');
  assert.equal(late.hours.at(-1).status, 'Late by 14h 00m');
  assert.ok(late.hours.every(row => row.label !== 'If reset'));
});

test('pending snapshots retain cycle, recap, alternatives and their original presentation', () => {
  const value = withHours({
    alternatives: [
      { kind: 'recap', arrival: '2026-09-10T17:00:00-04:00', lateMinutes: 0 },
    ],
  });
  const actual = label(value, { ...forecast, routeUpdatePending: true });
  assert.deepEqual(actual, label(value));
  assert.equal(actual.statusText, 'Cycle short');
  assert.equal(actual.previousStatusText, undefined);
  assert.equal(actual.tone, 'danger');
  assert.equal(actual.hours[0].value, '−8h 00m');
  assert.equal(actual.hours.at(-1).value, 'Sep 10 · 05:00 PM');
  assert.equal(actual.hours.at(-1).status, 'On time with recap');
  assert.equal(actual.hours[0].tone, 'danger');
  assert.equal(actual.hours.at(-1).tone, 'success');
});

test('next recap is exact baseline home time, not borrowed from a later stop or retained after its time', () => {
  for (const baseline of [
    null,
    { ...forecast.cycleAtCalculation, recapVerified: false },
    {
      ...forecast.cycleAtCalculation,
      nextRecapAt: '2026-09-08T08:00:00-04:00',
    },
    { ...forecast.cycleAtCalculation, nextRecapMinutes: 0 },
    { ...forecast.cycleAtCalculation, homeTimeZoneId: 'invalid' },
  ]) {
    assert.ok(
      label(stop, { ...forecast, cycleAtCalculation: baseline }).hours.every(
        row => row.label !== 'Next recap',
      ),
    );
  }
  assert.equal(
    stopEtaLabels({ ...forecast, validUntil: '2026-09-08T12:00:00Z' }, now)
      .size,
    0,
  );
});

test('next recap shows the home-local date and amount without time or zone labels', () => {
  for (const [nextRecapAt, homeTimeZoneId, expected] of [
    ['2026-09-10T10:05:00Z', 'Pacific/Kiritimati', 'Sep 11'],
    ['2026-09-11T06:55:00Z', 'America/Los_Angeles', 'Sep 10'],
  ]) {
    const baseline = {
      ...forecast.cycleAtCalculation,
      nextRecapAt,
      homeTimeZoneId,
    };
    const recap = label(stop, {
      ...forecast,
      cycleAtCalculation: baseline,
    }).hours.find(row => row.recap);
    assert.deepEqual(recap, {
      label: 'Next recap',
      value: expected,
      credit: '+3h 05m',
      recap: true,
      tone: 'neutral',
    });
    assert.equal(baseline.nextRecapAt, nextRecapAt);
    assert.equal(baseline.homeTimeZoneId, homeTimeZoneId);
  }
});

test('malformed, duplicated and unknown alternative kinds do not fabricate status', () => {
  const valid = {
    kind: 'recap',
    arrival: '2026-09-10T17:00:00-04:00',
    lateMinutes: 0,
  };
  const actual = label(
    withHours({
      alternatives: [
        { ...valid, kind: 'other' },
        { ...valid, arrival: 'invalid' },
        { ...valid, lateMinutes: null },
        { ...valid, lateMinutes: -1 },
        valid,
        { ...valid, arrival: '2026-09-10T18:00:00-04:00' },
      ],
    }),
  );
  assert.equal(
    actual.hours.filter(row => row.label === 'With recap').length,
    1,
  );
  assert.equal(actual.hours.at(-1).value, 'Sep 10 · 05:00 PM');
});
