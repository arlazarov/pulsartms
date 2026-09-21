import test from 'node:test';
import assert from 'node:assert/strict';
import { nextLoadDisplay } from '../../Scripts/fleetMap/routes/nextLoadDisplay.js';
import { futureRouteColor } from '../../Scripts/fleetMap/rendering/routePalette.ts';

test('pending loads reserve numbers without changing inputs or calculating popup display values', () => {
  const loads = [
    { id: 'pending', loadNumber: 1, stops: [], stopCount: 3, legs: [] },
    {
      id: 'next',
      loadNumber: 2,
      stops: [
        { latitude: 40, longitude: -80, job: 'Pickup', name: 'Warehouse' },
      ],
      deadhead: { miles: 20, points: [] },
      legs: [],
    },
  ];
  const before = structuredClone(loads);
  const display = nextLoadDisplay(loads);
  assert.deepEqual(loads, before);
  assert.deepEqual([...display.groups[0].numbers], [4]);
  assert.deepEqual(display.groups[0].members, [
    { loadId: 'next', loadNumber: 2, index: 0 },
  ]);
  assert.equal(
    display.groups[0].color,
    futureRouteColor(1),
    'pending loads reserve their palette position',
  );
});

test('every co-located visit keeps its own number and exact stop while load ownership is preserved', () => {
  const stop = { latitude: 40, longitude: -80 };
  const display = nextLoadDisplay([
    {
      id: 'load',
      loadNumber: 2,
      stops: [
        { ...stop, job: 'Pickup' },
        { ...stop, job: 'Drop Off' },
        { ...stop, latitude: 40.0001, job: 'Delivery', id: 'second-delivery' },
      ],
      legs: [
        { miles: 10, points: [stop, stop] },
        { miles: 5, points: [stop, stop] },
      ],
      deadhead: { miles: 20, points: [stop, stop] },
    },
  ]);
  assert.equal(display.groups.length, 3);
  assert.deepEqual(
    display.groups.map(group => [...group.numbers]),
    [[1], [2], [3]],
  );
  assert.deepEqual(
    display.groups.map(group => group.members),
    [
      [{ loadId: 'load', loadNumber: 2, index: 0 }],
      [{ loadId: 'load', loadNumber: 2, index: 1 }],
      [{ loadId: 'load', loadNumber: 2, index: 2 }],
    ],
  );
  assert.equal(
    display.groups[2].stop.latitude,
    40.0001,
    'rounded-coordinate neighbors do not borrow the first stop anchor',
  );
  assert.equal(display.groups[2].stop.id, 'second-delivery');
  assert.deepEqual(
    display.lines.map(line => [line.loadId, line.role]),
    [
      ['load', 'deadhead'],
      ['load', 'future'],
      ['load', 'future'],
    ],
  );
  assert.ok(display.groups.every(group => group.color === futureRouteColor(0)));
  assert.equal(
    display.lines[0].routeColor,
    undefined,
    'deadhead retains its separate empty-road color',
  );
  assert.ok(
    display.lines
      .slice(1)
      .every(line => line.routeColor === futureRouteColor(0)),
  );
});

test('repeated same-location visits never produce grouped slash numbers', () => {
  const stops = Array.from({ length: 8 }, (_, index) => ({
    id: `stop-${index}`,
    latitude: 40,
    longitude: -80,
    job: index % 2 ? 'Delivery' : 'Pickup',
  }));
  const display = nextLoadDisplay([
    { id: 'repeated', loadNumber: 12, stops, legs: [] },
  ]);
  assert.equal(display.groups.length, stops.length);
  display.groups.forEach((group, index) => {
    assert.equal(group.stop, stops[index]);
    assert.deepEqual([...group.numbers], [index + 1]);
    assert.deepEqual(group.members, [
      { loadId: 'repeated', loadNumber: 12, index },
    ]);
    assert.equal(group.color, futureRouteColor(0));
  });
});

test('coincident stops from different loads retain separate matching road and marker colors', () => {
  const stop = { latitude: 40, longitude: -80, job: 'Pickup' };
  const loads = Array.from({ length: 4 }, (_, index) => ({
    id: `load-${index}`,
    loadNumber: 12,
    stops: [stop],
    legs: [{ points: [stop, { ...stop, latitude: 41 }] }],
  }));
  const display = nextLoadDisplay(loads);
  assert.equal(display.groups.length, 4);
  for (let index = 0; index < loads.length; index++) {
    assert.deepEqual([...display.groups[index].numbers], [index + 1]);
    assert.deepEqual(display.groups[index].members, [
      { loadId: loads[index].id, loadNumber: 12, index: 0 },
    ]);
    assert.equal(display.groups[index].color, futureRouteColor(index));
    assert.equal(display.lines[index].routeColor, display.groups[index].color);
  }
});
