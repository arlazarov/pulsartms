import { popupFixture } from './popupFixture.js';
import test from 'node:test';
import assert from 'node:assert/strict';
import { createStationLayer } from '../../Scripts/fleetMap/stations/stationLayer.js';

test('recommendations keep rings on the scene and distances only in the selected popup', async () => {
  const points = new Map();
  const recommendations = [];
  const mapListeners = {};
  let overlay;
  let infoWindow;
  const element = () => ({
    children: [],
    listeners: {},
    style: { setProperty() {} },
    classList: {
      add() {},
      toggle() {},
      contains() {
        return false;
      },
    },
    setAttribute() {},
    append(...nodes) {
      this.children.push(...nodes);
    },
    replaceChildren(...nodes) {
      this.children = nodes;
    },
    addEventListener(name, callback) {
      this.listeners[name] = callback;
    },
    removeEventListener() {},
    remove() {},
  });
  globalThis.document = { documentElement: {}, createElement: element };
  globalThis.window = { devicePixelRatio: 2 };
  globalThis.getComputedStyle = () => ({
    getPropertyValue: () => '10, 20, 30',
  });
  globalThis.google = {
    maps: {
      RenderingType: { VECTOR: 'VECTOR', RASTER: 'RASTER' },
      WebGLOverlayView: class {
        constructor() {
          overlay = this;
          this.redraws = 0;
        }
        setMap(map) {
          this.map = map;
        }
        requestRedraw() {
          this.redraws++;
        }
      },
      InfoWindow: class {
        constructor(options) {
          Object.assign(this, options);
          infoWindow = this;
        }
        setContent(content) {
          this.content = content;
        }
        setPosition(position) {
          this.position = position;
        }
        open(options) {
          this.map = options.map;
        }
        close() {
          this.map = null;
        }
      },
      marker: {
        AdvancedMarkerElement: class {
          constructor(options) {
            Object.assign(this, options);
            this.listeners = {};
            recommendations.push(this);
          }
          addEventListener(name, fn) {
            this.listeners[name] = fn;
          }
          removeEventListener(name) {
            delete this.listeners[name];
          }
        },
      },
    },
  };
  const map = {
    cursor: 'default',
    getZoom: () => 5,
    getRenderingType: () => 'VECTOR',
    setOptions(options) {
      this.cursor = options.draggableCursor;
    },
    addListener(name, callback) {
      mapListeners[name] = callback;
      return { remove: () => delete mapListeners[name] };
    },
  };
  let opened = 0;
  const layer = createStationLayer(
    map,
    () => {
      opened++;
    },
    () => {
      overlay = { map, redraws: 0 };
      return {
        setPoint(id, position, color, recommended, selected, label) {
          points.set(id, { recommended, selected, label });
        },
        removePoint(id) {
          points.delete(id);
        },
        redraw() {
          overlay.redraws++;
        },
        setVisible() {},
        hitTest(p) {
          return p?.lat === 40
            ? p.lng === -79
              ? 'a'
              : p.lng === -78
                ? 'b'
                : null
            : null;
        },
        dispose() {
          overlay.map = null;
        },
      };
    },
    popupFixture,
  );
  const quote = {
    currency: 'USD',
    unit: 'US gal',
    product: 'Diesel',
    discountPrice: 3,
    priceAfterIfta: 2.5,
    effectiveFrom: '2026-09-05',
    effectiveTo: '2026-09-05',
  };
  const stations = ['a', 'b'].map(id => ({
    id,
    name: id,
    latitude: 40,
    longitude: id === 'a' ? -79 : -78,
    discounts: [quote],
    cashDiscount: quote,
    iftaDiscount: quote,
  }));
  layer.setRecommended(['a']);
  await layer.setStations(stations, '2026-09-05', false);
  assert.equal(overlay.map, map);
  await layer.setVisible(true);
  assert.equal(
    recommendations.length,
    0,
    'recommendations must not create DOM markers',
  );
  assert.equal(points.get('a').recommended, true);
  assert.ok(overlay.redraws > 0);
  assert.equal(layer.handleMapClick({ latLng: { lat: 40, lng: -79 } }), true);
  assert.equal(opened, 1);
  assert.deepEqual(infoWindow.position, { lat: 40, lng: -79 });
  assert.equal(layer.handleMapClick({ latLng: { lat: 10, lng: 10 } }), false);
  await layer.setVisible(false);
  await layer.setRecommended([
    { id: 'b', gallons: 30, routeMile: 100, miles: 10 },
  ]);
  await layer.setVisible(true);
  assert.equal(recommendations.length, 0);
  assert.equal(points.get('b').label, undefined);
  assert.equal(layer.handleMapClick({ latLng: { lat: 40, lng: -78 } }), true);
  const distance = infoWindow.content.children.find(
    node => node.textContent === '10 mi · 16 km away',
  );
  assert.equal(distance.textContent, '10 mi · 16 km away');
  assert.equal(distance.hidden, false);
  const redrawsBeforeProgress = overlay.redraws;
  layer.setProgress(95);
  assert.equal(points.get('b').label, undefined);
  assert.equal(distance.textContent, '5 mi · 8 km away');
  assert.equal(
    overlay.redraws,
    redrawsBeforeProgress,
    'distance-only changes do not redraw station markers',
  );
  layer.setProgress(100.6);
  assert.equal(points.get('b').recommended, false);
  assert.equal(points.get('b').label, undefined);
  assert.equal(distance.hidden, true);
  const pendingRender = layer.setStations(
    Array.from({ length: 64 }, (_, i) => ({
      ...stations[0],
      id: `pending-${i}`,
    })),
    '2026-09-05',
    false,
  );
  layer.dispose();
  assert.equal(overlay.map, null);
  const redrawsAfterDispose = overlay.redraws;
  await pendingRender;
  layer.dispose();
  layer.closePopup();
  layer.setProgress(105);
  await layer.setVisible(true);
  await layer.setStations(stations, '2026-09-05', false);
  await layer.setRecommended(['a']);
  await layer.setIfta(true);
  assert.equal(layer.handleMapClick({ latLng: { lat: 40, lng: -79 } }), false);
  assert.equal(points.size, 0);
  assert.equal(
    overlay.redraws,
    redrawsAfterDispose,
    'late calls must not touch a disposed renderer',
  );
});
