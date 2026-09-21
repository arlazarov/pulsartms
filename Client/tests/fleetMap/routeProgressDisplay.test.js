import test from 'node:test';
import assert from 'node:assert/strict';
import { createRouteLayer } from '../../Scripts/fleetMap/routes/routeLayer.ts';

const point = longitude => ({ latitude: 40, longitude });
const routePlan = () => ({
  id: 'plan',
  version: 1,
  truckId: 'truck',
  dispatchId: '33333333-3333-3333-3333-333333333333',
  fromCurrentPosition: true,
  tracking: { nextStopId: 'pickup' },
  stops: [
    { id: 'pickup', job: 'Pickup', name: 'Warehouse', point: point(-79) },
    { id: 'delivery', job: 'Delivery', name: 'Customer', point: point(-78) },
  ],
  route: {
    legs: [
      { miles: 100, points: [point(-80), point(-79)] },
      { miles: 100, points: [point(-79), point(-78)] },
    ],
  },
});

function fixture(t, canFit = () => true) {
  const originalDocument = globalThis.document,
    originalGoogle = globalThis.google;
  const clock = { now: 0 };
  t.mock.method(performance, 'now', () => clock.now);
  const calls = {
    progress: [],
    fits: [],
    shows: 0,
    creates: 0,
    pathWrites: 0,
    listenerRemoves: [],
    popupDisposes: 0,
  };
  const state = { content: null };
  const lines = [],
    markers = [];
  globalThis.document = {
    createElement(tagName) {
      calls.creates++;
      return {
        tagName,
        children: [],
        append(...children) {
          this.children.push(...children);
        },
      };
    },
  };
  globalThis.google = {
    maps: {
      LatLng: class {
        constructor(value) {
          this.value = value;
        }
        lat() {
          return this.value.lat;
        }
        lng() {
          return this.value.lng;
        }
      },
      LatLngBounds: class {
        points = [];
        extend(value) {
          this.points.push(value);
        }
      },
    },
  };
  class Line {
    constructor(options) {
      Object.assign(this, options);
      this.path = [];
      lines.push(this);
    }
    setOptions(options) {
      Object.assign(this, options);
    }
    setPath(path) {
      calls.pathWrites++;
      this.path = path;
    }
    getPath() {
      return {
        removeAt: index => {
          calls.pathWrites++;
          this.path.splice(index, 1);
        },
        setAt: (index, value) => {
          calls.pathWrites++;
          assert.ok(value instanceof google.maps.LatLng);
          this.path[index] = { lat: value.lat(), lng: value.lng() };
        },
      };
    }
    setMap(map) {
      this.map = map;
    }
  }
  class Marker {
    constructor(options) {
      Object.assign(this, options);
      markers.push(this);
    }
    setNumber(number) {
      this.number = number;
    }
    setJob(job) {
      this.job = job;
    }
  }
  const map = {
    getZoom: () => 13,
    fitBounds: (bounds, padding) => calls.fits.push({ bounds, padding }),
    addListener: name => ({
      remove() {
        calls.listenerRemoves.push(name);
      },
    }),
  };
  const layer = createRouteLayer(
    map,
    (...values) => calls.progress.push(values),
    () => {},
    Line,
    Marker,
    () => ({
      show(content) {
        calls.shows++;
        state.content = content;
      },
      hide() {
        state.content = null;
      },
      dispose() {
        calls.popupDisposes++;
      },
    }),
    canFit,
  );
  t.after(() => {
    layer.dispose();
    globalThis.document = originalDocument;
    globalThis.google = originalGoogle;
  });
  const nodes = node => [node, ...(node.children ?? []).flatMap(nodes)];
  const distance = () => {
    const field = nodes(state.content).find(node =>
      node.className?.split(' ').includes('fleet-route-popup__distance'),
    );
    return field.children.find(node => node.tagName === 'dd').children[0]
      .textContent;
  };
  const openDelivery = () =>
    markers
      .findLast(marker => marker.map && marker.job === 'Delivery')
      .onSelect();
  return { layer, clock, calls, state, lines, markers, distance, openDelivery };
}

test('explicit fit shows the full path without rebuilding', t => {
  const { layer, calls, lines } = fixture(t);
  layer.setPlan(routePlan(), false, { progressMiles: 125 });
  const writes = calls.pathWrites;
  const progress = [...calls.progress];
  const paths = lines.map(line => line.path);
  layer.fitRemaining();
  assert.equal(calls.fits.length, 1);
  assert.deepEqual(calls.fits[0].bounds.points, [
    { lat: 40, lng: -80 },
    { lat: 40, lng: -79 },
    { lat: 40, lng: -78 },
  ]);
  assert.equal(calls.fits[0].padding, 55);
  assert.equal(calls.pathWrites, writes);
  assert.deepEqual(calls.progress, progress);
  lines.forEach((line, index) => assert.equal(line.path, paths[index]));
});

test('explicit fit skips missing geometry and respects Follow', t => {
  let allowed = true;
  const { layer, calls } = fixture(t, () => allowed);
  layer.fitRemaining();
  layer.setPlan(routePlan(), false, { progressMiles: 125 });
  assert.equal(calls.fits.length, 0);
  allowed = false;
  layer.fitRemaining();
  assert.equal(calls.fits.length, 0);
  allowed = true;
  layer.fitRemaining();
  assert.equal(calls.fits.length, 1);
  layer.dispose();
  layer.fitRemaining();
  assert.equal(calls.fits.length, 1);
});

test('minute-sampled popup and progress notification do not throttle the animated road', t => {
  const { layer, clock, calls, state, lines, distance, openDelivery } =
    fixture(t);
  layer.setPlan(routePlan(), false);
  layer.setProgress({ progressMiles: 0 });
  openDelivery();
  assert.deepEqual(calls.progress, [['truck', 0, 200]]);
  assert.equal(distance(), '200 mi · 322 km');
  const content = state.content,
    creates = calls.creates,
    tail = lines[2].path;

  for (const [now, longitude] of [
    [1_000, -79.875],
    [59_999, -79.75],
  ]) {
    clock.now = now;
    layer.setRenderedPosition('truck', point(longitude));
    assert.equal(
      lines[0].path[0].lng,
      longitude,
      'the animated leading edge follows each rendered position',
    );
    assert.equal(
      lines[2].path,
      tail,
      'movement within one segment retains the long road buffer',
    );
    assert.equal(
      state.content,
      content,
      'the popup retains its sampled mileage',
    );
    assert.equal(
      calls.creates,
      creates,
      'animation does not rebuild the popup',
    );
    assert.equal(calls.shows, 1);
    assert.equal(
      calls.progress.length,
      1,
      'no repeated interop notification before one minute',
    );
  }
  clock.now = 60_000;
  layer.setRenderedPosition('truck', point(-79.5));
  assert.equal(lines[0].path[0].lng, -79.5);
  assert.deepEqual(calls.progress, [
    ['truck', 0, 200],
    ['truck', 50, 150],
  ]);
  assert.equal(distance(), '150 mi · 241 km');
  assert.equal(calls.shows, 2);
});

test('same-version polling and fuel updates retain cadence and unchanged progress flushes at the boundary', t => {
  const { layer, clock, calls, state, lines, markers, distance, openDelivery } =
    fixture(t);
  const plan = routePlan();
  layer.setPlan(plan, false);
  layer.setProgress({ progressMiles: 10 });
  openDelivery();
  const content = state.content,
    retainedMarkers = [...markers],
    tail = lines[2].path;

  for (const [now, progressMiles] of [
    [1_000, 20],
    [30_000, 30],
    [59_999, 40],
  ]) {
    clock.now = now;
    const polled = structuredClone(plan);
    polled.fuelPlan = {
      calculatedAt: `${now}`,
      stops: [{ stationId: `station-${now}` }],
    };
    layer.setPlan(polled, false);
    layer.setProgress({ progressMiles });
    assert.equal(state.content, content);
    assert.equal(
      calls.progress.length,
      1,
      'polling must not restart or bypass the display interval',
    );
    assert.equal(lines[2].path, tail);
    assert.deepEqual(markers, retainedMarkers);
  }
  assert.equal(distance(), '190 mi · 306 km');
  assert.equal(lines[0].path[0].lng, -79.6);
  const pathWrites = calls.pathWrites;
  clock.now = 60_000;
  layer.setProgress({ progressMiles: 40 });
  assert.deepEqual(
    calls.progress,
    [
      ['truck', 10, 190],
      ['truck', 40, 160],
    ],
    'an unchanged progress value must still publish when the minute boundary arrives',
  );
  assert.equal(distance(), '160 mi · 257 km');
  assert.equal(
    calls.pathWrites,
    pathWrites,
    'a display flush does not rewrite unchanged geometry',
  );
});

for (const change of [
  'geometry',
  'truck',
  'dispatch',
  'next stop',
  'pending stop',
]) {
  test(`${change} changes publish the next progress immediately instead of retaining another route snapshot`, t => {
    const { layer, clock, calls, lines, markers, distance, openDelivery } =
      fixture(t);
    const plan = routePlan();
    layer.setPlan(plan, false);
    layer.setProgress({ progressMiles: 10 });
    openDelivery();
    const tail = lines[2].path;
    const changed = structuredClone(plan);
    if (change === 'geometry') {
      changed.version++;
      changed.route.legs[0].miles = 150;
    } else if (change === 'truck') changed.truckId = 'other-truck';
    else if (change === 'dispatch')
      changed.dispatchId = '44444444-4444-4444-4444-444444444444';
    else
      changed.tracking = {
        nextStopId: 'delivery',
        passedStopIds: change === 'pending stop' ? ['pickup'] : [],
      };
    clock.now = 1_000;
    layer.setPlan(changed, false);
    layer.setProgress({ progressMiles: 20 });
    openDelivery();
    const remaining = change === 'geometry' ? 230 : 180;
    assert.deepEqual(calls.progress, [
      ['truck', 10, 190],
      [changed.truckId, 20, remaining],
    ]);
    assert.equal(
      distance(),
      change === 'geometry' ? '230 mi · 370 km' : '180 mi · 290 km',
    );
    layer.setProgress({ progressMiles: 21 });
    assert.equal(
      calls.progress.length,
      2,
      'the identity change resets cadence only once',
    );
    if (change === 'pending stop') {
      assert.equal(
        lines[2].path,
        tail,
        'stop advancement does not replace unchanged geometry',
      );
      assert.ok(markers[0].map);
      assert.equal(markers[1].number, '2');
    }
  });
}

test('invalid progress clears stale displayed mileage and the next valid sample appears immediately', t => {
  const { layer, clock, calls, lines, distance, openDelivery } = fixture(t);
  layer.setPlan(routePlan(), false);
  layer.setProgress({ progressMiles: 10 });
  openDelivery();
  const retainedPaths = lines.map(line => line.path);
  for (const invalid of [
    null,
    { progressMiles: null },
    {},
    { progressMiles: NaN },
    { progressMiles: Infinity },
  ]) {
    clock.now += 1_000;
    const notifications = calls.progress.length;
    layer.setProgress(invalid);
    assert.equal(distance(), '—');
    lines.forEach((line, index) =>
      assert.equal(
        line.path,
        retainedPaths[index],
        'unknown progress retains only the already-trimmed road',
      ),
    );
    assert.equal(
      calls.progress.length,
      notifications,
      'unknown mileage is not reported as a valid zero',
    );
    const shows = calls.shows;
    layer.setProgress(invalid);
    assert.equal(
      calls.shows,
      shows,
      'repeated invalid samples do not churn the popup',
    );
    clock.now++;
    layer.setProgress({ progressMiles: 10 });
    assert.equal(distance(), '190 mi · 306 km');
    assert.equal(calls.progress.length, notifications + 1);
    assert.deepEqual(calls.progress.at(-1), ['truck', 10, 190]);
  }
});

test('pending recalculation keeps displayed mileage while animation advances, then publishes the new value immediately', t => {
  const { layer, clock, calls, lines, distance, openDelivery } = fixture(t);
  layer.setPlan(routePlan(), false);
  layer.setProgress({ progressMiles: 10 });
  openDelivery();
  layer.setEtas(
    new Map([
      [
        'delivery',
        {
          text: 'ETA Sep 10, 02:00 PM',
          statusText: 'On time',
          tone: 'success',
        },
      ],
    ]),
    true,
  );
  clock.now = 60_000;
  layer.setRenderedPosition('truck', point(-79.5));
  assert.equal(lines[0].path[0].lng, -79.5);
  assert.equal(distance(), '190 mi · 306 km');
  assert.equal(calls.progress.length, 1);
  clock.now = 120_000;
  layer.setRenderedPosition('truck', point(-79.4));
  assert.equal(distance(), '190 mi · 306 km');
  layer.setEtas(
    new Map([['delivery', { text: 'ETA Sep 10, 02:10 PM', tone: 'eta' }]]),
    false,
  );
  assert.equal(distance(), '140 mi · 225 km');
  assert.equal(calls.progress.length, 2);
  layer.setProgress({ progressMiles: 61 });
  assert.equal(
    calls.progress.length,
    2,
    'ordinary sampling resumes after one immediate recovery',
  );
});

test('disposing a sampled route releases map objects and blocks later display or animation writes', t => {
  const { layer, clock, calls, lines, markers, state, openDelivery } =
    fixture(t);
  const plan = routePlan();
  layer.setPlan(plan, false);
  layer.setProgress({ progressMiles: 10 });
  openDelivery();
  layer.dispose();
  const afterDisposal = structuredClone(calls);
  assert.equal(state.content, null);
  assert.ok(lines.every(line => line.map === null));
  assert.ok(markers.every(marker => marker.map === null));
  assert.deepEqual(calls.listenerRemoves, ['idle', 'dragstart']);
  assert.equal(calls.popupDisposes, 1);
  clock.now = 60_000;
  layer.setPlan(plan, false);
  layer.setProgress({ progressMiles: 50 });
  layer.setRenderedPosition('truck', point(-79.5), true);
  layer.setEtas(new Map());
  layer.closePopup();
  layer.dispose();
  assert.deepEqual(calls, afterDisposal);
  const position = point(-79.5);
  assert.equal(layer.getDisplayPosition('truck', position), position);
});
