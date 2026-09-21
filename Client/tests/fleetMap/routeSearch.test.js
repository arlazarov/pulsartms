import test from 'node:test';
import assert from 'node:assert/strict';
import {
  lowerBound,
  segmentRange,
} from '../../Scripts/fleetMap/geometry/routeSearch.ts';

test('segment window matches exhaustive search including duplicate distances and boundaries', () => {
  for (const values of [
    [],
    [0],
    [0, 0, 1, 1, 2, 5, 10],
    Array.from({ length: 1001 }, (_, i) => i / 10),
  ]) {
    for (const center of [-20, 0, 1, 5, 10, 50, 100, 200]) {
      const [start, end] = segmentRange(values, center - 5, center + 5);
      const actual = [];
      for (let i = start; i < end; i++) actual.push(i);
      const expected = [];
      for (let i = 1; i < values.length; i++) {
        if (values[i] >= center - 5 && values[i - 1] <= center + 5)
          expected.push(i);
      }
      assert.deepEqual(actual, expected);
    }
  }
});

test('large routes search only the nearby window using logarithmic index lookups', () => {
  let reads = 0;
  const values = new Proxy(
    Array.from({ length: 100001 }, (_, i) => i / 100),
    {
      get(target, key) {
        if (/^\d+$/.test(String(key))) reads++;
        return target[key];
      },
    },
  );
  const [start, end] = segmentRange(values, 495, 505);
  assert.equal(end - start, 1002);
  assert.ok(reads <= 36);
  assert.equal(lowerBound([0, 1, 1, 2], 1), 1);
});
