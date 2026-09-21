import test from 'node:test';
import assert from 'node:assert/strict';
import { createMapRepaint } from '../../Scripts/fleetMap/provider/mapRepaint.ts';

function fixture() {
  const overlays = [];
  const map = { getRenderingType: () => 'VECTOR' };
  const repaint = createMapRepaint(map, () => {
    const overlay = {
      redraws: 0,
      setMap(value) {
        this.map = value;
      },
      requestRedraw() {
        this.redraws++;
      },
    };
    overlays.push(overlay);
    return overlay;
  });
  return { repaint, overlays, map };
}

test('a stationary map gets one provider frame before revealing its new GPU', async () => {
  const { repaint, overlays, map } = fixture();
  let ready = 0;
  repaint.request(() => ready++);
  repaint.request(() => ready++);
  assert.equal(overlays.length, 1);
  const overlay = overlays[0];
  assert.equal(overlay.map, map);
  assert.equal(ready, 0);
  overlay.onContextRestored();
  assert.equal(overlay.redraws, 1);
  overlay.onDraw();
  overlay.onDraw();
  assert.equal(ready, 0);
  await Promise.resolve();
  assert.equal(ready, 1);
  assert.equal(overlay.map, null);
  assert.deepEqual(Object.keys(map), ['getRenderingType']);
  repaint.dispose();
});

test('disposing a pending repaint cancels late native and microtask callbacks', async () => {
  const { repaint, overlays } = fixture();
  let ready = 0;
  repaint.request(() => ready++);
  const overlay = overlays[0];
  overlay.onDraw();
  repaint.dispose();
  overlay.onContextRestored();
  overlay.onDraw();
  repaint.request(() => ready++);
  await Promise.resolve();
  assert.equal(overlay.map, null);
  assert.equal(overlay.redraws, 0);
  assert.equal(ready, 0);
  assert.equal(overlays.length, 1);
});

test('non-vector fixtures do not allocate a WebGL overlay', () => {
  let ready = 0;
  const repaint = createMapRepaint({ getRenderingType: () => 'RASTER' }, () => {
    throw new Error('Unexpected native overlay');
  });
  repaint.request(() => ready++);
  assert.equal(ready, 1);
  repaint.dispose();
});
