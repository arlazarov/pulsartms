import test from 'node:test';
import assert from 'node:assert/strict';
import {
  createLabelFonts,
  sceneMetrics,
} from '../../Scripts/fleetMap/rendering/sceneMetrics.ts';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.js';

test('label atlases match physical font sizes without enlarging CSS text', () => {
  for (const density of [1, 1.25, 2, 3]) {
    const fonts = createLabelFonts(density);
    for (const [role, size] of [
      ['label', 16],
      ['stopLabel', 14],
      ['truck', 13],
      ['stop', 15],
      ['fuelVisit', 12],
    ]) {
      assert.deepEqual(fonts[role], {
        sdf: false,
        fontSize: Math.round(size * density),
      });
    }
  }
  for (const density of [0, -1, NaN, Infinity]) {
    assert.deepEqual(createLabelFonts(density), createLabelFonts(1));
  }
  assert.equal(sceneMetrics.truckLabelSize, 13);
  assert.deepEqual(sceneMetrics.truckLabelPadding, [9, 4]);
});

test('density changes refresh text, preserve route data and release the resize listener', t => {
  let frame, overlay;
  const raf = globalThis.requestAnimationFrame,
    cancel = globalThis.cancelAnimationFrame;
  globalThis.requestAnimationFrame = callback => {
    frame = callback;
    return 1;
  };
  globalThis.cancelAnimationFrame = () => {
    frame = null;
  };
  t.after(() => {
    globalThis.requestAnimationFrame = raf;
    globalThis.cancelAnimationFrame = cancel;
  });
  const listeners = new Map();
  const viewport = {
    devicePixelRatio: 1,
    addEventListener: (name, callback) => listeners.set(name, callback),
    removeEventListener: (name, callback) => {
      assert.equal(listeners.get(name), callback);
      listeners.delete(name);
    },
  };
  class Overlay {
    constructor(props) {
      this.props = props;
      overlay = this;
    }
    setProps(props) {
      Object.assign(this.props, props);
    }
    setMap() {}
    finalize() {}
  }
  class Layer {
    constructor(props) {
      this.props = props;
    }
  }
  const host = { dataset: {}, ownerDocument: { defaultView: viewport } };
  const map = { getDiv: () => host, addListener: () => ({ remove() {} }) };
  const scene = createScene(map, {
    GoogleMapsOverlay: Overlay,
    TextLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    ScatterplotLayer: Layer,
  });
  t.after(() => scene.dispose());
  const flush = () => {
    const callback = frame;
    frame = null;
    callback?.();
  };
  const get = id => overlay.props.layers.find(layer => layer.props.id === id);
  const route = new scene.Polyline({ map, strokeWeight: 4 });
  route.setPath([
    { lng: -80, lat: 35 },
    { lng: -79, lat: 36 },
  ]);
  const stop = new scene.StopMarker({
    position: { lng: -79, lat: 36 },
    number: '1',
  });
  stop.setDistance('Company\n72 mi · 116 km');
  flush();
  const originalRoute = get(route.id),
    originalText = get('route-stop-distances');
  listeners.get('resize')();
  assert.equal(frame, null, 'ordinary resize never invalidates label atlases');
  viewport.devicePixelRatio = 2;
  listeners.get('resize')();
  flush();
  const retinaText = get('route-stop-distances');
  assert.notEqual(retinaText, originalText);
  assert.equal(retinaText.props.fontSettings.fontSize, 28);
  assert.equal(retinaText.props.getSize, originalText.props.getSize);
  assert.equal(get('route-stop-1-numbers').props.fontSettings.fontSize, 30);
  assert.equal(
    get(route.id),
    originalRoute,
    'density does not rebuild route geometry',
  );
  stop.setDistance('Company\n71 mi · 114 km');
  flush();
  assert.equal(
    get('route-stop-distances').props.fontSettings,
    retinaText.props.fontSettings,
  );
  viewport.devicePixelRatio = 1;
  listeners.get('resize')();
  flush();
  assert.equal(get('route-stop-distances').props.fontSettings.fontSize, 14);
  scene.dispose();
  assert.equal(listeners.size, 0);
  assert.equal(frame, null);
});

test('stop cards read shared theme tokens, align content left inside a centered card and never measure on truck motion', t => {
  let frame,
    overlay,
    observer,
    dark = false,
    measurements = 0;
  const themeColors = {
    light: {
      text: [23, 36, 56],
      pickup: [128, 96, 50],
      delivery: [32, 122, 99],
      eta: [35, 65, 176],
      success: [20, 125, 59],
      danger: [185, 28, 28],
      muted: [71, 85, 105],
    },
    dark: {
      text: [238, 242, 247],
      pickup: [216, 177, 125],
      delivery: [125, 211, 192],
      eta: [165, 180, 252],
      success: [167, 243, 208],
      danger: [253, 164, 175],
      muted: [203, 213, 225],
    },
  };
  const raf = globalThis.requestAnimationFrame,
    cancel = globalThis.cancelAnimationFrame;
  globalThis.requestAnimationFrame = callback => {
    frame = callback;
    return 1;
  };
  globalThis.cancelAnimationFrame = () => {
    frame = null;
  };
  t.after(() => {
    globalThis.requestAnimationFrame = raf;
    globalThis.cancelAnimationFrame = cancel;
  });
  const viewport = {
    devicePixelRatio: 2,
    addEventListener() {},
    removeEventListener() {},
    getComputedStyle: probe => ({
      color: `rgb(${themeColors[dark ? 'dark' : 'light'][probe.className.split('--')[1] ?? 'text'].join(', ')})`,
      backgroundColor: dark ? 'rgb(23, 36, 56)' : 'rgb(255, 255, 255)',
      borderTopColor: 'rgb(226, 232, 240)',
      fontSize: '14px',
      fontFamily: 'Arial, sans-serif',
      paddingLeft: '12px',
      paddingTop: '8px',
      borderTopLeftRadius: '8px',
    }),
    MutationObserver: class {
      constructor(callback) {
        this.callback = callback;
        observer = this;
      }
      observe() {}
      disconnect() {
        this.disconnected = true;
      }
    },
  };
  const document = {
    defaultView: viewport,
    documentElement: {},
    body: {},
    createElement(tag) {
      return tag === 'canvas'
        ? {
            getContext: () => ({
              measureText(text) {
                measurements++;
                return { width: text.length * 7 };
              },
            }),
          }
        : { remove() {} };
    },
  };
  class Overlay {
    constructor(props) {
      this.props = props;
      overlay = this;
    }
    setProps(props) {
      this.props = props;
    }
    setMap() {}
    finalize() {}
  }
  class Layer {
    constructor(props) {
      this.props = props;
    }
  }
  const host = { dataset: {}, ownerDocument: document, append() {} };
  const map = { getDiv: () => host, addListener: () => ({ remove() {} }) };
  const scene = createScene(map, {
    GoogleMapsOverlay: Overlay,
    TextLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    ScatterplotLayer: Layer,
  });
  t.after(() => scene.dispose());
  const flush = () => {
    const callback = frame;
    frame = null;
    callback?.();
  };
  const get = id => overlay.props.layers.find(layer => layer.props.id === id);
  const stop = new scene.StopMarker({
    position: { lng: -79, lat: 40 },
    number: '1',
    job: 'Pickup',
  });
  stop.setDistance(
    'Pickup\nCompany\nETA Sep 9, 09:00 AM local\nEmpty 10 mi\nTotal 110 mi',
    ['heading', 'text', 'success', 'muted', 'text'],
  );
  flush();
  const original = get('route-stop-distances');
  assert.equal(original.props.getSize, 14);
  assert.equal(original.props.fontSettings.fontSize, 28);
  assert.equal(original.props.fontWeight, 400);
  assert.equal(original.props.getTextAnchor, 'start');
  assert.deepEqual(original.props.getColor, [23, 36, 56]);
  assert.deepEqual(original.props.backgroundPadding, [12, 8]);
  assert.equal(original.props.backgroundBorderRadius, 8);
  assert.deepEqual(original.props.getPixelOffset(original.props.data[0]), [
    (-'ETA Sep 9, 09:00 AM local'.length * 7) / 2,
    -34,
  ]);
  assert.equal(original.props._subLayerProps.characters.visible, false);
  assert.deepEqual(original.props.getBorderColor(original.props.data[0]), [
    ...themeColors.light.pickup,
    110,
  ]);
  const originalContent = get('route-stop-distances-content');
  assert.deepEqual(
    originalContent.props.data.map(row => originalContent.props.getColor(row)),
    ['pickup', 'text', 'success', 'muted', 'text'].map(
      role => themeColors.light[role],
    ),
  );
  assert.deepEqual(
    originalContent.props.data.map(row => row.text),
    original.props.data[0].text.split('\n'),
  );
  const measured = measurements;
  const truck = scene.createTruckMarker(map, () => {});
  truck.update({ unitNumber: '100', engineState: 'On' });
  truck.render({ longitude: -79, latitude: 40, heading: 0 });
  flush();
  assert.equal(measurements, measured);
  assert.equal(get('route-stop-distances'), original);
  assert.equal(get('route-stop-distances-content'), originalContent);
  dark = true;
  observer.callback();
  flush();
  assert.deepEqual(get('route-stop-distances').props.getColor, [238, 242, 247]);
  assert.deepEqual(
    get('route-stop-distances').props.getBackgroundColor,
    [23, 36, 56, 255],
  );
  const darkContent = get('route-stop-distances-content');
  assert.deepEqual(
    darkContent.props.data.map(row => darkContent.props.getColor(row)),
    ['pickup', 'text', 'success', 'muted', 'text'].map(
      role => themeColors.dark[role],
    ),
  );
  scene.dispose();
  assert.equal(observer.disconnected, true);
});
