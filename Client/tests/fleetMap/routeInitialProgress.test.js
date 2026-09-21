import test from 'node:test';
import assert from 'node:assert/strict';
import { createRouteLayer } from '../../Scripts/fleetMap/routes/routeLayer.ts';

const point = longitude => ({ latitude: 40, longitude });
const plan = () => ({
  id: 'route',
  version: 1,
  truckId: 'truck',
  dispatchId: 'dispatch',
  fromCurrentPosition: true,
  tracking: { nextStopId: 'delivery' },
  stops: [{ id: 'delivery', job: 'Delivery', point: point(-78) }],
  route: {
    legs: [{ miles: 200, points: [point(-80), point(-79), point(-78)] }],
  },
});

function fixture(t) {
  const previousGoogle = globalThis.google;
  const lines = [],
    writes = [],
    fits = [],
    notifications = [];
  const listeners = new Map(),
    removedListeners = [];
  let zoom = 5,
    following = false;
  globalThis.google = {
    maps: {
      LatLng: class {
        constructor(value) {
          Object.assign(this, value);
        }
      },
      LatLngBounds: class {
        points = [];
        extend(value) {
          this.points.push({ ...value });
        }
      },
    },
  };
  class Line {
    constructor() {
      this.path = [];
      lines.push(this);
    }
    setOptions() {}
    setPath(path) {
      this.path = path;
      writes.push(path.map(value => ({ ...value })));
    }
    getPath() {
      return {
        setAt: (index, value) => {
          this.path[index] = value;
          writes.push(this.path.map(p => ({ ...p })));
        },
        removeAt: index => {
          this.path.splice(index, 1);
          writes.push(this.path.map(p => ({ ...p })));
        },
      };
    }
    setMap() {}
  }
  class Marker {
    constructor(options) {
      Object.assign(this, options);
    }
    setNumber() {}
    setJob() {}
  }
  const layer = createRouteLayer(
    {
      getZoom: () => zoom,
      fitBounds(bounds) {
        fits.push(bounds.points);
      },
      addListener(name, callback) {
        listeners.set(name, callback);
        return {
          remove() {
            listeners.delete(name);
            removedListeners.push(name);
          },
        };
      },
    },
    (...values) => notifications.push(values),
    () => {},
    Line,
    Marker,
    () => ({ hide() {}, show() {}, dispose() {} }),
    () => !following,
  );
  t.after(() => {
    layer.dispose();
    globalThis.google = previousGoogle;
  });
  return {
    layer,
    lines,
    writes,
    fits,
    notifications,
    listeners,
    removedListeners,
    zoom(value) {
      zoom = value;
      listeners.get('idle')?.();
    },
    drag() {
      listeners.get('dragstart')?.();
    },
    following(value) {
      following = value;
    },
  };
}

test('cold saved preview draws and fits without progress, keeps traveled geometry when authoritative progress arrives', t => {
  const f = fixture(t),
    saved = plan();
  f.layer.setPlan(saved, true);
  f.layer.setProgress(null);
  f.zoom(14);
  assert.equal(f.lines[0].path[0].lng, -80);
  assert.equal(f.lines[2].path.at(-1).lng, -78);
  assert.equal(f.fits.length, 1);
  assert.ok(f.fits[0].some(p => p.lng === -80));
  assert.deepEqual(f.notifications, []);

  f.layer.setPlan(structuredClone(saved), false);
  f.layer.setProgress({ progressMiles: 150 });
  assert.equal(f.lines[1].path[0].lng, -80);
  assert.ok(
    [f.lines[0], f.lines[2]]
      .flatMap(line => line.path)
      .every(p => p.lng >= -78.5),
  );
  assert.equal(f.fits.length, 1);
  assert.equal(f.lines[0].path[0].lng, -78.5);
  assert.deepEqual(f.notifications, [['truck', 150, 50]]);
  f.layer.setProgress({ progressMiles: 160 });
  f.zoom(5);
  assert.equal(
    f.fits.length,
    1,
    'progress and zoom updates do not repeat the deferred fit',
  );
});

test('atomic plan and progress update handles both cold and same-identity metadata payloads', t => {
  const f = fixture(t),
    saved = plan();
  f.layer.setPlan(saved, true, { progressMiles: 120 });
  assert.equal(f.lines[0].path[0].lng, -78.8);
  assert.equal(f.fits.length, 1);
  f.layer.setPlan(structuredClone(saved), false, { progressMiles: 130 });
  assert.equal(f.lines[0].path[0].lng, -78.7);
  assert.equal(f.lines[1].path[0].lng, -80);
  assert.ok(f.fits[0].some(p => p.lng === -80));
  assert.equal(f.fits.length, 1);
});

test('progressless warm polling and detail changes retain only the previously trimmed road', t => {
  const f = fixture(t),
    saved = plan();
  f.layer.setPlan(saved, false);
  f.layer.setProgress({ progressMiles: 150 });
  const paths = f.lines.map(line => line.path),
    notifications = f.notifications.length;
  f.layer.setPlan(structuredClone(saved), false);
  f.layer.setProgress(null);
  f.lines.forEach((line, i) => assert.equal(line.path, paths[i]));
  f.zoom(14);
  assert.equal(f.lines[1].path[0].lng, -80);
  assert.ok(
    [f.lines[0], f.lines[2]]
      .flatMap(line => line.path)
      .every(p => p.lng >= -78.5),
  );
  assert.equal(
    f.notifications.length,
    notifications,
    'zoom cannot report retained progress as a fresh measurement',
  );
  assert.equal(f.fits.length, 0);
});

for (const change of ['truck', 'dispatch', 'geometry']) {
  test(`a ${change} change cannot inherit the earlier route progress or deferred fit`, t => {
    const f = fixture(t),
      old = plan(),
      replacement = plan();
    f.layer.setPlan(old, false);
    f.layer.setProgress({ progressMiles: 150 });
    f.layer.setPlan({ ...old, version: 2 }, true);
    if (change === 'truck') replacement.truckId = 'another-truck';
    if (change === 'dispatch') replacement.dispatchId = 'another-dispatch';
    if (change === 'geometry') replacement.version = 3;
    f.layer.setPlan(replacement, false);
    f.layer.setProgress(null);
    f.zoom(14);
    assert.equal(f.lines[0].path[0].lng, -80);
    assert.equal(f.lines[2].path.at(-1).lng, -78);
    assert.equal(f.fits.length, 1);
    f.layer.setProgress({ progressMiles: 20 });
    assert.equal(f.lines[0].path[0].lng, -79.8);
    assert.equal(
      f.fits.length,
      1,
      'a replacement without an explicit fit cannot move the camera',
    );
  });
}

test('following blocks preview fitting and disposal prevents further camera updates', t => {
  const f = fixture(t);
  f.following(true);
  f.layer.setPlan(plan(), true);
  f.layer.setProgress(null);
  f.following(true);
  f.layer.setProgress({ progressMiles: 100 });
  f.following(false);
  f.layer.setProgress({ progressMiles: 110 });
  assert.equal(f.fits.length, 0);
  f.layer.setPlan({ ...plan(), version: 2 }, true);
  assert.equal(f.fits.length, 1);
  f.layer.dispose();
  f.layer.setProgress({ progressMiles: 120 });
  assert.equal(f.fits.length, 1);
});

test('manual dragging after preview is not overridden by arriving progress', t => {
  const f = fixture(t),
    saved = plan();
  f.layer.setPlan(saved, true, null);
  f.drag();
  f.layer.setPlan(structuredClone(saved), false, { progressMiles: 150 });
  f.zoom(14);
  f.layer.setProgress({ progressMiles: 160 });
  assert.equal(
    f.fits.length,
    1,
    'late progress cannot override the manually positioned camera',
  );
  assert.equal(f.lines[1].path[0].lng, -80);
  assert.ok(
    [f.lines[0], f.lines[2]]
      .flatMap(line => line.path)
      .every(p => p.lng >= -78.4),
  );
  assert.deepEqual(f.notifications, [['truck', 150, 50]]);

  f.layer.setPlan(structuredClone(saved), true, { progressMiles: 160 });
  assert.equal(f.fits.length, 2, 'a later explicit fit request still works');
});

for (const progress of [
  null,
  undefined,
  { progressMiles: null },
  { progressMiles: NaN },
  { locationStale: true },
]) {
  test(`unknown progress ${JSON.stringify(progress)} shows geometry without reporting or snapping a truck position`, t => {
    const f = fixture(t);
    f.layer.setPlan(plan(), false, progress);
    f.layer.setRenderedPosition(
      'truck',
      { latitude: 40, longitude: -79 },
      true,
    );
    assert.equal(f.lines[0].path[0].lng, -80);
    assert.equal(f.lines[2].path.at(-1).lng, -78);
    assert.deepEqual(f.notifications, []);
    assert.equal(f.fits.length, 0);
    f.layer.setPlan(null, false);
    assert.ok(f.lines.every(line => line.path.length === 0));
  });
}

test('disposal removes the drag and detail listeners exactly once', t => {
  const f = fixture(t);
  assert.deepEqual([...f.listeners.keys()].sort(), ['dragstart', 'idle']);
  f.layer.dispose();
  f.layer.dispose();
  assert.equal(f.listeners.size, 0);
  assert.deepEqual(f.removedListeners.sort(), ['dragstart', 'idle']);
});

test('reroute retains the reference prefix and fits the whole route', t => {
  const f = fixture(t),
    saved = plan();
  saved.referenceRoute = {
    legs: [{ miles: 300, points: [point(-81), point(-80), point(-78)] }],
  };
  f.layer.setPlan(saved, true, { progressMiles: 100 });
  assert.equal(f.lines[1].path[0].lng, -81);
  assert.equal(f.lines[0].path[0].lng, -79);
  assert.ok(f.fits[0].some(p => p.lng === -81));
  f.layer.setPlan(null, false);
  assert.ok(f.lines.every(line => line.path.length === 0));
});

test('large progress jumps replace the remaining road once and keep history', t => {
  const f = fixture(t),
    saved = plan();
  saved.route.legs[0].points = Array.from({ length: 20001 }, (_, index) =>
    point(-80 + index / 10000),
  );
  f.zoom(14);
  f.layer.setPlan(saved, false, { progressMiles: 0 });
  const writes = f.writes.length;
  const history = f.lines[1].path;
  f.layer.setProgress({ progressMiles: 150 });
  assert.ok(
    f.writes.length - writes <= 3,
    'crossing thousands of vertices must not copy the tail per vertex',
  );
  assert.ok(Math.abs(f.lines[0].path[0].lng + 78.5) < 1e-9);
  assert.equal(f.lines[2].path.at(-1).lng, -78);
  assert.equal(f.lines[1].path, history);
  f.drag();
  f.layer.setProgress({ progressMiles: 175 });
  assert.equal(f.fits.length, 0);
});

test('server-defined empty approach stays separate from loaded road', t => {
  const f = fixture(t),
    saved = plan();
  saved.segments = [{ cargoState: 'Empty' }, { cargoState: 'Loaded' }];
  saved.stops = [
    { id: 'pickup', job: 'Pick Up', stateAfter: 'Loaded', point: point(-79) },
    { id: 'delivery', job: 'Delivery', point: point(-78) },
  ];
  saved.route.legs = [
    { miles: 100, points: [point(-80), point(-79)] },
    { miles: 100, points: [point(-79), point(-78)] },
  ];
  f.layer.setPlan(saved, false, { progressMiles: 50 });
  assert.equal(f.lines[3].path[0].lng, -79.5);
  assert.equal(f.lines[3].path.at(-1).lng, -79);
  f.layer.setProgress({ progressMiles: 150 });
  assert.deepEqual(f.lines[3].path, []);
  assert.equal(f.lines[4].path[0].lng, -80);
  assert.equal(f.lines[2].path.at(-1).lng, -78);
});

test('off-route reroute keeps pickup geometry without a straight connector', t => {
  const f = fixture(t);
  const saved = plan();
  saved.referenceRoute = {
    legs: [{ miles: 300, points: [point(-83), point(-82), point(-81)] }],
  };
  f.layer.setPlan(saved, true, { progressMiles: 50 });
  assert.equal(f.lines[3].path[0].lng, -83);
  assert.equal(f.lines[3].path.at(-1).lng, -81);
  assert.equal(f.lines[1].path[0].lng, -80);
  assert.ok(f.fits.at(-1).some(p => p.lng === -83));
  f.layer.dispose();
});
