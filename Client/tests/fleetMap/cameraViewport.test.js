import test from 'node:test';
import assert from 'node:assert/strict';
import { createCameraViewport } from '../../Scripts/fleetMap/ui/cameraViewport.ts';

const rect = (left, top, width, height) => ({
  left,
  top,
  width,
  height,
  right: left + width,
  bottom: top + height,
});

function fixture(t) {
  const previous = globalThis.google;
  globalThis.google = {
    maps: {
      LatLng: class {
        constructor(position) {
          Object.assign(this, position);
        }
      },
      Point: class {
        constructor(x, y) {
          Object.assign(this, { x, y });
        }
      },
    },
  };
  t.after(() => {
    globalThis.google = previous;
  });
  const overlays = new Map(),
    listeners = new Map(),
    observers = [];
  let reads = 0,
    mapBounds = rect(80, 100, 1000, 600);
  class Observer {
    nodes = new Set();
    constructor(callback) {
      this.callback = callback;
      observers.push(this);
    }
    observe(node, options) {
      this.nodes.add(node);
      this.options = options;
    }
    unobserve(node) {
      this.nodes.delete(node);
    }
    disconnect() {
      this.nodes.clear();
      this.disconnected = true;
    }
  }
  const stageListeners = new Map();
  const stage = {
    querySelector: selector => overlays.get(selector),
    addEventListener: (name, callback, capture) =>
      stageListeners.set(name, { callback, capture }),
    removeEventListener: name => stageListeners.delete(name),
  };
  const view = {
    ResizeObserver: Observer,
    MutationObserver: Observer,
    addEventListener: (name, callback) => listeners.set(name, callback),
    removeEventListener: name => listeners.delete(name),
  };
  const element = {
    parentElement: stage,
    ownerDocument: { defaultView: view },
    getBoundingClientRect: () => {
      reads++;
      return mapBounds;
    },
  };
  const map = {
    getZoom: () => 5,
    getProjection: () => ({
      fromLatLngToPoint: point => ({ x: point.lng, y: point.lat }),
      fromPointToLatLng: point => ({ lng: point.x, lat: point.y }),
    }),
  };
  const camera = createCameraViewport(element, map);
  t.after(() => camera.dispose());
  return {
    camera,
    map,
    stage,
    observers,
    listeners,
    stageListeners,
    get reads() {
      return reads;
    },
    setMapBounds(value) {
      mapBounds = value;
    },
    overlay(selector, bounds) {
      const node = {
        getBoundingClientRect: () => {
          reads++;
          return bounds();
        },
      };
      overlays.set(selector, node);
      return node;
    },
    remove: selector => overlays.delete(selector),
  };
}

test('viewport no longer installs a secondary disclosure click listener', t => {
  const f = fixture(t);
  assert.equal(f.stageListeners.size, 0);
  f.camera.dispose();
  assert.equal(f.stageListeners.size, 0);
});

test('no overlays retain the exact camera target and ordinary numeric route padding', t => {
  const { camera } = fixture(t),
    target = { lat: 40, lng: -79 };
  assert.equal(camera.center(target), target);
  assert.equal(camera.padding(55), 55);
});

test('inspector top inset follows actual side clearance without repeated style writes', t => {
  const f = fixture(t),
    styles = new Map();
  let clearance = 100,
    writes = 0;
  const info = f.overlay('.fleet-map-info-reserved', () =>
    rect(
      80 + clearance,
      100 +
        Math.min(
          12,
          Number.parseFloat(styles.get('--map-inspector-side-gap') ?? '0'),
        ),
      1000 - clearance * 2,
      160,
    ),
  );
  info.style = {
    getPropertyValue: name => styles.get(name),
    setProperty: (name, value) => {
      styles.set(name, value);
      writes++;
    },
  };
  for (const value of [100, 12, 8, 2, 0, 6, 100]) {
    clearance = value;
    f.camera.refresh();
    assert.equal(styles.get('--map-inspector-side-gap'), `${value}px`);
    const previousWrites = writes;
    f.camera.refresh();
    assert.equal(writes, previousWrites);
  }
});

test('top truck info and right fuel card share one unoccluded camera region', t => {
  const f = fixture(t);
  f.overlay('.fleet-map-info-reserved', () => rect(80, 100, 1000, 160));
  f.overlay('.fuel-plan-editor', () => rect(680, 280, 400, 420));
  f.camera.refresh();
  assert.deepEqual(f.camera.center({ lat: 40, lng: -79 }), {
    lat: 40 - 80 / 32,
    lng: -79 + 200 / 32,
  });
  assert.deepEqual(f.camera.padding(55), {
    left: 55,
    right: 455,
    top: 215,
    bottom: 55,
  });
  const reads = f.reads;
  for (let index = 0; index < 1000; index++) {
    f.camera.center({ lat: 40 + index / 1000, lng: -79 });
    f.camera.padding(55);
  }
  assert.equal(
    f.reads,
    reads,
    'telemetry camera movement only uses cached layout',
  );
});

test('first inspector reveal, Details, Hide and loading update cached insets without moving the camera', t => {
  const f = fixture(t);
  let bounds = rect(0, 0, 0, 0),
    changes = 0;
  const info = f.overlay('.fleet-map-info-reserved', () => bounds);
  f.camera.refresh();
  f.camera.onChange(() => changes++);
  assert.ok(f.observers[0].nodes.has(info));
  bounds = rect(80, 100, 1000, 160);
  f.observers[0].callback();
  assert.equal(changes, 0);
  assert.deepEqual(f.camera.center({ lat: 40, lng: -79 }), {
    lat: 37.5,
    lng: -79,
  });
  f.observers[0].callback();
  assert.equal(changes, 0);
  bounds = rect(80, 100, 1000, 320);
  f.observers[0].callback();
  assert.equal(
    changes,
    0,
    'Details must not repeat the untouched startup focus',
  );
  assert.deepEqual(
    f.camera.center({ lat: 40, lng: -79 }),
    { lat: 35, lng: -79 },
    'later explicit focus uses expanded insets',
  );
  bounds = rect(0, 0, 0, 0);
  f.observers[0].callback();
  assert.equal(changes, 0);
  assert.equal(f.camera.padding(55), 55);
});

test('an actual map resize notifies camera ownership once and retains current overlay insets', t => {
  const f = fixture(t);
  f.overlay('.fleet-map-info-reserved', () => rect(80, 100, 800, 160));
  f.camera.refresh();
  let changes = 0;
  f.camera.onChange(() => changes++);
  f.setMapBounds(rect(80, 100, 800, 500));
  f.observers[0].callback();
  f.listeners.get('resize')();
  assert.equal(changes, 1);
  assert.deepEqual(f.camera.padding(55), {
    left: 55,
    right: 55,
    top: 215,
    bottom: 55,
  });
});

test('captured Follow center survives disclosure without retaining stale layout', t => {
  const f = fixture(t);
  let height = 160;
  f.overlay('.fleet-map-info-reserved', () => rect(80, 100, 1000, height));
  f.camera.refresh();
  const center = f.camera.captureCenter();
  const target = { lat: 40, lng: -79 };
  for (const value of [320, 160, 500, 0]) {
    height = value;
    f.camera.refresh();
    const reads = f.reads;
    assert.deepEqual(center(target), { lat: 37.5, lng: -79 });
    assert.deepEqual(center({ lat: 41, lng: -78 }), { lat: 38.5, lng: -78 });
    assert.equal(f.reads, reads, 'Follow frames must not read DOM layout');
    assert.deepEqual(
      f.camera.captureCenter()(target),
      f.camera.center(target),
      'A new Follow session uses current insets',
    );
  }
  f.camera.dispose();
  assert.equal(center(target), target);
});

test('Follow started without an overlay stays centered after the card appears', t => {
  const f = fixture(t);
  const center = f.camera.captureCenter();
  f.overlay('.fleet-map-info-reserved', () => rect(80, 100, 1000, 160));
  f.camera.refresh();
  const target = { lat: 40, lng: -79 };
  assert.equal(center(target), target);
  assert.notDeepEqual(f.camera.captureCenter()(target), target);
});

test('only direct overlay insertion is observed, removed nodes and all listeners are released', t => {
  const f = fixture(t),
    [resize, mutation] = f.observers;
  assert.deepEqual(mutation.options, { childList: true });
  assert.deepEqual([...mutation.nodes], [f.stage]);
  const card = f.overlay('.fuel-plan-editor', () => rect(680, 280, 400, 420));
  mutation.callback();
  assert.ok(resize.nodes.has(card));
  f.remove('.fuel-plan-editor');
  mutation.callback();
  assert.equal(resize.nodes.has(card), false);
  f.camera.dispose();
  assert.ok(resize.disconnected && mutation.disconnected);
  assert.equal(f.listeners.size, 0);
  const reads = f.reads,
    target = { lat: 40, lng: -79 };
  mutation.callback();
  resize.callback();
  f.camera.refresh();
  assert.equal(f.reads, reads);
  assert.equal(f.camera.center(target), target);
  assert.equal(f.camera.padding(55), 55);
});

test('fully obscured or unavailable projection safely retains the original target', t => {
  const f = fixture(t),
    target = { lat: 40, lng: -79 };
  f.overlay('.fuel-plan-editor', () => rect(0, 0, 2000, 2000));
  f.camera.refresh();
  assert.equal(f.camera.center(target), target);
  assert.equal(f.camera.padding(55), 55);
  f.overlay('.fuel-plan-editor', () => rect(680, 280, 400, 420));
  f.camera.refresh();
  f.map.getProjection = () => null;
  assert.equal(f.camera.center(target), target);
});
