import test from 'node:test';
import assert from 'node:assert/strict';
import { tripStopLabels } from '../../Scripts/fleetMap/routes/stopLabels.ts';
import { nextLoadDisplay } from '../../Scripts/fleetMap/routes/nextLoadDisplay.ts';

test('a load with one delivery says P and D', () => {
  assert.deepEqual(tripStopLabels(['Pickup', 'Delivery']), ['P', 'D']);
});

test('several deliveries are numbered in stop order', () => {
  assert.deepEqual(tripStopLabels(['Pickup', 'Delivery', 'Delivery']), [
    'P',
    'D1',
    'D2',
  ]);
});

test('every pickup says P while deliveries count on their own', () => {
  assert.deepEqual(
    tripStopLabels(['Pickup', 'Pickup', 'Delivery', 'Delivery']),
    ['P', 'P', 'D1', 'D2'],
  );
});

test('Dispatch job spellings are read the same way', () => {
  assert.deepEqual(tripStopLabels([' Pick  Up ', 'DROP OFF', 'Dropoff']), [
    'P',
    'D1',
    'D2',
  ]);
  assert.deepEqual(tripStopLabels(['pick up', 'drop off']), ['P', 'D']);
});

test('a stop that neither loads nor unloads keeps its place in the load', () => {
  assert.deepEqual(tripStopLabels(['Pickup', 'Hook trailer', 'Delivery']), [
    'P',
    '2',
    'D',
  ]);
  assert.deepEqual(tripStopLabels(['Pickup', undefined, null, 'Delivery']), [
    'P',
    '2',
    '3',
    'D',
  ]);
});

test('each next load restarts its labels while numbers keep the chain order', () => {
  const load = (id, latitude, jobs) => ({
    id,
    loadNumber: id,
    legs: [],
    stops: jobs.map((job, index) => ({
      latitude,
      longitude: -80 + index,
      job,
    })),
  });
  const { groups } = nextLoadDisplay([
    load(12, 40, ['Pickup', 'Delivery', 'Delivery']),
    load(13, 41, ['Pickup', 'Delivery']),
  ]);
  assert.deepEqual(
    groups.map(group => group.labels),
    [['P'], ['D1'], ['D2'], ['P'], ['D']],
  );
  assert.deepEqual(
    groups.map(group => [...group.numbers]),
    [[1], [2], [3], [4], [5]],
  );
});
