import test from 'node:test';
import assert from 'node:assert/strict';
import {
  showStopMap,
  selectStopMap,
  activateStopMap,
  disposeStopMap,
} from '../../Scripts/dispatch/dispatch.ts';

test('stop map retains the basemap and camera across selection, then releases markers', async () => {
  const original = {
    window: globalThis.window,
    google: globalThis.google,
    document: globalThis.document,
    getComputedStyle: globalThis.getComputedStyle,
  };
  const calls = { maps: 0, fits: 0, cleared: 0, markers: [], lines: [] };
  const classes = () => new Set();
  const maps = {
    importLibrary: async () => {},
    Map: class {
      constructor(element, options) {
        assert.equal(options.gestureHandling, 'greedy');
        assert.equal(options.cameraControl, false);
        assert.equal(options.zoomControl, false);
        calls.maps++;
      }
      fitBounds() {
        calls.fits++;
      }
      setZoom(value) {
        calls.zoom = value;
      }
      setCenter(value) {
        calls.center = value;
      }
      setMapTypeId(value) {
        calls.type = value;
      }
      setTilt(value) {
        calls.tilt = value;
      }
    },
    LatLngBounds: class {
      extend() {}
    },
    Polyline: class {
      constructor(options) {
        Object.assign(this, options);
        calls.lines.push(this);
      }
      setMap(map) {
        this.map = map;
      }
    },
    marker: {
      AdvancedMarkerElement: class {
        constructor(options) {
          Object.assign(this, options);
          calls.markers.push(this);
        }
      },
    },
    event: {
      clearInstanceListeners() {
        calls.cleared++;
      },
    },
  };
  globalThis.google = { maps };
  globalThis.getComputedStyle = () => ({
    getPropertyValue: name =>
      name === '--dispatch-empty-road-color' ? 'orange' : 'blue',
  });
  globalThis.window = { google: globalThis.google };
  globalThis.document = {
    documentElement: { dataset: { theme: 'light' } },
    createElement() {
      const active = classes();
      return {
        dataset: {},
        children: [],
        append(...children) {
          this.children.push(...children);
        },
        querySelectorAll() {
          return this.children;
        },
        classList: {
          toggle(name, enabled) {
            enabled ? active.add(name) : active.delete(name);
          },
          contains(name) {
            return active.has(name);
          },
        },
      };
    },
  };
  try {
    const element = { isConnected: true };
    const points = [
      { id: 'first', latitude: 40, longitude: -75, number: 1, name: 'Pickup' },
      {
        id: 'second',
        latitude: 41,
        longitude: -74,
        number: 2,
        name: 'Delivery',
      },
    ];
    await showStopMap(element, 'fixture', points, 'first');
    await showStopMap(element, 'fixture', points, 'second');
    assert.equal(calls.maps, 1);
    assert.equal(calls.fits, 1);
    assert.equal(calls.markers.length, 2);
    assert.equal(
      calls.markers[1].content.classList.contains('is-selected'),
      true,
    );
    const paths = [
      [
        { latitude: 40, longitude: -75 },
        { latitude: 40.5, longitude: -74.5 },
      ],
      [
        { latitude: 40.6, longitude: -74.4 },
        { latitude: 41, longitude: -74 },
      ],
    ];
    await showStopMap(element, 'fixture', points, 'second', paths, [
      { cargoState: 'Loaded' },
      { cargoState: 'Empty' },
    ]);
    assert.equal(calls.lines.length, 2, 'transfer sections stay separate');
    assert.equal(calls.fits, 1, 'late roads must not move the camera');
    assert.equal(calls.lines[0].strokeColor, 'blue');
    assert.equal(calls.lines[1].strokeColor, 'orange');
    selectStopMap(element, 'first');
    assert.equal(
      calls.markers[0].content.classList.contains('is-selected'),
      true,
    );
    assert.equal(
      calls.markers[1].content.classList.contains('is-selected'),
      false,
    );
    assert.equal(calls.lines.length, 2, 'selection reuses road geometry');
    await showStopMap(element, 'fixture', points, 'first', []);
    assert.ok(
      calls.lines.every(line => line.map === null),
      'draft hides saved roads',
    );
    await showStopMap(element, 'fixture', points, 'first', paths);
    const previousFits = calls.fits;
    activateStopMap(element, 'first');
    assert.equal(calls.type, 'roadmap', 'first click keeps the overview');
    activateStopMap(element, 'first');
    assert.equal(calls.type, 'satellite');
    assert.equal(calls.zoom, 18);
    selectStopMap(element, 'first');
    await showStopMap(element, 'fixture', points, 'first', paths);
    assert.equal(calls.type, 'satellite', 'render does not toggle the view');
    activateStopMap(element, 'first');
    assert.equal(calls.type, 'roadmap');
    assert.equal(calls.fits, previousFits + 2);
    activateStopMap(element, 'second');
    assert.equal(calls.type, 'roadmap', 'different stop starts with overview');
    activateStopMap(element, 'second');
    assert.equal(calls.type, 'satellite');
    assert.deepEqual(calls.center, { lat: 41, lng: -74 });
    disposeStopMap(element);
    assert.ok(calls.lines.every(line => line.map === null));
    assert.ok(calls.markers.every(marker => marker.map === null));
    assert.equal(calls.cleared, 1);
    const delayed = { isConnected: true };
    const initializing = showStopMap(delayed, 'fixture', points, 'first');
    selectStopMap(delayed, 'second');
    await initializing;
    assert.equal(
      calls.markers.at(-1).content.classList.contains('is-selected'),
      true,
    );
    disposeStopMap(delayed);
    const colocated = { isConnected: true };
    const before = calls.markers.length;
    await showStopMap(
      colocated,
      'fixture',
      [points[0], { ...points[1], latitude: 40, longitude: -75 }],
      'second',
    );
    assert.equal(calls.markers.length, before + 1);
    const group = calls.markers.at(-1);
    assert.deepEqual(
      group.content.children.map(x => x.textContent),
      [1, 2],
    );
    assert.equal(
      group.content.children[0].classList.contains('is-selected'),
      false,
    );
    assert.equal(
      group.content.children[1].classList.contains('is-selected'),
      true,
    );
    selectStopMap(colocated, 'first');
    assert.equal(
      group.content.children[0].classList.contains('is-selected'),
      true,
    );
    assert.equal(
      group.content.children[1].classList.contains('is-selected'),
      false,
    );
    disposeStopMap(colocated);
    const abandoned = { isConnected: true };
    const pending = showStopMap(abandoned, 'fixture', points, 'first');
    disposeStopMap(abandoned);
    await pending;
    assert.equal(
      calls.maps,
      3,
      'late provider completion must not resurrect a disposed map',
    );
  } finally {
    Object.assign(globalThis, original);
  }
});
