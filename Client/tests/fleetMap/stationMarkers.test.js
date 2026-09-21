import test from 'node:test';
import assert from 'node:assert/strict';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.ts';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.js';

const station = (id, changes = {}) => ({
  id,
  position: [-80, 36],
  price: 5.613,
  color: [21, 128, 61],
  ...changes,
});

test('station layers never read prices or rebuild cached layers during camera-only changes', () => {
  let priceReads = 0,
    editingReads = 0,
    layersCreated = 0;
  class Layer {
    constructor(props) {
      this.props = props;
      layersCreated++;
    }
  }
  const build = createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  const hidden = station('hidden');
  Object.defineProperties(hidden, {
    price: {
      get() {
        priceReads++;
        return 5.613;
      },
    },
    editing: {
      get() {
        editingReads++;
        return false;
      },
    },
  });
  const input = {
    lines: [],
    stationData: [hidden],
    stationsVisible: false,
    stopData: [],
    distanceData: [],
    vehicles: [],
  };
  assert.deepEqual(build(input), []);
  const initialLayers = layersCreated,
    initialEditingReads = editingReads;
  for (const stationZoom of [7, 8, 9, 10, 5])
    assert.deepEqual(build({ ...input, stationZoom }), []);
  assert.equal(priceReads, 0);
  assert.equal(
    editingReads,
    initialEditingReads,
    'unchanged hidden station data is not rescanned',
  );
  assert.equal(layersCreated, initialLayers);
  const visible = build({ ...input, stationsVisible: true });
  assert.deepEqual(
    visible.map(layer => layer.props.id),
    ['fuel-points'],
  );
  assert.equal(
    visible[0].props.data[0],
    hidden,
    'visibility restores the original selectable station',
  );
  assert.equal(
    priceReads,
    0,
    'visible markers do not calculate, format or declutter prices',
  );
});

test('hidden active edits retain their original selectable point, color and separate editing badge', () => {
  let unrelatedPriceReads = 0;
  class Layer {
    constructor(props) {
      this.props = props;
    }
  }
  const build = createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  const ordinary = station('ordinary');
  Object.defineProperty(ordinary, 'price', {
    get() {
      unrelatedPriceReads++;
      return 4.123;
    },
  });
  const edited = station('edited', { editing: true, recommended: true });
  const input = {
    lines: [],
    stationData: [ordinary, edited],
    stationsVisible: false,
    stopData: [],
    distanceData: [],
    vehicles: [],
  };
  const initial = build(input);
  assert.deepEqual(
    initial.map(layer => layer.props.id),
    ['fuel-editing-points', 'fuel-editing-ring', 'fuel-editing-label'],
  );
  const [point, ring, badge] = initial.map(layer => layer.props);
  assert.equal(point.data[0], edited);
  assert.equal(point.getRadius, 8);
  assert.equal(point.getFillColor(edited), edited.color);
  assert.equal(ring.getRadius, 14);
  assert.equal(badge.getText(edited), 'Editing');
  assert.deepEqual(badge.getPixelOffset, [0, -25]);
  for (const stationZoom of [7, 8, 6]) {
    const layers = build({ ...input, stationZoom });
    for (let index = 0; index < layers.length; index++)
      assert.equal(layers[index], initial[index]);
  }
  assert.equal(unrelatedPriceReads, 0);
  // With fuel off, closing the editor leaves nothing behind: the plan's own
  // stops belong to the fuel layer like everything else in it.
  assert.deepEqual(
    build({
      ...input,
      stationData: [ordinary, { ...edited, editing: false }],
    }).map(layer => layer.props.id),
    [],
  );
  assert.deepEqual(
    build({
      ...input,
      stationsVisible: true,
      stationData: [ordinary, { ...edited, editing: false }],
    })
      .map(layer => layer.props.id)
      .filter(id => id.startsWith('fuel-recommendation')),
    ['fuel-recommendation-points', 'fuel-recommendation-rings'],
  );
});

test('every station has the same 16px circle with no inside price, regardless of spacing or price validity', () => {
  class Layer {
    constructor(props) {
      this.props = props;
    }
  }
  const build = createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  const ordinary = station('ordinary'),
    planned = station('planned', { recommended: true, numbers: '1/3' });
  const selected = [];
  const input = {
    lines: [],
    stationData: [ordinary, planned],
    stationsVisible: true,
    stopData: [],
    distanceData: [],
    vehicles: [],
    selectStation: info => selected.push(info.object.id),
  };
  const initial = build(input),
    byId = new Map(initial.map(layer => [layer.props.id, layer]));
  assert.deepEqual(
    [...byId.keys()],
    [
      'fuel-points',
      'fuel-recommendation-points',
      'fuel-recommendation-rings',
      'fuel-recommendation-numbers',
    ],
  );
  const points = byId.get('fuel-points').props,
    plannedPoints = byId.get('fuel-recommendation-points').props;
  assert.equal(points.data[0], ordinary);
  assert.equal(points.getRadius, 8);
  // A step larger than the stations it was chosen from, and its ring still
  // narrower than a stop's badge: a fuel stop is not more than a stop.
  assert.equal(plannedPoints.getRadius, 10);
  assert.equal(plannedPoints.getFillColor(planned), planned.color);
  const ring = byId.get('fuel-recommendation-rings').props;
  assert.equal(ring.getRadius, 14);
  assert.ok(ring.getRadius * 2 < 34, 'narrower than a stop badge');
  const order = byId.get('fuel-recommendation-numbers').props;
  assert.equal(order.getText(planned), 'Fuel 1/3');
  // Above the ring, clear of it: the label used to sit on the marker.
  assert.deepEqual(order.getPixelOffset, [0, -27]);
  for (const props of [points, plannedPoints, ring, order])
    props.onClick({ object: props.data[0] });
  assert.deepEqual(selected, ['ordinary', 'planned', 'planned', 'planned']);
  for (const layer of build({
    ...input,
    vehicles: [{ unit: 'truck', position: [-80, 36] }],
  }))
    if (byId.has(layer.props.id))
      assert.equal(
        layer,
        byId.get(layer.props.id),
        'truck motion retains all station layers',
      );
  for (const stationZoom of [3, 4, 8, 15]) {
    const zoomed = build({ ...input, stationZoom });
    for (const layer of zoomed)
      assert.equal(
        layer,
        byId.get(layer.props.id),
        'zoom never rebuilds station geometry or labels',
      );
  }
  const invalid = [null, undefined, NaN, Infinity, 0, -1].map((price, index) =>
    station(`invalid-${index}`, { price }),
  );
  const unpriced = build({
    ...input,
    stationData: [ordinary, planned, ...invalid],
  }).find(layer => layer.props.id === 'fuel-points').props;
  assert.deepEqual(
    unpriced.data,
    [ordinary, ...invalid],
    'price validity never filters a station',
  );
  assert.equal(unpriced.getRadius, 8);
});

test('camera changes need no price listener while station data retains exact popup prices and canonical selection', t => {
  let frame, overlay;
  const previous = [
    globalThis.requestAnimationFrame,
    globalThis.cancelAnimationFrame,
  ];
  globalThis.requestAnimationFrame = callback => {
    frame = callback;
    return 1;
  };
  globalThis.cancelAnimationFrame = () => {
    frame = null;
  };
  t.after(
    () =>
      ([globalThis.requestAnimationFrame, globalThis.cancelAnimationFrame] =
        previous),
  );
  class Layer {
    constructor(props) {
      this.props = props;
    }
  }
  class Overlay {
    constructor(props) {
      this.props = props;
      overlay = this;
    }
    setMap() {}
    setProps(props) {
      this.props = props;
    }
    finalize() {}
  }
  const listeners = new Map();
  const map = {
    getDiv: () => ({ dataset: {} }),
    addListener(name, callback) {
      listeners.set(name, callback);
      return {
        remove() {
          listeners.delete(name);
        },
      };
    },
  };
  const scene = createScene(map, {
    GoogleMapsOverlay: Overlay,
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  t.after(() => scene.dispose());
  const flush = () => {
    const callback = frame;
    frame = null;
    callback?.();
  };
  const get = id => overlay.props.layers.find(layer => layer.props.id === id);
  const selected = [];
  const points = scene.createStationPointLayer(map, id => selected.push(id));
  points.setPoint(
    'one',
    { lng: -80, lat: 36 },
    'rgb(21,128,61)',
    false,
    false,
    '',
    false,
    5.613,
  );
  points.setVisible(true);
  flush();
  assert.equal(get('fuel-points').props.data[0].price, 5.613);
  assert.equal(get('fuel-price-labels'), undefined);
  for (const name of ['idle', 'bounds_changed'])
    assert.equal(
      listeners.has(name),
      false,
      'camera movement never scans or formats station data',
    );
  const originalStationLayer = get('fuel-points');
  listeners.get('zoom_changed')();
  flush();
  assert.equal(
    get('fuel-points'),
    originalStationLayer,
    'truck zoom listener leaves station layers intact',
  );
  points.setPoint(
    'one',
    { lng: -80, lat: 36 },
    'rgb(245,158,11)',
    false,
    false,
    '',
    false,
    5.286,
  );
  points.redraw();
  flush();
  const updated = get('fuel-points').props;
  assert.equal(updated.data[0].price, 5.286);
  assert.deepEqual(updated.getFillColor(updated.data[0]), [245, 158, 11]);
  updated.onClick({ object: updated.data[0] });
  assert.deepEqual(selected, ['one']);
  assert.equal(get('fuel-price-labels'), undefined);
  points.setPoint(
    'one',
    { lng: -80, lat: 36 },
    'rgb(128,128,128)',
    false,
    false,
    '',
    false,
    null,
  );
  points.redraw();
  flush();
  assert.equal(get('fuel-points').props.data.length, 1);
  scene.dispose();
  assert.equal(listeners.size, 0);
});
