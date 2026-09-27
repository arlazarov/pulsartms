import test from 'node:test';
import assert from 'node:assert/strict';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.ts';

// The scene as the map drives it: frames, zoom and trucks rendered again
// and again, with the renderer stubbed so its layers can be read.
function stage(t) {
  let frame = null;
  const originalRaf = globalThis.requestAnimationFrame,
    originalCancel = globalThis.cancelAnimationFrame,
    originalObserver = globalThis.MutationObserver;
  globalThis.requestAnimationFrame = callback => {
    frame = callback;
    return 1;
  };
  globalThis.cancelAnimationFrame = () => {
    frame = null;
  };
  globalThis.MutationObserver = class {
    observe() {}
    disconnect() {}
  };
  t.after(() => {
    globalThis.requestAnimationFrame = originalRaf;
    globalThis.cancelAnimationFrame = originalCancel;
    globalThis.MutationObserver = originalObserver;
  });
  const overlays = [],
    listeners = new Map();
  class Overlay {
    constructor(props) {
      this.props = props;
      overlays.push(this);
    }
    setMap() {}
    setProps(props) {
      this.props = props;
    }
    finalize() {}
  }
  class Layer {
    constructor(props) {
      this.props = props;
      this.id = props.id;
    }
  }
  let zoom = 7;
  const map = {
    moveCamera() {},
    getZoom: () => zoom,
    getDiv: () => ({ dataset: {}, clientWidth: 1000, clientHeight: 700 }),
    setOptions() {},
    addListener(name, callback) {
      listeners.set(name, callback);
      return { remove: () => listeners.delete(name) };
    },
  };
  const scene = createScene(map, {
    GoogleMapsOverlay: Overlay,
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  return {
    scene,
    map,
    flush() {
      const callback = frame;
      frame = null;
      callback?.();
    },
    zoomTo(value) {
      zoom = value;
      listeners.get('zoom_changed')?.();
    },
    layer: id => overlays[0].props.layers.find(layer => layer.id === id)?.props,
  };
}

// Where the renderer draws a truck's number: its position plus its offset.
function numberAt(view, unit) {
  const layer = view.layer('truck-numbers');
  const row = layer.data.find(truck => truck.unit === unit);
  return { position: layer.getPosition(row), row };
}

const delivery = { lng: -74.241, lat: 42.9379 };

test('a moving truck ringed by its next stop keeps its number on the badge, and driving relays nothing', t => {
  const view = stage(t);
  const stop = new view.scene.StopMarker({ position: delivery, number: '2' });
  stop.setNext(true, 'truck-11006');
  const truck = view.scene.createTruckMarker(view.map, () => {});
  truck.update({
    unitNumber: '11006',
    engineState: 'On',
    truckId: 'truck-11006',
  });
  truck.render({ longitude: -74.3255, latitude: 42.93314, speed: 59 });
  view.flush();

  const first = numberAt(view, '11006');
  assert.equal(first.row.merged, true, 'far out, the truck is the ring');
  assert.deepEqual(first.position, [delivery.lng, delivery.lat]);
  const layouts = view.scene.stopLayouts();

  // Five more frames of the drive, still over the badge at this zoom.
  for (let step = 1; step <= 5; step++) {
    truck.render({
      longitude: -74.3255 + step * 0.01,
      latitude: 42.93314,
      speed: 59,
    });
    view.flush();
    const now = numberAt(view, '11006');
    assert.deepEqual(
      now.position,
      [delivery.lng, delivery.lat],
      `frame ${step}: the number stays on the badge`,
    );
    assert.equal(now.row.merged, true);
  }
  assert.equal(
    view.scene.stopLayouts(),
    layouts,
    'five frames of driving laid the stops out no more times',
  );

  // Zoomed in, the two part: one layout, and the number is back on the truck.
  view.zoomTo(14);
  view.flush();
  const near = numberAt(view, '11006');
  assert.equal(near.row.merged, false);
  assert.deepEqual(near.position, near.row.position);
  assert.equal(view.scene.stopLayouts(), layouts + 1);
});

// The ring belongs to the route's truck: another truck driving over the
// same badge is drawn as itself, and the route's own truck, far away,
// makes no ring either.
test('only the truck whose route it is becomes its next stop ring', t => {
  const view = stage(t);
  const stop = new view.scene.StopMarker({ position: delivery, number: '2' });
  stop.setNext(true, 'truck-11006');
  const own = view.scene.createTruckMarker(view.map, () => {});
  own.update({
    unitNumber: '11006',
    engineState: 'On',
    truckId: 'truck-11006',
  });
  own.render({ longitude: -80, latitude: 35, speed: 59 });
  const other = view.scene.createTruckMarker(view.map, () => {});
  other.update({
    unitNumber: '54777',
    engineState: 'On',
    truckId: 'truck-54777',
  });
  other.render({ longitude: -74.3255, latitude: 42.93314, speed: 59 });
  view.flush();

  assert.equal(numberAt(view, '54777').row.merged, false);
  assert.equal(numberAt(view, '11006').row.merged, false);
  const badge =
    view.layer('route-stop-1-points') ?? view.layer('route-stop-2-points');
  assert.ok(badge === undefined || badge.data.every(row => !row.standing));
});
