import test from 'node:test';
import assert from 'node:assert/strict';
import { createTruckLayer } from '../../Scripts/fleetMap/trucks/truckLayer.js';

function startupFixture(t, cameraViewport) {
  const previousDocument = globalThis.document,
    previousGoogle = globalThis.google;
  globalThis.document = {
    hidden: false,
    addEventListener() {},
    removeEventListener() {},
  };
  globalThis.google = {
    maps: {
      LatLngBounds: class {
        points = [];
        extend(point) {
          this.points.push(point);
        }
        isEmpty() {
          return this.points.length === 0;
        }
      },
    },
  };
  const domEvents = new Map(),
    cameras = [],
    initial = [];
  const map = {
    getDiv: () => ({
      addEventListener: (name, callback) => domEvents.set(name, callback),
      removeEventListener: name => domEvents.delete(name),
    }),
    addListener: () => ({ remove() {} }),
    fitBounds: bounds => cameras.push({ fit: bounds.points }),
    moveCamera: value => cameras.push(value),
    panTo: center => cameras.push({ pan: center }),
    setZoom() {},
  };
  const markers = new Map();
  const marker = () => ({
    render() {},
    update(value) {
      markers.set(value.truckId, this);
    },
    setSelected() {},
    setVisible(value) {
      this.visible = value;
    },
    dispose() {
      this.disposed = true;
    },
  });
  const layer = createTruckLayer(
    map,
    undefined,
    undefined,
    undefined,
    marker,
    undefined,
    (change, wait) => {
      initial.push({ hasChange: Boolean(change), wait });
      change?.(true);
    },
    cameraViewport,
  );
  t.after(() => {
    layer.dispose();
    globalThis.document = previousDocument;
    globalThis.google = previousGoogle;
  });
  const trucks = [
    { truckId: 'a', truckExternalId: 'first', latitude: 40, longitude: -80 },
    { truckId: 'b', truckExternalId: 'second', latitude: 45, longitude: -75 },
  ].map(truck => ({
    ...truck,
    updatedAt: new Date(Date.now() - 90000).toISOString(),
    speed: 0,
  }));
  return { layer, trucks, cameras, domEvents, initial, markers };
}

test('editing scopes trucks temporarily and restores every truck', t => {
  const { layer, trucks, markers } = startupFixture(t);
  assert.equal('setVisible' in layer, false);
  layer.setTrucks(trucks, []);
  assert.ok([...markers.values()].every(marker => marker.visible));
  layer.setEditingTruck('a');
  assert.equal(markers.get('a').visible, true);
  assert.equal(markers.get('b').visible, false);
  layer.setTrucks(
    [...trucks, { ...trucks[1], truckId: 'c', truckExternalId: 'third' }],
    [],
  );
  assert.equal(markers.get('b').visible, false);
  assert.equal(markers.get('c').visible, false);
  assert.ok(layer.getPosition('b'), 'hidden telemetry remains available');
  layer.setEditingTruck(null);
  assert.ok(
    [...markers.values()].every(marker => marker.visible && !marker.disposed),
  );
  layer.setEditingTruck('b');
  assert.equal(markers.get('a').visible, false);
  assert.equal(markers.get('b').visible, true);
  layer.setEditingTruck(null);
  assert.ok([...markers.values()].every(marker => marker.visible));
});

test('first fleet snapshot fits once while telemetry refresh cannot move the camera again', t => {
  const { layer, trucks, cameras, initial } = startupFixture(t);
  layer.setTrucks(trucks, []);
  assert.deepEqual(cameras, [
    {
      fit: [
        { lat: 40, lng: -80 },
        { lat: 45, lng: -75 },
      ],
    },
  ]);
  assert.equal(initial.length, 1);
  layer.setTrucks(trucks, []);
  layer.setInitialTruck('b');
  layer.setTrucks(trucks, []);
  assert.equal(cameras.length, 1);
});

test('deep-link truck is the initial camera target without an intermediate fleet fit', t => {
  const { layer, trucks, cameras, initial } = startupFixture(t);
  layer.setInitialTruck('b');
  layer.setTrucks(trucks, []);
  assert.deepEqual(cameras, [{ center: { lat: 45, lng: -75 } }]);
  assert.equal(
    initial[0].wait,
    false,
    'moveCamera is immediate and does not wait for an optional idle event',
  );
  layer.setTrucks(trucks, []);
  assert.equal(cameras.length, 1);
});

test('manual camera use before telemetry suppresses the delayed first automatic fit', t => {
  for (const action of ['pointerdown', 'wheel', 'keydown']) {
    const { layer, trucks, cameras, domEvents } = startupFixture(t);
    domEvents.get(action)();
    layer.setTrucks(trucks, []);
    assert.deepEqual(cameras, [], action);
    layer.dispose();
  }
});

test('empty telemetry reveals the initial view and missing deep-link positions fall back to the fleet', t => {
  const { layer, trucks, cameras, initial } = startupFixture(t);
  layer.setTrucks([], []);
  assert.equal(initial[0].hasChange, false);
  layer.setInitialTruck('missing');
  layer.setTrucks(trucks, []);
  assert.equal(cameras.length, 1);
  assert.equal(cameras[0].fit.length, 2);
});

test('a late query focus after empty telemetry selects without undoing the user camera', t => {
  const { layer, trucks, cameras, domEvents } = startupFixture(t);
  layer.setInitialTruck('b');
  layer.setTrucks([], []);
  domEvents.get('pointerdown')();
  layer.setTrucks(trucks, []);
  assert.equal(layer.focusTruck('b', undefined, true), true);
  assert.deepEqual(cameras, []);
  assert.equal(
    layer.focusTruck('a', undefined, true),
    true,
    'a different explicit query target may focus',
  );
  assert.equal(cameras.length, 1);
  assert.equal(
    layer.focusTruck('b'),
    true,
    'explicit search remains an intentional camera action',
  );
  assert.equal(cameras.length, 2);
});

test('manual focus and Follow use cached free-map coordinates, without undoing user gestures or route fits', t => {
  let offset = 0;
  const viewport = {
    center: position => ({ ...position, lat: position.lat + offset }),
    captureCenter() {
      const captured = offset;
      return position => ({ ...position, lat: position.lat + captured });
    },
    padding: value => value,
  };
  const { layer, trucks, cameras, domEvents } = startupFixture(t, viewport);
  layer.setTrucks(trucks, []);
  layer.focusTruck('b');
  assert.deepEqual(cameras.at(-1), { center: { lat: 45, lng: -75 } });
  offset = -2;
  layer.refreshViewport();
  assert.deepEqual(
    cameras.at(-1),
    { center: { lat: 43, lng: -75 } },
    'an actual map resize adjusts the focused truck',
  );
  const beforeGesture = cameras.length;
  domEvents.get('pointerdown')();
  offset = -3;
  layer.refreshViewport();
  assert.equal(
    cameras.length,
    beforeGesture,
    'layout updates do not undo a user camera change',
  );
  layer.focusTruck('b');
  layer.clearViewportFocus();
  const beforeFit = cameras.length;
  offset = -4;
  layer.refreshViewport();
  assert.equal(
    cameras.length,
    beforeFit,
    'route fits supersede temporary manual centering',
  );
  layer.setFollow('b', true);
  assert.deepEqual(cameras.at(-1), { zoom: 15, center: { lat: 41, lng: -75 } });
  offset = -5;
  layer.refreshViewport();
  assert.deepEqual(
    cameras.at(-1),
    { center: { lat: 40, lng: -75 } },
    'stationary Follow responds to an actual map resize',
  );
  layer.dispose();
  const beforeDispose = cameras.length;
  layer.refreshViewport();
  assert.equal(cameras.length, beforeDispose);
});

test('follow continues between telemetry polls until buffered movement is exhausted', t => {
  const saved = {
    document: globalThis.document,
    raf: globalThis.requestAnimationFrame,
    cancel: globalThis.cancelAnimationFrame,
    now: Date.now,
    performance: globalThis.performance,
  };
  let now = 1800000000000,
    tick = 0,
    frame;
  globalThis.document = {
    hidden: false,
    addEventListener() {},
    removeEventListener() {},
  };
  globalThis.requestAnimationFrame = fn => {
    frame = fn;
    return 1;
  };
  globalThis.cancelAnimationFrame = () => {
    frame = null;
  };
  Date.now = () => now;
  globalThis.performance = { now: () => tick };
  const centers = [];
  const layer = createTruckLayer(
    {
      addListener() {
        return { remove() {} };
      },
      moveCamera({ center }) {
        centers.push(center);
      },
      getZoom: () => 15,
    },
    () => {},
    () => {},
    (_, p) => p,
    () => ({
      render() {},
      update() {},
      setVisible() {},
      setSelected() {},
      dispose() {},
    }),
  );
  t.after(() => {
    layer.dispose();
    globalThis.document = saved.document;
    globalThis.requestAnimationFrame = saved.raf;
    globalThis.cancelAnimationFrame = saved.cancel;
    Date.now = saved.now;
    globalThis.performance = saved.performance;
  });
  layer.setInitialTruck('id');
  const point = (age, longitude) => ({
    truckId: 'id',
    truckExternalId: 'ext',
    latitude: 40,
    longitude,
    updatedAt: new Date(now - age).toISOString(),
    speed: 60,
    heading: 90,
  });
  layer.setTrucks([point(60000, -79.9)], [point(90000, -80)]);
  layer.setFollow('id', true);
  for (let i = 0; i < 120; i++) {
    assert.equal(
      typeof frame,
      'function',
      'buffered movement must keep requesting frames',
    );
    now += 16;
    tick += 16;
    const next = frame;
    frame = null;
    next();
  }
  assert.ok(centers.length > 100);
  assert.ok(centers.at(-1).lng > centers[0].lng);
  layer.setFollow('id', false);
  const count = centers.length;
  now += 16;
  tick += 16;
  frame();
  assert.equal(
    centers.length,
    count,
    'disabled follow does not move the camera',
  );
  layer.dispose();
  layer.dispose();
  layer.setTrucks([point(60000, -79.8)], []);
  layer.setEditingTruck(null);
  layer.clearSelection();
  assert.equal(layer.setFollow('id', true), false);
  assert.equal(layer.focusTruck('id', 15), false);
  assert.equal(layer.getPosition('id'), null);
  assert.equal(frame, null, 'disposed trucks must not restart animation');
  assert.equal(centers.length, count);
});

test('follow stops on gestures, deselection and editing another truck', t => {
  const previous = globalThis.document;
  globalThis.document = {
    hidden: false,
    addEventListener() {},
    removeEventListener() {},
  };
  const events = new Map(),
    centers = [],
    changes = [],
    positions = [],
    zooms = [];
  let zoom = 5;
  const map = {
    addListener(name, fn) {
      events.set(name, fn);
      return {
        remove() {
          events.delete(name);
        },
      };
    },
    moveCamera(value) {
      centers.push(value.center);
      if (value.zoom !== undefined) this.setZoom(value.zoom);
    },
    setZoom(value) {
      zoom = value;
      zooms.push(value);
      events.get('zoom_changed')?.();
    },
    getZoom() {
      return zoom;
    },
  };
  const marker = {
    render() {},
    update() {},
    setSelected() {},
    setVisible() {},
    dispose() {},
  };
  const layer = createTruckLayer(
    map,
    () => {},
    (id, position) => positions.push(position),
    (_, p) => ({ ...p, latitude: p.latitude + 0.001 }),
    () => marker,
    v => changes.push(v),
  );
  t.after(() => {
    layer.dispose();
    globalThis.document = previous;
  });
  layer.setTrucks([], []);
  assert.equal(layer.setFollow('missing', true), false);
  assert.equal(layer.isFollowing(), false);
  layer.setInitialTruck('id');
  layer.setTrucks(
    [
      {
        truckId: 'id',
        truckExternalId: 'external',
        latitude: 40,
        longitude: -80,
        updatedAt: new Date(Date.now() - 90000).toISOString(),
        speed: 60,
      },
    ],
    [],
  );
  assert.ok(positions.length > 0);
  assert.equal(layer.setFollow('id', true), true);
  assert.equal(layer.isFollowing(), true);
  assert.deepEqual(zooms, [15]);
  assert.deepEqual(centers.at(-1), { lat: 40.001, lng: -80 });
  const count = centers.length;
  events.get('idle')();
  assert.deepEqual(zooms, [15], 'follow does not keep overriding user zoom');
  assert.equal(
    centers.length,
    count,
    'idle does not create a camera feedback loop',
  );
  map.setZoom(17);
  assert.equal(layer.isFollowing(), false);
  assert.equal(changes.at(-1), false, 'manual zoom ends follow');
  const changeCount = changes.length;
  assert.equal(layer.setFollow('id'), true, 'one click resumes after zoom');
  assert.equal(layer.isFollowing(), true);
  events.get('idle')();
  assert.equal(zoom, 15);
  assert.deepEqual(
    changes.slice(changeCount),
    [true],
    'resume emits one state change',
  );
  map.setZoom(13.5);
  assert.equal(
    changes.at(-1),
    false,
    'fractional manual zoom ends follow immediately',
  );
  assert.equal(layer.setFollow('id'), true);
  assert.equal(zoom, 15);
  assert.deepEqual(centers.at(-1), { lat: 40.001, lng: -80 });
  assert.equal(layer.setFollow('id'), false, 'toggle reads the map state');
  assert.equal(layer.setFollow('id'), true);
  events.get('dragstart')();
  assert.equal(changes.at(-1), false);
  layer.setFollow('id', true);
  layer.clearSelection();
  assert.equal(changes.at(-1), false);
  layer.setFollow('id', true);
  layer.setEditingTruck('another-truck');
  assert.equal(changes.at(-1), false);
});

test('camera events never measure labels and release listeners', () => {
  const previous = globalThis.document;
  const events = new Map();
  const domEvents = new Map();
  let measurements = 0;
  globalThis.document = {
    hidden: false,
    addEventListener() {},
    removeEventListener() {},
  };
  try {
    const layer = createTruckLayer({
      getDiv() {
        return {
          addEventListener(name, fn) {
            assert.equal(
              domEvents.has(name),
              false,
              `duplicate ${name} listener`,
            );
            domEvents.set(name, fn);
          },
          removeEventListener(name, fn) {
            assert.equal(domEvents.get(name), fn);
            domEvents.delete(name);
          },
        };
      },
      addListener(name, fn) {
        events.set(name, fn);
        return {
          remove() {
            events.delete(name);
          },
        };
      },
      getBounds() {
        measurements++;
        return null;
      },
    });
    assert.equal(
      events.has('bounds_changed'),
      false,
      'camera motion does not suspend route progress',
    );
    for (let i = 0; i < 120; i++) events.get('dragstart')();
    assert.equal(measurements, 0);
    events.get('idle')();
    assert.equal(measurements, 0);
    layer.dispose();
    assert.equal(events.size, 0);
    assert.equal(domEvents.size, 0);
  } finally {
    globalThis.document = previous;
  }
});
