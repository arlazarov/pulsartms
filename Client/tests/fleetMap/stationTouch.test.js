import test from 'node:test';
import assert from 'node:assert/strict';
import { pickNearbyStation } from '../../Scripts/fleetMap/rendering/stationTouch.ts';

test('touch misses query ordinary and recommended fuel points in CSS pixels', () => {
  const result = { object: { id: 'nearest' } };
  const overlay = {
    pickObject(query) {
      assert.deepEqual(query, {
        x: 100,
        y: 200,
        radius: 15,
        layerIds: [
          'fuel-points',
          'fuel-recommendation-points',
          'fuel-editing-points',
        ],
      });
      return result;
    },
  };
  assert.equal(
    pickNearbyStation(
      overlay,
      { x: 100, y: 200 },
      { srcEvent: { domEvent: { pointerType: 'touch' } } },
    ),
    result,
  );
});

test('direct hits, secondary clicks, double taps and invalid coordinates do not query GPU', () => {
  const overlay = {
    pickObject() {
      assert.fail('unexpected picking pass');
    },
  };
  const touch = { srcEvent: { pointerType: 'touch' } };
  assert.equal(
    pickNearbyStation(
      overlay,
      { object: { unit: '11006' }, x: 1, y: 2 },
      touch,
    ),
    null,
  );
  assert.equal(
    pickNearbyStation(
      overlay,
      { x: 1, y: 2 },
      { srcEvent: { pointerType: 'mouse', button: 2 } },
    ),
    null,
  );
  assert.equal(
    pickNearbyStation(overlay, { x: 1, y: 2 }, { ...touch, tapCount: 2 }),
    null,
  );
  assert.equal(pickNearbyStation(overlay, {}, touch), null);
});

test('mouse misses preserve a 20px target around the smaller 16px station fill without a visible halo', () => {
  const nearest = { object: { id: 'nearest' } };
  const overlay = {
    pickObject(query) {
      assert.deepEqual(query, {
        x: 100,
        y: 200,
        radius: 2,
        layerIds: [
          'fuel-points',
          'fuel-recommendation-points',
          'fuel-editing-points',
        ],
      });
      return nearest;
    },
  };
  assert.equal(
    pickNearbyStation(
      overlay,
      { x: 100, y: 200 },
      { srcEvent: { pointerType: 'mouse', button: 0 } },
    ),
    nearest,
  );
});

test('coarse-pointer fallback supports map events without pointer type', t => {
  const previous = globalThis.matchMedia;
  globalThis.matchMedia = () => ({ matches: true });
  t.after(() => {
    globalThis.matchMedia = previous;
  });
  let queries = 0;
  const overlay = {
    pickObject() {
      queries++;
      return null;
    },
  };
  assert.equal(pickNearbyStation(overlay, { x: 1, y: 2 }, {}), null);
  assert.equal(queries, 1);
  pickNearbyStation(
    overlay,
    { x: 1, y: 2 },
    { srcEvent: { pointerType: 'mouse' } },
  );
  assert.equal(queries, 2);
});
