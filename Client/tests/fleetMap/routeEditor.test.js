import test from 'node:test';
import assert from 'node:assert/strict';
import { createRouteEditor } from '../../Scripts/fleetMap/routes/routeEditor.ts';

test('route editing preserves map, bounds handle count and isolates stale callbacks', t => {
  const previous = globalThis.google;
  globalThis.google = {
    maps: {
      LatLngBounds: class {
        extend() {}
      },
    },
  };
  t.after(() => {
    globalThis.google = previous;
  });
  const lines = [],
    markers = [],
    stops = [],
    notifications = [];
  class Line {
    constructor(options) {
      Object.assign(this, options);
      lines.push(this);
    }
    setOptions(options) {
      Object.assign(this, options);
    }
    setPath(path) {
      this.path = path;
    }
    setMap(map) {
      this.map = map;
    }
  }
  class Marker {
    constructor(options) {
      Object.assign(this, options);
      this.listeners = {};
      markers.push(this);
    }
    addListener(name, fn) {
      this.listeners[name] = fn;
      return {
        remove: () => {
          delete this.listeners[name];
        },
      };
    }
  }
  class Stop {
    constructor(options) {
      Object.assign(this, options);
      stops.push(this);
    }
  }
  let fits = 0;
  const map = {
    getDiv: () => ({ ownerDocument: { createElement: () => ({}) } }),
    fitBounds: () => fits++,
  };
  const editor = createRouteEditor(
    map,
    Line,
    Stop,
    Marker,
    (...args) => notifications.push(args),
    () => 40,
  );
  const point = (latitude, longitude = -80) => ({ latitude, longitude });
  const leg = { points: [point(40), point(41), point(42)] };
  const preview = {
    id: 'preview',
    truckId: 'truck',
    stops: [
      { id: 'a', point: point(40) },
      { id: 'b', point: point(42) },
      { id: 'c', point: point(43) },
    ],
    viaPoints: [],
    options: [
      { number: 1, route: { legs: [leg, leg] } },
      { number: 2, route: { legs: [leg, leg] } },
    ],
  };
  const payload = { session: 'session', selected: 1, editing: true, preview };
  editor.set(payload);
  assert.equal(fits, 1);
  assert.equal(lines.length, 4);
  assert.equal(markers.length, 1);
  assert.ok(stops.every(stop => stop.routeRole === 'preview'));
  for (let i = 0; i < 100; i++) lines[i % 2].onHover({ coordinate: [-79, 41] });
  assert.equal(
    markers.length,
    1,
    'one reused road handle, not a marker per hover',
  );
  markers[0].position = { lat: 41, lng: -78 };
  markers[0].listeners.dragstart();
  markers[0].listeners.dragend();
  assert.deepEqual(notifications.at(-1), [
    'OnRouteViaChanged',
    'session',
    null,
    1,
    41,
    -78,
  ]);
  const staleDrag = markers[0].listeners.dragend;
  const paths = lines.map(line => line.path);
  editor.set({
    session: 'session',
    previewId: 'preview',
    selected: 2,
    editing: true,
  });
  assert.equal(fits, 1, 'selection does not reset the camera');
  assert.equal(lines.length, 4, 'selection reuses every line');
  assert.equal(stops.length, 3, 'selection reuses mandatory stop markers');
  assert.ok(
    lines.every((line, i) => line.path === paths[i]),
    'selection keeps path allocations',
  );
  assert.equal(lines[0].strokeWeight, 3);
  assert.equal(lines[2].strokeWeight, 5);
  const count = notifications.length;
  staleDrag();
  assert.equal(notifications.length, count);
  assert.equal(lines[0].map, map);
  assert.equal(markers[0].map, null);
  assert.equal(Object.keys(markers[0].listeners).length, 0);
  editor.set({ ...payload, addPoint: true });
  lines.at(-1).onClick();
  assert.equal(
    notifications.length,
    count,
    'add-point gesture does not also select a route',
  );
  editor.click({ latLng: { lat: () => 41, lng: () => -77 } });
  assert.deepEqual(notifications.at(-1), [
    'OnRouteViaChanged',
    'session',
    null,
    -1,
    41,
    -77,
  ]);
  editor.set({
    session: 'session',
    previewId: 'preview',
    selected: 1,
    editing: false,
    addPoint: false,
  });
  assert.equal(lines.length, 4, 'editing mode reuses geometry');
  assert.equal(stops.length, 3);
  assert.ok(
    lines.every(
      (line, i) => line.path === paths[i] && line.onHover === undefined,
    ),
  );
  const beforeStale = lines.length;
  editor.set({
    session: 'old-session',
    previewId: 'preview',
    selected: 2,
    editing: true,
  });
  editor.set({
    session: 'session',
    previewId: 'unknown-preview',
    selected: 2,
    editing: true,
  });
  assert.equal(
    lines.length,
    beforeStale,
    'unknown metadata cannot replace retained geometry',
  );
  assert.equal(editor.truckId, 'truck');
  const staleClick = lines[0].onClick;
  editor.set({ ...payload, preview: { ...preview, id: 'replacement' } });
  const beforeClick = notifications.length;
  staleClick();
  assert.equal(
    notifications.length,
    beforeClick,
    'removed geometry cannot select an option',
  );
  assert.equal(lines.length, 8, 'a new preview replaces geometry');
  editor.dispose();
  assert.ok(
    markers.every(
      marker =>
        marker.map === null && Object.keys(marker.listeners).length === 0,
    ),
  );
  assert.ok(lines.every(line => line.map === null));
  assert.ok(stops.every(stop => stop.map === null));
  assert.equal(editor.active, false);
  assert.equal(editor.click({}), false);
});
