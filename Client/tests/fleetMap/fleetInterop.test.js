import test from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
import { createRouteStops } from '../../Scripts/fleetMap/routes/routeStops.js';

const ports = {
  'routes/routeLayer': `export const createRouteLayer = (...args) => {
    const fixture = globalThis.fleetInteropFixture;
    fixture.routePopup = args[5]({}, {onClose: () => fixture.route.closePopup()});
    fixture.openCurrentStop = (content = {kind: 'stop'}) => { args[2](); fixture.routePopup.show(content); };
    fixture.routeProgress = args[1];
    fixture.routeCanFit = args[6];
    fixture.routeFitPadding = args[7];
    return fixture.route;
  };`,
  'routes/nextLoads': `export const createNextLoadsLayer = (...args) => {
    const fixture = globalThis.fleetInteropFixture;
    fixture.selectNextLoad = (id, index, leg) => { fixture.selectedNextLoad = id; args[3](id, index, leg); };
    return fixture.nextLoads;
  };`,
  'trucks/truckLayer': `export const createTruckLayer = (...args) => {
    const fixture = globalThis.fleetInteropFixture;
    fixture.selectTruck = args[1];
    return fixture.trucks;
  };`,
  'stations/stationLayer': `export const createStationLayer = (...args) => {
    const fixture = globalThis.fleetInteropFixture;
    fixture.fuelPopup = args[3]({}, {onClose: () => fixture.stations.closePopup()});
    fixture.openFuelStation = (content = {kind: 'fuel'}) => { args[1](); fixture.fuelPopup.show(content); };
    fixture.editFuelStation = args[4];
    return fixture.stations;
  };`,
  'provider/googleMapsLoader': 'export const loadGoogleMaps = async () => {};',
  'provider/mapHost': `export const createMapHost = () => (_element, options) => {
    globalThis.fleetInteropFixture.mapOptions = options;
    return {map: globalThis.fleetInteropFixture.map, show() {}, initialCamera(change) { change?.(); }, release() { globalThis.fleetInteropFixture.released = (globalThis.fleetInteropFixture.released ?? 0) + 1; }};
  };`,
  'rendering/gpuScene':
    'export const createGpuScene = () => globalThis.fleetInteropFixture.gpuScene;',
  'lifecycle/backgroundWork':
    'export const yieldToBrowser = () => Promise.resolve();',
};
const bundle = await build({
  entryPoints: [
    fileURLToPath(
      new URL('../../Scripts/fleetMap/fleetMap.js', import.meta.url),
    ),
  ],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'node',
  plugins: [
    {
      name: 'fleet-ports',
      setup(build) {
        build.onResolve({ filter: /\.js$/ }, args => {
          const key = args.path.replace(/^\.\//, '').replace(/\.js$/, '');
          return ports[key] ? { path: key, namespace: 'fixture' } : undefined;
        });
        build.onLoad({ filter: /.*/, namespace: 'fixture' }, args => ({
          contents: ports[args.path],
        }));
      },
    },
  ],
});
const { createFleetMap } = await import(
  'data:text/javascript;base64,' +
    Buffer.from(bundle.outputFiles[0].text).toString('base64')
);

async function fixture(t, { failInspector = false } = {}) {
  const originalGoogle = globalThis.google,
    originalFixture = globalThis.fleetInteropFixture;
  t.after(() => {
    globalThis.google = originalGoogle;
    globalThis.fleetInteropFixture = originalFixture;
  });
  const noop = () => {};
  const calls = {
    plans: [],
    progress: [],
    stations: [],
    editContexts: [],
    callbacks: [],
    nextRoutes: [],
    currentEtas: [],
    nextClears: 0,
    nextSelectionClears: 0,
    mapStationClicks: 0,
    currentPopupCloses: 0,
    stationPopupCloses: 0,
    fuelEditing: [],
    editingTrucks: [],
    pans: [],
    follows: [],
    inspections: [],
    initialTrucks: [],
    stationVisibility: [],
    ifta: [],
    fits: [],
  };
  const listeners = new Map();
  const state = {
    map: {
      addListener: (event, callback) => {
        listeners.set(event, callback);
        return {
          remove() {
            listeners.delete(event);
          },
        };
      },
      getZoom: () => 5,
      panTo: position => calls.pans.push(position),
    },
    gpuScene: {
      dispose: noop,
      setClusterSelect(callback) {
        state.selectCluster = callback;
      },
      consumeTruckClick() {
        const consumed = state.gpuClick;
        state.gpuClick = false;
        return consumed;
      },
    },
    gpuClick: false,
    selectedNextLoad: null,
    route: {
      fitRemaining() {
        if (state.routeCanFit()) calls.fits.push(state.routeFitPadding(55));
      },
      setPlan(plan, fit, progress) {
        calls.plans.push(plan);
        if (progress !== undefined) calls.progress.push(progress);
      },
      setProgress: value => calls.progress.push(value),
      setEtas: labels => calls.currentEtas.push(labels),
      setRenderedPosition: noop,
      closePopup() {
        calls.currentPopupCloses++;
        state.routePopup?.hide();
      },
      dispose: noop,
    },
    nextLoads: {
      setStopOffset: noop,
      setVisible: noop,
      set: routes => calls.nextRoutes.push(routes),
      clear() {
        calls.nextClears++;
        this.clearSelection();
      },
      clearSelection() {
        calls.nextSelectionClears++;
        if (state.selectedNextLoad !== null) state.selectNextLoad(null, 0);
      },
      dispose: noop,
    },
    trucks: {
      getPosition: () => null,
      isFollowing: () => false,
      clearSelection: noop,
      dispose: noop,
      setInitialTruck: id => calls.initialTrucks.push(id),
      setEditingTruck: id => calls.editingTrucks.push(id),
      setFollow: (id, enabled) => calls.follows.push([id, enabled]),
      releaseCamera: () => calls.follows.push([null, false]),
    },
    stations: {
      setRecommended: async stops => {
        calls.stations.push(stops);
      },
      setProgress: noop,
      setIfta: async value => calls.ifta.push(value),
      setVisible: async value => calls.stationVisibility.push(value),
      setEditContext: context =>
        calls.editContexts.push(context ? { ...context } : null),
      setEditing(station) {
        calls.fuelEditing.push(station);
        return station ? { lat: 40, lng: -79 } : null;
      },
      handleMapClick() {
        calls.mapStationClicks++;
        return false;
      },
      closePopup() {
        calls.stationPopupCloses++;
        state.fuelPopup?.hide();
      },
      dispose: noop,
    },
  };
  globalThis.google = {
    maps: {
      RenderingType: { VECTOR: 'VECTOR' },
      TrafficLayer: class {
        setMap() {}
      },
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
  globalThis.fleetInteropFixture = state;
  const callbacks = {
    invokeMethodAsync(...args) {
      (args[0] === 'OnMapInspectorChanged'
        ? calls.inspections
        : calls.callbacks
      ).push(args);
      return Promise.resolve();
    },
  };
  const viewport = {};
  const native = {
    children: [],
    events: new Map(),
    replaceChildren(...content) {
      this.children = content;
    },
    addEventListener(name, callback) {
      if (failInspector) throw new Error('inspector refused');
      this.events.set(name, callback);
    },
    removeEventListener(name) {
      this.events.delete(name);
    },
  };
  const element = {
    isConnected: true,
    ownerDocument: { defaultView: viewport },
    parentElement: {
      querySelector: selector =>
        selector === '.fleet-map-inspector__native' ? native : null,
    },
  };
  native.ownerDocument = element.ownerDocument;
  native.closest = () => ({ contains: node => node?.insideInspector === true });
  if (failInspector) {
    await assert.rejects(
      createFleetMap(element, 'fixture', callbacks),
      /inspector refused/,
    );
    return { state };
  }
  const api = await createFleetMap(element, 'fixture', callbacks);
  t.after(() => api.dispose());
  return { api, calls, state, listeners, element, viewport, native };
}

test('a mount that fails before the layers exist still gives the provider map back', async t => {
  // Unreleased, the host stays mounted and refuses every later attempt until
  // the page is reloaded.
  const { state } = await fixture(t, { failInspector: true });
  assert.equal(state.released, 1);
});
const bytes = value => new TextEncoder().encode(JSON.stringify(value));
test('map options retain station controls without a truck visibility API', async t => {
  const { api, calls } = await fixture(t);
  assert.equal('setTrucksVisible' in api, false);
  await api.setOptions({
    initialTruckId: 'truck',
    stationsVisible: true,
    trafficVisible: true,
    useIfta: true,
  });
  assert.deepEqual(calls.initialTrucks, ['truck']);
  assert.deepEqual(calls.stationVisibility, [true]);
  assert.deepEqual(calls.ifta, [true]);
});

test('fuel, route and camera overlays suppress marker inspectors without clearing the saved road', async t => {
  const { api, calls, state, native } = await fixture(t);
  await api.setRoute(plan(), null, false);
  state.openFuelStation();
  api.setInspectionSuspended(true);
  const routeCount = calls.plans.length;
  state.openCurrentStop();
  state.openFuelStation();
  state.selectNextLoad('future', 0);
  state.selectTruck('truck');
  assert.deepEqual(native.children, []);
  assert.equal(calls.inspections.at(-1)[1], 'closed');
  assert.equal(
    calls.plans.length,
    routeCount,
    'opening an overlay does not clear or recalculate the road',
  );
  api.setInspectionSuspended(false);
  api.setInspectorMode('truck', 'truck');
  state.openCurrentStop();
  assert.equal(native.children[0].kind, 'stop');
});
test('editing isolates the current truck independently of station focus and resets with route identity', async t => {
  const { api, calls } = await fixture(t);
  await api.setRoute(plan(), null, false);
  api.setFuelEditorTruck('other');
  assert.notEqual(calls.editingTrucks.at(-1), 'other');
  api.setFuelEditorTruck('truck');
  assert.equal(calls.editingTrucks.at(-1), 'truck');
  api.clearFuelStationFocus();
  assert.equal(
    calls.editingTrucks.at(-1),
    'truck',
    'empty drafts and deselected stations retain editing scope',
  );
  api.setFuelEditorTruck(null);
  assert.equal(calls.editingTrucks.at(-1), null);
  api.setFuelEditorTruck('truck');
  api.clearSelection();
  assert.equal(calls.editingTrucks.at(-1), null);
});
test('map omits fullscreen and camera controls while retaining wheel and touch gestures', async t => {
  const { state } = await fixture(t);
  for (const control of ['fullscreenControl', 'cameraControl', 'zoomControl'])
    assert.equal(state.mapOptions[control], false);
  assert.equal(state.mapOptions.gestureHandling, 'greedy');
  assert.equal(state.mapOptions.isFractionalZoomEnabled, true);
});

test('explicit inspector Back and Close restore map focus but marker selections do not steal it', async t => {
  const { api, state, element, native } = await fixture(t),
    focus = [];
  element.focus = options => {
    focus.push(options);
    element.ownerDocument.activeElement = element;
  };
  state.openCurrentStop();
  element.ownerDocument.activeElement = { insideInspector: true };
  api.setInspectorMode('truck', 'truck');
  assert.deepEqual(focus, [{ preventScroll: true }]);
  state.openFuelStation();
  element.ownerDocument.activeElement = { insideInspector: true };
  api.clearMapInspection();
  assert.equal(focus.length, 2);
  state.openFuelStation();
  element.ownerDocument.activeElement = { insideInspector: true };
  native.events.get('keydown')({ key: 'Escape', stopPropagation() {} });
  assert.equal(focus.length, 3);
  element.ownerDocument.activeElement = element;
  state.openCurrentStop();
  state.openFuelStation();
  api.clearMapInspection();
  assert.equal(focus.length, 3);
});
const plan = (id = 'plan') => ({
  id,
  version: 1,
  truckId: 'truck',
  dispatchId: 'current',
  stops: [],
  route: { legs: [] },
});
const etaPlan = (stopId = 'stop') => ({
  ...plan(),
  fromCurrentPosition: true,
  stops: [
    {
      id: stopId,
      point: { latitude: 40, longitude: -79 },
      address: 'Delivery address',
    },
  ],
  route: {
    legs: [
      {
        miles: 100,
        points: [
          { latitude: 40, longitude: -80 },
          { latitude: 40, longitude: -79 },
        ],
      },
    ],
  },
});

function clock(t) {
  let now = Date.parse('2026-09-08T12:00:00Z'),
    id = 0;
  const timers = new Map();
  t.mock.method(Date, 'now', () => now);
  t.mock.method(globalThis, 'setTimeout', (callback, delay) => {
    timers.set(++id, { callback, at: now + delay });
    return id;
  });
  t.mock.method(globalThis, 'clearTimeout', timer => timers.delete(timer));
  return {
    advance(milliseconds) {
      now += milliseconds;
      for (const [key, timer] of [...timers])
        if (timer.at <= now) {
          timers.delete(key);
          timer.callback();
        }
    },
    deadline: () => [...timers.values()].map(timer => timer.at),
  };
}

const etaPayload = () => ({
  planId: 'plan',
  planVersion: 1,
  truckId: 'truck',
  currentDispatchId: 'current',
  eta: {
    calculatedAt: '2026-09-08T12:00:00Z',
    validUntil: '2026-09-08T12:02:00Z',
    stops: [
      {
        dispatchId: 'current',
        stopId: 'stop',
        arrival: '2026-09-08T18:00:00Z',
        timeZoneId: 'Etc/UTC',
        appointment: '2026-09-08T18:00:00Z',
        lateMinutes: 0,
        hours: {
          cycleVerified: true,
          cycleAtArrivalMinutes: -60,
          cycleAfterStopMinutes: -60,
          alternatives: [],
        },
      },
    ],
    cycleAtCalculation: {
      recapVerified: true,
      nextRecapAt: '2026-09-08T12:03:00Z',
      nextRecapMinutes: 180,
      homeTimeZoneId: 'Etc/UTC',
    },
  },
});

test('zoom settles into hybrid at 15 and roadmap below without redundant replacements', async t => {
  const { api, state, listeners } = await fixture(t);
  let mapType = 'roadmap';
  const replacements = [];
  state.map.getMapTypeId = () => mapType;
  state.map.setMapTypeId = value => {
    mapType = value;
    replacements.push(value);
  };
  for (const [zoom, expectedType, expectedReplacements] of [
    [5, 'roadmap', 0],
    [14.9, 'roadmap', 0],
    [15, 'hybrid', 1],
    [15.1, 'hybrid', 1],
    [16, 'hybrid', 1],
    [14.9, 'roadmap', 2],
    [15, 'hybrid', 3],
    [6, 'roadmap', 4],
  ]) {
    state.map.getZoom = () => zoom;
    const previousType = mapType;
    listeners.get('zoom_changed')?.();
    assert.equal(
      mapType,
      previousType,
      'base-map changes wait for camera idle',
    );
    listeners.get('idle')();
    listeners.get('idle')();
    assert.equal(mapType, expectedType);
    assert.equal(
      replacements.length,
      expectedReplacements,
      'repeated idle does not replace unchanged tiles',
    );
  }
  assert.deepEqual(replacements, ['hybrid', 'roadmap', 'hybrid', 'roadmap']);
  api.dispose();
  assert.equal(
    listeners.has('idle'),
    false,
    'disposal removes the map-type listener',
  );
});

test('fuel marker visits use server future miles and invalidation clears only recommendations', async t => {
  const { api, calls } = await fixture(t);
  await api.setNextLoadsVisible(true);
  const route = {
    ...plan(),
    fuelPlan: {
      stops: [
        { stationId: 'same', visitKey: 'far', buyGallons: 40, milesAhead: 200 },
        {
          stationId: 'same',
          visitKey: 'near',
          buyGallons: 20,
          currentRouteMile: null,
          milesAhead: 40,
        },
      ],
    },
  };
  await api.setRoute(route, { progressMiles: 100 }, false);
  assert.equal(calls.stations.at(-1).length, 1);
  assert.equal(calls.stations.at(-1)[0].visitKey, 'near');
  assert.equal(calls.stations.at(-1)[0].miles, 40);
  route.fuelPlan.stops[1].milesAhead = 35;
  await api.setRoute(route, { progressMiles: 105 }, false);
  assert.equal(calls.stations.at(-1)[0].miles, 35);
  await api.setRoute(
    { ...route, fuelPlan: { ...route.fuelPlan, needsRefresh: true } },
    { progressMiles: 105 },
    false,
  );
  assert.deepEqual(calls.stations.at(-1), []);
  assert.equal(calls.plans.at(-1).id, route.id);
});

test('Next loads filters saved fuel visits immediately without requesting new geometry or calculations', async t => {
  const { api, calls, state } = await fixture(t);
  const saved = {
    ...plan(),
    fuelPlan: {
      stops: [
        {
          stationId: 'same',
          dispatchId: 'current',
          number: 1,
          buyGallons: 20,
          currentRouteMile: 120,
          milesAhead: 20,
        },
        {
          stationId: 'same',
          dispatchId: 'future',
          number: 2,
          buyGallons: 50,
          milesAhead: 500,
        },
        {
          stationId: 'future-only',
          dispatchId: 'future',
          number: 3,
          buyGallons: 60,
          milesAhead: 700,
        },
      ],
    },
  };
  const snapshot = structuredClone(saved);
  await api.setRoute(saved, { progressMiles: 100 }, false);
  assert.deepEqual(
    calls.stations.at(-1).map(x => [x.id, x.numbers]),
    [['same', '1']],
  );
  const plans = calls.plans.length;
  await api.setNextLoadsVisible(true);
  assert.deepEqual(
    calls.stations.at(-1).map(x => [x.id, x.numbers]),
    [
      ['same', '1/2'],
      ['future-only', '3'],
    ],
  );
  state.routeProgress('truck', 125, 20);
  const callbacks = calls.callbacks.length;
  await api.setNextLoadsVisible(false);
  assert.deepEqual(
    calls.stations.at(-1),
    [],
    'a passed visit must not reappear on toggling',
  );
  await api.setNextLoadsVisible(true);
  assert.deepEqual(
    calls.stations.at(-1).map(x => [x.id, x.numbers]),
    [
      ['same', '2'],
      ['future-only', '3'],
    ],
  );
  assert.equal(calls.plans.length, plans);
  assert.equal(calls.callbacks.length, callbacks);
  assert.deepEqual(saved, snapshot);
  await api.clearSelection();
  await api.setNextLoadsVisible(false);
  await api.setNextLoadsVisible(true);
  assert.deepEqual(calls.stations.at(-1), []);
});

test('latest Next loads visibility wins while route recommendation rendering is pending', async t => {
  const { api, calls, state } = await fixture(t);
  await api.setNextLoadsVisible(true);
  let finish;
  const pending = new Promise(resolve => {
    finish = resolve;
  });
  state.stations.setRecommended = stops => {
    calls.stations.push(stops);
    return pending;
  };
  const applying = api.setRoute(
    {
      ...plan(),
      fuelPlan: {
        stops: [
          {
            stationId: 'a',
            dispatchId: 'current',
            buyGallons: 20,
            milesAhead: 10,
          },
          {
            stationId: 'b',
            dispatchId: 'future',
            buyGallons: 50,
            milesAhead: 500,
          },
        ],
      },
    },
    null,
    false,
  );
  await Promise.resolve();
  const hiding = api.setNextLoadsVisible(false);
  assert.deepEqual(
    calls.stations.at(-1).map(x => x.id),
    ['a'],
  );
  finish();
  await Promise.all([applying, hiding]);
  assert.deepEqual(
    calls.stations.at(-1).map(x => x.id),
    ['a'],
  );
});

test('stop ETA updates preserve geometry, reject another route identity and expire without a provider refresh', async t => {
  const { api, calls } = await fixture(t);
  const now = Date.parse('2026-09-08T12:00:00Z');
  t.mock.method(Date, 'now', () => now);
  let expiry;
  t.mock.method(globalThis, 'setTimeout', (callback, delay) => {
    expiry = callback;
    assert.equal(delay, 120000);
    return 1;
  });
  t.mock.method(globalThis, 'clearTimeout', () => {});
  await api.setRoute(etaPlan('current-stop'), null, false);
  const geometryUpdates = calls.plans.length;
  const eta = {
    validUntil: '2026-09-08T12:02:00Z',
    stops: [
      {
        dispatchId: 'current',
        stopId: 'current-stop',
        arrival: '2026-09-08T18:00:00Z',
        timeZoneId: 'Etc/UTC',
      },
      {
        dispatchId: 'future',
        stopId: 'future-stop',
        arrival: '2026-09-09T18:00:00Z',
        timeZoneId: 'Etc/UTC',
      },
    ],
  };
  const payload = {
    planId: 'plan',
    planVersion: 1,
    truckId: 'truck',
    currentDispatchId: 'current',
    eta,
  };
  api.setStopEtas(payload);
  assert.deepEqual([...calls.currentEtas.at(-1).keys()], ['current-stop']);
  assert.match(calls.currentEtas.at(-1).get('current-stop').text, /Sep 8/);
  assert.equal(calls.plans.length, geometryUpdates);
  const updates = calls.currentEtas.length;
  api.setStopEtas({ ...payload, planId: 'obsolete' });
  assert.equal(calls.currentEtas.length, updates);
  api.setNextLoadsBytes(
    bytes({ truckId: 'truck', currentDispatchId: 'current', routes: [] }),
  );
  assert.equal(
    calls.currentEtas.length,
    updates,
    'future geometry does not republish stop ETA text',
  );
  expiry();
  assert.equal(calls.currentEtas.at(-1).size, 0);
});

test('pending stop ETA keeps its original bounded deadline across polling and clears when dismissed', async t => {
  const { api, calls } = await fixture(t);
  let now = Date.parse('2026-09-08T12:01:00Z');
  t.mock.method(Date, 'now', () => now);
  let expiry, delay;
  t.mock.method(globalThis, 'setTimeout', (callback, remaining) => {
    expiry = callback;
    delay = remaining;
    return 1;
  });
  let cleared = 0;
  t.mock.method(globalThis, 'clearTimeout', () => {
    cleared++;
  });
  await api.setRoute(etaPlan(), null, false);
  const payload = {
    planId: 'plan',
    planVersion: 1,
    truckId: 'truck',
    currentDispatchId: 'current',
    eta: {
      validUntil: '2026-09-08T12:00:00Z',
      routeUpdatePending: true,
      stops: [
        {
          dispatchId: 'current',
          stopId: 'stop',
          arrival: '2026-09-08T18:00:00Z',
          timeZoneId: 'Etc/UTC',
          appointment: '2026-09-08T18:00:00Z',
          lateMinutes: 0,
        },
      ],
    },
  };
  api.setStopEtas(payload);
  assert.equal(calls.currentEtas.at(-1).get('stop').statusText, 'On time');
  assert.equal(calls.currentEtas.at(-1).get('stop').tone, 'success');
  assert.equal(delay, 14 * 60_000);
  now += 60_000;
  api.setStopEtas(payload);
  assert.equal(delay, 13 * 60_000, 'polling cannot move the original expiry');
  expiry();
  assert.equal(calls.currentEtas.at(-1).size, 0);
  api.setStopEtas(payload);
  const beforeClear = cleared;
  await api.clearSelection();
  assert.ok(cleared > beforeClear);
  assert.equal(calls.currentEtas.at(-1).size, 0);
});

test('an in-flight refresh retains the original popup and deadline across expiry and empty polling', async t => {
  const { api, calls, state } = await fixture(t);
  const time = clock(t);
  const originalDocument = globalThis.document;
  globalThis.document = {
    createElement: tagName => ({
      tagName,
      children: [],
      append(...children) {
        this.children.push(...children);
      },
      setAttribute() {},
      addEventListener() {},
    }),
  };
  t.after(() => {
    globalThis.document = originalDocument;
  });
  let popup;
  const markers = [];
  const stops = createRouteStops(
    {},
    class {
      constructor(options) {
        Object.assign(this, options);
        markers.push(this);
      }
    },
    {
      show(content) {
        popup = content;
      },
      hide() {
        popup = null;
      },
    },
    () => {},
  );
  t.after(() => stops.clear());
  state.route.setPlan = value => {
    calls.plans.push(value);
    stops.setPlan(value);
  };
  state.route.setEtas = labels => {
    calls.currentEtas.push(labels);
    stops.setEtas(labels);
  };
  await api.setRoute(etaPlan(), null, false);
  const geometryUpdates = calls.plans.length;
  const payload = etaPayload();
  const originalPayload = structuredClone(payload);
  api.setStopEtas(payload);
  markers[0].onSelect();
  const content = popup;
  const complete = calls.currentEtas.at(-1);
  assert.ok(complete.get('stop').hours.some(row => row.label === 'Next recap'));
  time.advance(60_000);
  api.setStopEtas({ ...payload, refreshing: true });
  const deadline = Date.parse('2026-09-08T12:17:00Z');
  assert.deepEqual(time.deadline(), [deadline]);
  time.advance(120_000);
  assert.equal(
    popup,
    content,
    'expiry during the request does not change the open popup',
  );
  assert.equal(calls.currentEtas.at(-1), complete);
  api.setStopEtas({
    ...payload,
    refreshing: false,
    eta: {
      calculatedAt: '2026-09-08T12:03:00Z',
      validUntil: '2026-09-08T12:05:00Z',
      routeUpdatePending: true,
      stops: [],
    },
  });
  api.setStopEtas({ ...payload, refreshing: false, eta: null });
  assert.deepEqual(
    time.deadline(),
    [deadline],
    'empty responses cannot slide the saved deadline',
  );
  assert.equal(
    popup,
    content,
    'saved recap, text and tone remain byte-for-byte display-equivalent',
  );
  assert.equal(
    calls.plans.length,
    geometryUpdates,
    'ETA refresh never republishes route geometry',
  );
  assert.deepEqual(
    payload,
    originalPayload,
    'refresh handling never rewrites the server calculation or validity times',
  );
  time.advance(14 * 60_000 - 1);
  assert.equal(calls.currentEtas.at(-1), complete);
  time.advance(1);
  assert.equal(calls.currentEtas.at(-1).size, 0);
  assert.notEqual(
    popup,
    content,
    'the open popup loses the expired ETA at the original grace deadline',
  );
});

test('a complete response ends refreshing and expires at its own ordinary deadline', async t => {
  const { api, calls } = await fixture(t);
  const time = clock(t);
  await api.setRoute(etaPlan(), null, false);
  const payload = etaPayload();
  api.setStopEtas(payload);
  api.setStopEtas({ ...payload, refreshing: true });
  time.advance(180_000);
  const ready = {
    ...payload,
    refreshing: false,
    eta: {
      ...payload.eta,
      calculatedAt: '2026-09-08T12:03:00Z',
      validUntil: '2026-09-08T12:04:00Z',
      stops: payload.eta.stops.map(stop => ({
        ...stop,
        arrival: '2026-09-08T19:00:00Z',
        lateMinutes: 60,
      })),
    },
  };
  api.setStopEtas(ready);
  assert.match(calls.currentEtas.at(-1).get('stop').arrivalText, /07:00 PM/);
  assert.deepEqual(time.deadline(), [Date.parse('2026-09-08T12:04:00Z')]);
  time.advance(59_999);
  assert.equal(calls.currentEtas.at(-1).size, 1);
  time.advance(1);
  assert.equal(calls.currentEtas.at(-1).size, 0);
});

test('a verified unavailable response clears the refreshing ETA and a fresh result resumes normally', async t => {
  const { api, calls } = await fixture(t);
  const time = clock(t);
  await api.setRoute(etaPlan(), null, false);
  const payload = etaPayload();
  api.setStopEtas(payload);
  api.setStopEtas({ ...payload, refreshing: true });
  time.advance(180_000);
  assert.equal(calls.currentEtas.at(-1).size, 1);
  api.setStopEtas({
    ...payload,
    refreshing: false,
    eta: {
      calculatedAt: '2026-09-08T12:03:00Z',
      validUntil: '2026-09-08T12:05:00Z',
      routeUpdatePending: false,
      stops: [],
      unavailableReason: 'HOS unavailable',
    },
  });
  assert.equal(
    calls.currentEtas.at(-1).size,
    0,
    'verified unavailability must not display the previous ETA',
  );
  assert.deepEqual(
    time.deadline(),
    [],
    'unavailability cancels the pending grace timer',
  );
  api.setStopEtas({ ...payload, refreshing: false, eta: null });
  assert.equal(
    calls.currentEtas.at(-1).size,
    0,
    'a later transient gap cannot revive the cleared forecast',
  );
  const ready = {
    ...payload,
    refreshing: false,
    eta: {
      ...payload.eta,
      calculatedAt: '2026-09-08T12:03:00Z',
      validUntil: '2026-09-08T12:04:00Z',
    },
  };
  api.setStopEtas(ready);
  assert.equal(calls.currentEtas.at(-1).size, 1);
  assert.deepEqual(time.deadline(), [Date.parse('2026-09-08T12:04:00Z')]);
  time.advance(60_000);
  assert.equal(
    calls.currentEtas.at(-1).size,
    0,
    'the resumed forecast expires without inherited refresh grace',
  );
});

test('an older complete reply cannot replace a newer ETA or change its bounded deadline', async t => {
  const { api, calls } = await fixture(t);
  const time = clock(t);
  await api.setRoute(etaPlan(), null, false);
  const payload = etaPayload();
  api.setStopEtas(payload);
  const complete = calls.currentEtas.at(-1);
  const older = {
    ...payload,
    refreshing: false,
    eta: {
      ...payload.eta,
      calculatedAt: '2026-09-08T11:59:00Z',
      validUntil: '2026-09-08T12:05:00Z',
      stops: payload.eta.stops.map(stop => ({
        ...stop,
        arrival: '2026-09-08T17:00:00Z',
      })),
    },
  };
  api.setStopEtas(older);
  assert.equal(calls.currentEtas.at(-1), complete);
  assert.deepEqual(time.deadline(), [Date.parse('2026-09-08T12:02:00Z')]);
  api.setStopEtas({ ...payload, refreshing: true });
  time.advance(180_000);
  api.setStopEtas(older);
  assert.equal(calls.currentEtas.at(-1), complete);
  assert.deepEqual(time.deadline(), [Date.parse('2026-09-08T12:17:00Z')]);
  time.advance(14 * 60_000);
  assert.equal(calls.currentEtas.at(-1).size, 0);
});

test('safe geometry changes retain refreshing ETA but real stop context changes clear it', async t => {
  const { api, calls } = await fixture(t);
  clock(t);
  let route = etaPlan();
  const payload = etaPayload();
  await api.setRoute(route, null, false);
  api.setStopEtas(payload);
  const complete = calls.currentEtas.at(-1);
  api.setStopEtas({ ...payload, refreshing: true });
  route = { ...route, version: 2, route: structuredClone(route.route) };
  route.route.legs[0].miles = 110;
  await api.setRoute(route, null, false);
  assert.equal(calls.currentEtas.at(-1), complete);
  assert.equal(
    calls.plans.at(-1).route.legs[0].miles,
    110,
    'retaining ETA must not retain old geometry',
  );
  api.setStopEtas({ ...payload, planVersion: 2, refreshing: true });
  assert.equal(calls.currentEtas.at(-1), complete);
  for (const change of [
    value => {
      value.stops[0].point.latitude += 1;
    },
    value => {
      value.stops[0].scheduledDate = '2026-09-09';
    },
    value => {
      value.stops[0].id = 'different';
    },
    value => {
      value.tracking = { passedStopIds: ['stop'] };
    },
    value => {
      value.tracking = { allStopsPassed: true };
    },
    value => {
      value.dispatchId = 'other';
    },
    value => {
      value.truckId = 'other';
    },
  ]) {
    await api.setRoute(etaPlan(), null, false);
    api.setStopEtas(payload);
    api.setStopEtas({ ...payload, refreshing: true });
    const changed = etaPlan();
    change(changed);
    await api.setRoute(changed, null, false);
    assert.equal(calls.currentEtas.at(-1).size, 0);
  }
});

test('execution-leg callbacks include current and selected leg ownership', async t => {
  const { api, calls, state } = await fixture(t);
  const payload = {
    truckId: 'truck-a',
    currentDispatchId: 'shared-load',
    currentExecutionLegId: 'current-leg',
    currentAssignmentRevision: 3,
    routes: [{ id: 'shared-load', executionLegId: 'next-leg' }],
  };
  api.setNextLoadsBytes(bytes(payload));
  state.selectNextLoad('shared-load', 1, 'next-leg');
  assert.deepEqual(calls.callbacks.at(-1), [
    'OnNextExecutionLegSelected',
    'truck-a',
    'shared-load',
    'shared-load',
    1,
    'next-leg',
    'current-leg',
    3,
  ]);
  api.setNextLoadsBytes(
    bytes({
      ...payload,
      currentAssignmentRevision: 4,
    }),
  );
  assert.equal(calls.nextClears, 1);
});

test('next-load selection callbacks carry the payload truck, current load, selected load and stop index', async t => {
  const { api, calls, state } = await fixture(t);
  const payload = {
    truckId: 'truck-a',
    currentDispatchId: 'current-a',
    routes: [{ id: 'next-a' }],
    labels: [{ id: 'next-a', names: ['Pickup', 'Delivery'] }],
  };
  api.setNextLoadsBytes(bytes(payload));
  assert.deepEqual(calls.nextRoutes, [payload.routes]);
  state.selectNextLoad('next-a', 1);
  assert.deepEqual(calls.callbacks, [
    ['OnNextLoadSelected', 'truck-a', 'current-a', 'next-a', 1],
  ]);
  api.setNextLoadsBytes(
    bytes({ ...payload, routes: [{ id: 'next-a', stops: [] }] }),
  );
  api.setNextLoadsBytes(
    bytes({ labels: [{ id: 'next-a', names: ['Renamed pickup'] }] }),
  );
  assert.equal(
    calls.nextRoutes.length,
    2,
    'label-only metadata does not mutate the renderer',
  );
  assert.equal(calls.nextClears, 0);
  assert.equal(state.selectedNextLoad, 'next-a');
  api.clearNextLoadSelection();
  assert.deepEqual(calls.callbacks.at(-1), [
    'OnNextLoadSelected',
    'truck-a',
    'current-a',
    null,
    0,
  ]);
  assert.equal(
    calls.nextClears,
    0,
    'clearing selection preserves rendered routes',
  );
  state.selectNextLoad('next-a', 0);
  api.setNextLoadsBytes(
    bytes({
      truckId: 'truck-b',
      currentDispatchId: 'current-b',
      routes: [{ id: 'next-b' }],
    }),
  );
  assert.equal(calls.nextClears, 1);
  assert.equal(state.selectedNextLoad, null);
  assert.deepEqual(calls.callbacks.at(-1), [
    'OnNextLoadSelected',
    'truck-a',
    'current-a',
    null,
    0,
  ]);
  state.selectNextLoad('next-b', 2);
  assert.deepEqual(calls.callbacks.at(-1), [
    'OnNextLoadSelected',
    'truck-b',
    'current-b',
    'next-b',
    2,
  ]);
  api.clearNextLoads();
  assert.equal(calls.nextClears, 2);
  const count = calls.callbacks.length;
  state.selectNextLoad('late', 0);
  assert.equal(
    calls.callbacks.length,
    count,
    'cleared route payloads cannot supply a stale callback identity',
  );
});

for (const order of ['stop first', 'map first']) {
  test(`stop selection consumes the associated map click when callbacks arrive ${order}`, async t => {
    const { api, calls, state, listeners } = await fixture(t);
    api.setNextLoadsBytes(
      bytes({
        truckId: 'truck',
        currentDispatchId: 'current',
        routes: [{ id: 'next' }],
      }),
    );
    const pickStop = () => {
      state.gpuClick = true;
      state.selectNextLoad('next', 1);
    };
    if (order === 'stop first') pickStop();
    listeners.get('click')({});
    if (order === 'map first') pickStop();
    await Promise.resolve();
    assert.equal(calls.nextSelectionClears, 0);
    assert.equal(calls.mapStationClicks, 0);
    assert.equal(state.selectedNextLoad, 'next');
    assert.deepEqual(calls.callbacks, [
      ['OnNextLoadSelected', 'truck', 'current', 'next', 1],
    ]);
    listeners.get('click')({});
    await Promise.resolve();
    assert.equal(calls.nextSelectionClears, 0);
    assert.equal(calls.mapStationClicks, 1);
    assert.equal(state.selectedNextLoad, 'next');
    assert.deepEqual(calls.callbacks, [
      ['OnNextLoadSelected', 'truck', 'current', 'next', 1],
      ['OnMapBackgroundClicked', 'truck', calls.inspections.at(-1).at(-1)],
    ]);
  });
}

test('truck selection clears the next-load selection before notifying the selected truck', async t => {
  const { api, calls, state } = await fixture(t);
  api.setNextLoadsBytes(
    bytes({
      truckId: 'truck-a',
      currentDispatchId: 'current',
      routes: [{ id: 'next' }],
    }),
  );
  state.selectNextLoad('next', 0);
  state.selectTruck('truck-b');
  assert.equal(state.selectedNextLoad, null);
  assert.equal(calls.nextSelectionClears, 1);
  assert.deepEqual(calls.callbacks.slice(-2), [
    ['OnNextLoadSelected', 'truck-a', 'current', null, 0],
    ['OnTruckSelected', 'truck-b'],
  ]);
});

test('one shared inspector switches native stop, fuel and future content without stale owner reopening', async t => {
  const { api, state, calls, native } = await fixture(t);
  await api.setRoute(plan(), null, false);
  api.setNextLoadsBytes(
    bytes({
      truckId: 'truck',
      currentDispatchId: 'current',
      routes: [{ id: 'future' }],
    }),
  );
  const stop = { name: 'current stop HTML' },
    fuel = { name: 'fuel HTML' };
  state.openCurrentStop(stop);
  assert.deepEqual(native.children, [stop]);
  assert.deepEqual(calls.inspections.at(-1), [
    'OnMapInspectorChanged',
    'stop',
    'truck',
    1,
  ]);
  state.openFuelStation(fuel);
  state.routePopup.hide();
  state.routePopup.show({ name: 'late stop ETA' });
  assert.deepEqual(native.children, [fuel]);
  assert.deepEqual(calls.inspections.at(-1), [
    'OnMapInspectorChanged',
    'fuel',
    'truck',
    2,
  ]);
  state.selectNextLoad('future', 1);
  state.fuelPopup.show({ name: 'late fuel price' });
  assert.deepEqual(native.children, []);
  assert.deepEqual(calls.inspections.at(-1), [
    'OnMapInspectorChanged',
    'next-stop',
    'truck',
    3,
  ]);
  api.setInspectorMode('truck', 'truck');
  assert.equal(state.selectedNextLoad, null);
  assert.deepEqual(calls.inspections.at(-1), [
    'OnMapInspectorChanged',
    'truck',
    'truck',
    4,
  ]);
  state.selectTruck('other');
  assert.deepEqual(calls.inspections.at(-1), [
    'OnMapInspectorChanged',
    'truck',
    'other',
    5,
  ]);
  state.fuelPopup.show(fuel);
  state.routePopup.hide();
  assert.deepEqual(native.children, []);
  assert.deepEqual(
    calls.inspections.map(row => row.at(-1)),
    [1, 2, 3, 4, 5],
  );
});

test('empty map clicks cannot dismiss an editor or a closed inspector', async t => {
  const { api, state, calls, listeners } = await fixture(t);
  listeners.get('click')({});
  await Promise.resolve();
  state.openFuelStation();
  api.setInspectionSuspended(true);
  listeners.get('click')({});
  await Promise.resolve();
  assert.equal(
    calls.callbacks.some(call => call[0] === 'OnMapBackgroundClicked'),
    false,
  );
});

test('detail Close preserves the road; blank clicks request owned dismissal', async t => {
  const { api, state, calls, native, listeners } = await fixture(t);
  await api.setRoute(plan(), null, false);
  let clearedTruck = 0;
  state.trucks.clearSelection = () => clearedTruck++;
  const planCount = calls.plans.length,
    contexts = calls.editContexts.length;
  state.openFuelStation();
  api.clearMapInspection();
  assert.deepEqual(calls.inspections.at(-1).slice(0, 3), [
    'OnMapInspectorChanged',
    'closed',
    'truck',
  ]);
  assert.deepEqual(native.children, []);
  assert.equal(calls.plans.length, planCount);
  assert.equal(calls.editContexts.length, contexts);
  assert.equal(clearedTruck, 0);
  state.openCurrentStop();
  const stopContent = native.children[0];
  const inspections = calls.inspections.length;
  listeners.get('click')({});
  await Promise.resolve();
  assert.deepEqual(native.children, [stopContent]);
  assert.equal(
    calls.inspections.length,
    inspections,
    'Blazor owns the selection transition',
  );
  assert.deepEqual(calls.callbacks.at(-1), [
    'OnMapBackgroundClicked',
    'truck',
    calls.inspections.at(-1).at(-1),
  ]);
  api.setInspectorMode('truck', 'truck');
  const truckInspections = calls.inspections.length;
  for (let i = 0; i < 3; i++) {
    listeners.get('click')({});
    await Promise.resolve();
  }
  assert.equal(calls.inspections.length, truckInspections);
  assert.equal(calls.inspections.at(-1)[1], 'truck');
  assert.deepEqual(calls.callbacks.at(-1), [
    'OnMapBackgroundClicked',
    'truck',
    calls.inspections.at(-1).at(-1),
  ]);
  state.openFuelStation();
  native.events.get('keydown')({ key: 'Escape', stopPropagation() {} });
  assert.equal(
    calls.inspections.at(-1)[1],
    'truck',
    'Escape returns native details to the current truck',
  );
  assert.equal(clearedTruck, 0);
  assert.equal(calls.plans.length, planCount);
  assert.equal(
    api.focusFuelStation({
      truckId: 'truck',
      dispatchId: 'current',
      stationId: 'fuel',
    }),
    true,
    'closing inspection retains the accepted current route context',
  );
  api.dispose();
  const before = calls.inspections.length;
  api.clearMapInspection();
  api.setInspectorMode('truck');
  state.fuelPopup.show({ late: true });
  assert.equal(calls.inspections.length, before);
  assert.equal(native.events.size, 0);
});

test('exploring a truck group preserves the selection, inspector and route', async t => {
  const { api, state, calls, listeners } = await fixture(t);
  await api.setRoute(plan(), null, false);
  state.selectTruck('truck');
  api.setNextLoadsBytes(
    bytes({
      truckId: 'truck',
      currentDispatchId: 'current',
      routes: [{ id: 'next' }],
    }),
  );
  let cleared = 0;
  state.trucks.clearSelection = () => {
    cleared++;
    state.selectTruck(null);
  };
  const before = structuredClone(calls);

  listeners.get('click')({});
  state.gpuClick = true;
  state.selectCluster();
  await Promise.resolve();

  assert.equal(cleared, 0);
  assert.deepEqual(calls.follows, [[null, false]]);
  assert.deepEqual({ ...calls, follows: before.follows }, before);
  assert.equal(calls.inspections.at(-1)[1], 'truck');
  assert.equal(
    api.focusFuelStation({
      truckId: 'truck',
      dispatchId: 'current',
      stationId: 'fuel',
    }),
    true,
    'the accepted route and fuel context remain available',
  );
});

test('exploring a truck group without a selection does not select a member', async t => {
  const { state, calls } = await fixture(t);
  state.selectCluster();
  assert.deepEqual(calls.follows, [[null, false]]);
  assert.deepEqual(calls.callbacks, []);
  assert.deepEqual(calls.inspections, []);
  assert.deepEqual(calls.plans, []);
});

test('deselecting a truck clears its route, follow selection and fuel context', async t => {
  const { api, state, calls } = await fixture(t);
  await api.setRoute(plan(), null, false);
  let clearedTruck = 0;
  state.trucks.clearSelection = () => {
    clearedTruck++;
    state.selectTruck(null);
  };
  api.setInspectorMode('truck', 'truck');
  await api.clearSelection();
  assert.equal(clearedTruck, 1);
  assert.equal(calls.plans.at(-1), null);
  assert.equal(calls.progress.at(-1), null);
  assert.equal(calls.editContexts.at(-1), null);
  assert.equal(calls.editingTrucks.at(-1), null);
  assert.deepEqual(calls.inspections.at(-1).slice(0, 3), [
    'OnMapInspectorChanged',
    'closed',
    null,
  ]);
  assert.equal(
    api.focusFuelStation({
      truckId: 'truck',
      dispatchId: 'current',
      stationId: 'fuel',
    }),
    false,
  );
});

for (const [card, open] of [
  ['current stop', 'openCurrentStop'],
  ['fuel station', 'openFuelStation'],
]) {
  test(`opening a ${card} card clears the future-load card and retains the route identity`, async t => {
    const { api, calls, state } = await fixture(t);
    const payload = {
      truckId: 'truck-a',
      currentDispatchId: 'current-a',
      routes: [{ id: 'next-a' }],
    };
    api.setNextLoadsBytes(bytes(payload));
    state.selectNextLoad('next-a', 1);
    const ownPopupCloses =
      card === 'current stop' ? 'currentPopupCloses' : 'stationPopupCloses';
    const closesBefore = calls[ownPopupCloses];

    state[open]();

    assert.equal(state.selectedNextLoad, null);
    assert.equal(calls.nextSelectionClears, 1);
    assert.deepEqual(calls.callbacks, [
      ['OnNextLoadSelected', 'truck-a', 'current-a', 'next-a', 1],
      ['OnNextLoadSelected', 'truck-a', 'current-a', null, 0],
    ]);
    assert.equal(calls.nextClears, 0);
    assert.deepEqual(calls.nextRoutes, [payload.routes]);
    assert.equal(
      calls[ownPopupCloses],
      closesBefore,
      'future deselection must not close the newly selected card',
    );

    state[open]();
    assert.equal(
      calls.callbacks.length,
      2,
      'an already cleared selection does not notify again',
    );
    state.selectNextLoad('next-a', 0);
    assert.deepEqual(calls.callbacks.at(-1), [
      'OnNextLoadSelected',
      'truck-a',
      'current-a',
      'next-a',
      0,
    ]);
  });
}

test('disposing before a queued map click prevents callbacks and layer mutations', async t => {
  const { api, calls, state, listeners } = await fixture(t);
  api.setNextLoadsBytes(
    bytes({
      truckId: 'truck',
      currentDispatchId: 'current',
      routes: [{ id: 'next' }],
    }),
  );
  state.selectNextLoad('next', 0);
  const before = calls.callbacks.length;
  listeners.get('click')({});
  api.dispose();
  await Promise.resolve();
  assert.equal(calls.nextSelectionClears, 0);
  assert.equal(calls.mapStationClicks, 0);
  assert.equal(calls.callbacks.length, before);
});

test('interop acknowledges full and metadata updates and rejects lost or mismatched geometry before mutation', async t => {
  const { api, calls } = await fixture(t);
  const full = plan();
  assert.equal(await api.setRouteBytes(bytes(full), null, false), true);
  const retained = calls.plans.at(-1).route;
  const metadata = {
    ...full,
    geometryOmitted: true,
    fuelPlan: null,
    referenceStops: null,
  };
  delete metadata.route;
  assert.equal(
    await api.setRouteBytes(bytes(metadata), { progressMiles: 10 }, false),
    true,
  );
  assert.equal(calls.plans.at(-1).route, retained);
  assert.equal(calls.plans.at(-1).fuelPlan, null);
  const before = calls.plans.length;
  for (const change of [{ id: 'other' }, { truckId: 'other' }, { version: 2 }])
    assert.equal(
      await api.setRouteBytes(bytes({ ...metadata, ...change }), null, false),
      false,
    );
  assert.equal(calls.plans.length, before);
  await api.clearSelection();
  const cleared = calls.plans.length;
  assert.equal(await api.setRouteBytes(bytes(metadata), null, false), false);
  assert.equal(calls.plans.length, cleared);
  assert.equal(await api.setRouteBytes(bytes(full), null, false), true);
});

test('route and progress are applied atomically before the first rendered-position update', async t => {
  const { api, state } = await fixture(t);
  const calls = [],
    saved = plan(),
    progress = { progressMiles: 150 };
  state.route.setPlan = (value, fit, initialProgress) =>
    calls.push(['plan', value, fit, initialProgress]);
  state.route.setProgress = () =>
    assert.fail(
      'route application must not draw a separate unknown-progress frame',
    );
  state.route.setRenderedPosition = () => calls.push(['position']);
  await api.setRoute(saved, progress, true);
  assert.deepEqual(calls, [['plan', saved, true, progress], ['position']]);
});

test('fuel editing context follows only accepted current-route identity and clears on completion or deselection', async t => {
  const { api, calls } = await fixture(t);
  const first = plan();
  assert.equal(await api.setRoute(first, null, false), true);
  assert.deepEqual(calls.editContexts.at(-1), {
    truckId: 'truck',
    dispatchId: 'current',
  });
  const initialUpdates = calls.editContexts.length;
  api.setNextLoadsBytes(
    bytes({
      truckId: 'truck',
      currentDispatchId: 'current',
      routes: [{ id: 'future' }],
    }),
  );
  assert.equal(
    calls.editContexts.length,
    initialUpdates,
    'future loads do not replace the authoritative edit context',
  );

  const metadata = { ...first, geometryOmitted: true, truckId: 'obsolete' };
  delete metadata.route;
  assert.equal(await api.setRoute(metadata, null, false), false);
  assert.equal(
    calls.editContexts.length,
    initialUpdates,
    'rejected route metadata cannot change editor ownership',
  );

  const next = { ...plan('next'), truckId: 'truck-b', dispatchId: 'current-b' };
  assert.equal(await api.setRoute(next, null, false), true);
  assert.deepEqual(calls.editContexts.at(-1), {
    truckId: 'truck-b',
    dispatchId: 'current-b',
  });
  assert.equal(
    await api.setRoute(
      { ...next, tracking: { allStopsPassed: true } },
      null,
      false,
    ),
    true,
  );
  assert.equal(
    calls.editContexts.at(-1),
    null,
    'completed loads cannot accept new fuel edits',
  );
  await api.setRoute(next, null, false);
  await api.clearSelection();
  assert.equal(calls.editContexts.at(-1), null);
});

test('fuel station edit callback carries the exact selected visit identity and stops after disposal', async t => {
  const { api, calls, state } = await fixture(t);
  await api.setRoute(plan(), null, false);
  const selection = {
    truckId: 'truck',
    dispatchId: 'current',
    stationId: 'station',
    name: 'Test fuel',
    beforeStopId: 'delivery',
    addNew: false,
  };
  state.editFuelStation(selection);
  assert.deepEqual(calls.callbacks.at(-1), [
    'OnFuelStationEdit',
    'truck',
    'current',
    'station',
    'Test fuel',
    'delivery',
    false,
  ]);
  state.editFuelStation({ ...selection, beforeStopId: null, addNew: true });
  assert.deepEqual(calls.callbacks.at(-1), [
    'OnFuelStationEdit',
    'truck',
    'current',
    'station',
    'Test fuel',
    null,
    true,
  ]);
  const count = calls.callbacks.length;
  api.dispose();
  state.editFuelStation(selection);
  assert.equal(
    calls.callbacks.length,
    count,
    'a stale station callback cannot reopen a disposed editor',
  );
});

test('editor selection alone pans to its station, pauses follow and blocks late route fitting without changing toggles', async t => {
  const { api, calls, state } = await fixture(t);
  await api.setRoute(plan(), null, false);
  const station = {
    truckId: 'truck',
    dispatchId: 'current',
    stationId: 'station',
  };
  assert.equal(api.focusFuelStation({ ...station, truckId: 'other' }), false);
  assert.equal(
    api.focusFuelStation({ ...station, dispatchId: 'future' }),
    false,
  );
  assert.equal(calls.pans.length, 0);
  assert.equal(api.focusFuelStation(station), true);
  assert.deepEqual(calls.pans, [{ lat: 40, lng: -79 }]);
  assert.deepEqual(calls.follows, [['truck', false]]);
  assert.equal(state.routeCanFit(), false);
  assert.equal(calls.fuelEditing.at(-1), station);
  await api.setRoute(plan(), { progressMiles: 10 }, false);
  await api.setNextLoadsVisible(false);
  assert.equal(
    calls.pans.length,
    1,
    'polling and Next Loads changes do not repan the camera',
  );
  api.clearFuelStationFocus();
  assert.equal(calls.fuelEditing.at(-1), null);
  assert.equal(state.routeCanFit(), true);
  api.focusFuelStation(station);
  await api.setRoute({ ...plan(), dispatchId: 'next' }, null, false);
  assert.equal(state.routeCanFit(), true);
  assert.equal(api.focusFuelStation(station), false);
  api.dispose();
  const before = calls.pans.length;
  assert.equal(api.focusFuelStation(station), false);
  api.clearFuelStationFocus();
  assert.equal(calls.pans.length, before);
});

function focusViewport(fixture, width, height, editorBounds) {
  const { element, viewport, state } = fixture;
  let frameId = 0;
  const frames = new Map();
  viewport.requestAnimationFrame = callback => {
    frames.set(++frameId, callback);
    return frameId;
  };
  viewport.cancelAnimationFrame = id => frames.delete(id);
  element.getBoundingClientRect = () => ({
    left: 80,
    top: 100,
    right: 80 + width,
    bottom: 100 + height,
    width,
    height,
  });
  element.parentElement.querySelector = selector =>
    selector === '.fuel-plan-editor'
      ? { getBoundingClientRect: () => editorBounds() }
      : null;
  state.map.getProjection = () => ({
    fromLatLngToPoint: position => ({ x: position.lng, y: position.lat }),
    fromPointToLatLng: point => ({ lng: point.x, lat: point.y }),
  });
  return {
    frames,
    flush() {
      for (const [id, callback] of [...frames]) {
        frames.delete(id);
        callback();
      }
    },
  };
}

test('Show route fits retained geometry and ends Follow without republishing', async t => {
  const { api, calls } = await fixture(t);
  await api.setRoute(plan(), { progressMiles: 125 }, false);
  api.showRoute('other');
  assert.equal(calls.fits.length, 0);
  api.showRoute('truck');
  assert.equal(calls.fits.length, 1);
  assert.deepEqual(calls.follows, [['truck', false]]);
  assert.equal(calls.plans.length, 1);
  assert.deepEqual(calls.progress, [{ progressMiles: 125 }]);
  api.setInspectionSuspended(true);
  api.showRoute('truck');
  assert.equal(calls.fits.length, 1);
  api.setInspectionSuspended(false);
  const completed = plan();
  completed.tracking = { allStopsPassed: true };
  await api.setRoute(completed, {}, false);
  api.showRoute('truck');
  assert.equal(calls.fits.length, 1);
  api.clearSelection();
  api.showRoute('truck');
  assert.equal(calls.fits.length, 1);
  api.dispose();
  api.showRoute('truck');
  assert.equal(calls.fits.length, 1);
});

test('fuel Cancel fits the retained route after layout and cancels station pan', async t => {
  const testState = await fixture(t);
  const { api, calls, element } = testState;
  const frame = focusViewport(testState, 1000, 500, () => ({}));
  const identity = { truckId: 'truck', dispatchId: 'current' };
  await api.setRoute(plan(), { progressMiles: 125 }, false);
  api.focusFuelStation({ ...identity, stationId: 'first' });
  api.setFuelEditorTruck(null);
  api.clearFuelStationFocus(true, identity);
  assert.equal(calls.fits.length, 0);
  element.parentElement.querySelector = selector =>
    selector === '.fleet-map-info-reserved'
      ? {
          getBoundingClientRect: () => ({
            left: 80,
            top: 100,
            right: 1080,
            bottom: 250,
            width: 1000,
            height: 150,
          }),
        }
      : null;
  frame.flush();
  assert.equal(calls.fits.length, 1);
  assert.equal(calls.fits[0].top, 205);
  assert.equal(calls.pans.length, 0);
  assert.equal(calls.plans.length, 1);
  assert.deepEqual(calls.progress, [{ progressMiles: 125 }]);
  api.clearFuelStationFocus();
  frame.flush();
  assert.equal(calls.fits.length, 1, 'clearing selection is not Cancel');
});

for (const change of [
  'truck',
  'dispatch',
  'completed',
  'deselect',
  'reopen',
  'dispose',
]) {
  test(`queued fuel return cannot override ${change}`, async t => {
    const testState = await fixture(t);
    const { api, calls } = testState;
    const frame = focusViewport(testState, 1000, 500, () => ({}));
    const identity = { truckId: 'truck', dispatchId: 'current' };
    await api.setRoute(plan(), null, false);
    api.clearFuelStationFocus(true, identity);
    if (change === 'truck')
      await api.setRoute({ ...plan(), truckId: 'other' }, null, false);
    if (change === 'dispatch')
      await api.setRoute({ ...plan(), dispatchId: 'other' }, null, false);
    if (change === 'completed')
      await api.setRoute(
        {
          ...plan(),
          tracking: { allStopsPassed: true },
        },
        null,
        false,
      );
    if (change === 'deselect') api.clearSelection();
    if (change === 'reopen') api.setFuelEditorTruck('truck');
    if (change === 'dispose') api.dispose();
    frame.flush();
    assert.deepEqual(calls.fits, []);
  });
}

test('fuel focus waits for editor layout and centers inside the free desktop map region without zooming', async t => {
  const fixtureState = await fixture(t);
  const { api, calls, state } = fixtureState;
  let editor = { left: 80, top: 100, right: 1080, bottom: 600 };
  const frame = focusViewport(fixtureState, 1000, 500, () => editor);
  await api.setRoute(plan(), null, false);
  api.focusFuelStation({
    truckId: 'truck',
    dispatchId: 'current',
    stationId: 'first',
  });
  assert.equal(
    calls.pans.length,
    0,
    'measurement waits until Blazor has committed the expanded editor',
  );
  editor = { left: 380, top: 190, right: 1068, bottom: 588 };
  frame.flush();
  assert.deepEqual(calls.pans, [{ lng: -79 + 350 / 32, lat: 40 }]);
  assert.equal(state.map.getZoom(), 5);
});

test('mobile fuel focus uses the area above the measured sheet and bounds partial overlap', async t => {
  const fixtureState = await fixture(t);
  const { api, calls } = fixtureState;
  const frame = focusViewport(fixtureState, 390, 650, () => ({
    left: 70,
    top: 388,
    right: 480,
    bottom: 800,
  }));
  await api.setRoute(plan(), null, false);
  api.focusFuelStation({
    truckId: 'truck',
    dispatchId: 'current',
    stationId: 'first',
  });
  frame.flush();
  assert.deepEqual(calls.pans, [{ lng: -79, lat: 40 + 181 / 32 }]);
});

test('fuel focus measures the entire opaque card rather than only its scrollable station controls', async t => {
  const fixtureState = await fixture(t);
  const { api, calls, element } = fixtureState;
  const frame = focusViewport(fixtureState, 800, 400, () => ({
    left: 480,
    top: 120,
    right: 880,
    bottom: 480,
  }));
  element.parentElement.querySelector = selector =>
    selector === '.fuel-plan-editor'
      ? {
          getBoundingClientRect: () => ({
            left: 480,
            top: 120,
            right: 880,
            bottom: 480,
          }),
          querySelector() {
            throw new Error(
              'The full card, including timeline and footer, obscures the map.',
            );
          },
        }
      : null;
  await api.setRoute(plan(), null, false);
  api.focusFuelStation({
    truckId: 'truck',
    dispatchId: 'current',
    stationId: 'first',
  });
  frame.flush();
  assert.deepEqual(calls.pans, [{ lng: -79 + 200 / 32, lat: 40 }]);
});

test('closing a workspace restores its connected opener after layout without stealing sidebar focus', async t => {
  const fixtureState = await fixture(t);
  const { api, element } = fixtureState;
  const { flush } = focusViewport(fixtureState, 800, 400, () => null);
  const document = element.ownerDocument;
  const focused = [];
  const opener = {
    isConnected: true,
    closest: () => null,
    focus: options => focused.push(options),
  };
  const closing = {
    closest: selector => (selector === '.fuel-plan-editor' ? {} : null),
  };
  document.body = {};
  document.querySelector = () => null;
  document.activeElement = opener;
  api.closeStationPopup(true);
  document.activeElement = closing;
  api.clearFuelStationFocus(true);
  assert.deepEqual(focused, []);
  document.activeElement = document.body;
  flush();
  assert.deepEqual(focused, [{ preventScroll: true }]);

  document.activeElement = opener;
  api.closeStationPopup(true);
  document.activeElement = closing;
  api.clearFuelStationFocus(true);
  document.activeElement = { closest: () => null };
  flush();
  assert.equal(
    focused.length,
    1,
    'a sidebar focus move wins over a pending close restoration',
  );
});

test('workspace focus falls back to the still-mounted map if the original popup opener is removed', async t => {
  const fixtureState = await fixture(t);
  const { api, element } = fixtureState;
  const { flush } = focusViewport(fixtureState, 800, 400, () => null);
  const document = element.ownerDocument;
  let mapFocus = 0;
  element.focus = () => mapFocus++;
  document.body = {};
  document.querySelector = () => null;
  document.activeElement = { isConnected: false };
  api.closeStationPopup(true);
  document.activeElement = { closest: () => ({}) };
  api.clearFuelStationFocus(true);
  document.activeElement = document.body;
  flush();
  assert.equal(mapFocus, 1);
});

test('queued focus pans cannot survive selection changes, editor closure, route changes or disposal', async t => {
  const fixtureState = await fixture(t);
  const { api, calls } = fixtureState;
  const { frames, flush } = focusViewport(fixtureState, 1000, 500, () => ({
    left: 380,
    top: 190,
    right: 1068,
    bottom: 588,
  }));
  const station = {
    truckId: 'truck',
    dispatchId: 'current',
    stationId: 'first',
  };
  await api.setRoute(plan(), null, false);
  api.focusFuelStation(station);
  const obsolete = [...frames.values()][0];
  api.focusFuelStation({ ...station, stationId: 'second' });
  obsolete();
  assert.equal(calls.pans.length, 0);
  assert.equal(frames.size, 1);
  flush();
  assert.equal(calls.pans.length, 1);
  for (const clear of [
    () => api.clearFuelStationFocus(),
    () => api.clearSelection(),
    () => api.setRoute({ ...plan(), dispatchId: 'next' }, null, false),
    () => api.dispose(),
  ]) {
    await api.setRoute(plan(), null, false);
    api.focusFuelStation(station);
    const queued = [...frames.values()][0];
    await clear();
    queued();
    flush();
    assert.equal(calls.pans.length, 1);
    assert.equal(frames.size, 0);
  }
});

test('fuel focus safely uses the original position when no visible map area or projection is available', async t => {
  const fixtureState = await fixture(t);
  const { api, calls, state } = fixtureState;
  let editor = { left: 80, top: 100, right: 1080, bottom: 600 };
  const { flush } = focusViewport(fixtureState, 1000, 500, () => editor);
  await api.setRoute(plan(), null, false);
  const station = {
    truckId: 'truck',
    dispatchId: 'current',
    stationId: 'first',
  };
  api.focusFuelStation(station);
  flush();
  editor = { left: 1100, top: 100, right: 1500, bottom: 600 };
  api.focusFuelStation(station);
  flush();
  state.map.getProjection = () => null;
  api.focusFuelStation(station);
  flush();
  assert.deepEqual(
    calls.pans,
    Array.from({ length: 3 }, () => ({ lat: 40, lng: -79 })),
  );
});

test('superseded and disposed interop updates cannot acknowledge or advance a newer plan', async t => {
  const { api, calls, state } = await fixture(t);
  const first = api.setRoute(plan('first'), null, false);
  const second = api.setRoute(plan('second'), null, false);
  assert.equal(await first, false);
  assert.equal(await second, true);
  assert.deepEqual(
    calls.plans.map(p => p.id),
    ['second'],
  );
  let release;
  state.stations.setRecommended = () =>
    new Promise(resolve => {
      release = resolve;
    });
  const pending = api.setRoute(
    {
      ...plan('pending'),
      fuelPlan: { stops: [{ stationId: 'fuel', buyGallons: 10 }] },
    },
    { progressMiles: 20 },
    false,
  );
  await Promise.resolve();
  api.dispose();
  release();
  assert.equal(await pending, false);
  const count = calls.plans.length;
  assert.equal(await api.setRoute(plan('late'), null, false), false);
  assert.equal(calls.plans.length, count);
});
