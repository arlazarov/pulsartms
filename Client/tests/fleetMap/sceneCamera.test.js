import test from 'node:test';
import assert from 'node:assert/strict';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.js';

function fixture(t, mode = 'VECTOR') {
  let frame, deck;
  const native = [];
  const globals = {
    google: globalThis.google,
    requestAnimationFrame: globalThis.requestAnimationFrame,
    cancelAnimationFrame: globalThis.cancelAnimationFrame,
  };
  globalThis.requestAnimationFrame = callback => {
    frame = callback;
    return 1;
  };
  globalThis.cancelAnimationFrame = () => {
    frame = null;
  };
  class NativeOverlay {
    constructor() {
      native.push(this);
    }
    setMap(value) {
      this.map = value;
    }
    requestRedraw() {}
  }
  globalThis.google = { maps: { WebGLOverlayView: NativeOverlay } };
  t.after(() => Object.assign(globalThis, globals));
  class Overlay {
    constructor(props) {
      this.props = props;
      deck = this;
    }
    setMap() {}
    setProps(props) {
      Object.assign(this.props, props);
    }
    finalize() {
      this.finalized = true;
    }
  }
  class Layer {
    constructor(props) {
      this.props = props;
    }
  }
  const map = {
    getRenderingType: () => mode,
    getDiv: () => ({ dataset: {} }),
    addListener: () => ({ remove() {} }),
  };
  const scene = createScene(map, {
    GoogleMapsOverlay: Overlay,
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  t.after(() => scene.dispose());
  return {
    scene,
    deck,
    native,
    setMode(value) {
      mode = value;
    },
    flush() {
      const callback = frame;
      frame = null;
      callback?.();
    },
  };
}

for (const initialMode of ['VECTOR', 'UNINITIALIZED']) {
  test(`scene waits for the provider camera from ${initialMode}`, async t => {
    const { deck, native, flush, setMode } = fixture(t, initialMode);
    assert.equal(deck.props.layerFilter(), false);
    setMode('VECTOR');
    deck.props.onLoad();
    flush();
    assert.equal(deck.props.layerFilter(), false);
    assert.equal(native.length, 1);
    native[0].onDraw();
    assert.equal(deck.props.layerFilter(), false);
    await Promise.resolve();
    flush();
    assert.equal(deck.props.layerFilter, null);
    assert.equal(native[0].map, null);

    deck.props.onLoad();
    assert.equal(
      deck.props.layerFilter(),
      false,
      'A replacement GPU device must synchronize again',
    );
    assert.equal(native.length, 2);
  });
}

test('scene ignores device and camera callbacks after disposal', async t => {
  const { scene, deck, native, flush } = fixture(t);
  deck.props.onLoad();
  native[0].onDraw();
  scene.dispose();
  await Promise.resolve();
  deck.props.onLoad();
  flush();
  assert.equal(deck.finalized, true);
  assert.equal(deck.props.layerFilter(), false);
  assert.equal(native.length, 1);
  assert.equal(native[0].map, null);
});

test('disposal clears the rendering-mode listeners the overlay library leaves behind', t => {
  const { scene } = fixture(t);
  const cleared = [];
  globalThis.google.maps.event = {
    clearListeners: (_map, name) => cleared.push(name),
  };
  scene.dispose();
  assert.deepEqual(cleared, ['renderingtype_changed']);
});

test('raster startup releases the gate without a native WebGL overlay', t => {
  const { deck, native, flush } = fixture(t, 'RASTER');
  deck.props.onLoad();
  flush();
  assert.equal(deck.props.layerFilter, null);
  assert.equal(native.length, 0);
});
