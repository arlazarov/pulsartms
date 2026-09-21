import test from 'node:test';
import assert from 'node:assert/strict';
import {
  decodePath,
  parseMapPayload,
} from '../../Scripts/fleetMap/geometry/encodedPath.ts';

// The same points and string are pinned in the server's tests
// (Server.Tests/Routing/EncodedPathTests.cs). Two codecs in two languages.
const known = [
  { latitude: 38.5, longitude: -120.2 },
  { latitude: 40.7, longitude: -120.95 },
  { latitude: 43.252, longitude: -126.453 },
  { latitude: -33.868821, longitude: 151.209296 },
  { latitude: 0.000001, longitude: -179.999999 },
];
const knownText = '_izlhA~rlgdF_{geC~ywl@_kwzCn`{nIhrabrCoddrpOk`er_A|clvvR';
const bytes = value => new TextEncoder().encode(JSON.stringify(value));

test('the agreed string decodes to the agreed points, across the antimeridian and the equator', () => {
  assert.deepEqual(decodePath(knownText), known);
});

test('nothing decodes to nothing and a damaged string is refused', () => {
  assert.deepEqual(decodePath(''), []);
  assert.deepEqual(decodePath(null), []);
  assert.throws(() => decodePath('_izlhA'), /cut short/);
  assert.throws(() => decodePath('_izl hA~rlgdF'), /invalid/);
});

test('a payload gets its points back wherever a leg carries a path', () => {
  const leg = { miles: 1, seconds: 2, points: [], path: knownText };
  const parsed = parseMapPayload(
    bytes({
      route: { legs: [leg] },
      referenceRoute: { legs: [leg] },
      routes: [{ legs: [leg] }],
      preview: { options: [{ route: { legs: [leg] } }] },
    }),
  );
  for (const found of [
    parsed.route.legs[0],
    parsed.referenceRoute.legs[0],
    parsed.routes[0].legs[0],
    parsed.preview.options[0].route.legs[0],
  ]) {
    assert.deepEqual(found.points, known);
    assert.equal(found.path, undefined);
    assert.equal(found.miles, 1);
  }
});

test('points sent the old way, and objects that merely have a path, are left alone', () => {
  const parsed = parseMapPayload(
    bytes({
      legs: [{ miles: 1, points: [known[0], known[1]] }],
      asset: { path: '/img/truck.svg' },
      both: { path: knownText, points: [known[0]] },
    }),
  );
  assert.deepEqual(parsed.legs[0].points, [known[0], known[1]]);
  assert.equal(parsed.asset.path, '/img/truck.svg');
  assert.deepEqual(parsed.both.points, [known[0]]);
});

test('null payloads pass through', () => {
  assert.equal(parseMapPayload(bytes(null)), null);
});
