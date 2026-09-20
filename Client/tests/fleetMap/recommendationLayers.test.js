import test from 'node:test';
import assert from 'node:assert/strict';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.js';
import { sceneMetrics as metrics } from '../../Scripts/fleetMap/rendering/sceneMetrics.js';

test('recommendation rings retain anchors and selection without floating distance labels', () => {
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
  const station = {
    id: 'a',
    position: [-79, 40],
    recommended: true,
    label: '10 mi · 16 km',
  };
  const selectStation = () => {};
  const input = {
    lines: [],
    stationData: [station],
    stationsVisible: true,
    stopData: [],
    distanceData: [],
    vehicles: [],
    selectStation,
  };
  const layers = build(input);
  const ring = layers.find(l => l.props.id === 'fuel-recommendation-rings');
  assert.deepEqual(
    layers.map(layer => layer.props.id),
    ['fuel-recommendation-points', 'fuel-recommendation-rings'],
  );
  assert.equal(ring.props.getPosition(station), station.position);
  assert.equal(ring.props.onClick, selectStation);
  assert.equal(
    build(input).find(l => l.props.id === ring.props.id),
    ring,
  );
  assert.equal(ring.props.radiusUnits, 'pixels');
  assert.equal(ring.props.getRadius, 14);
  assert.deepEqual(
    build({ ...input, stationsVisible: false }).map(layer => layer.props.id),
    ['fuel-recommendation-points', 'fuel-recommendation-rings'],
  );
});

test('all station fills cover the route and recommendation rings remain above ordinary stations', () => {
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
  const recommended = {
    id: 'recommended',
    position: [-79, 40],
    recommended: true,
    color: [10, 150, 10],
  };
  const ordinary = {
    id: 'ordinary',
    position: [-79, 40],
    recommended: false,
    color: [200, 100, 10],
  };
  const path = [
    [-80, 40],
    [-79, 40],
  ];
  const selectStation = () => {};
  const input = {
    stationData: [recommended, ordinary],
    stationsVisible: true,
    lines: [{ id: 'route', map: true, path, data: [path], strokeWeight: 4 }],
    stopData: [],
    distanceData: [],
    vehicles: [],
    selectStation,
  };
  const layers = build(input);
  assert.deepEqual(
    layers.map(layer => layer.props.id),
    [
      'route-outline',
      'route',
      'fuel-points',
      'fuel-recommendation-points',
      'fuel-recommendation-rings',
    ],
  );
  assert.deepEqual(layers[2].props.data, [ordinary]);
  assert.equal(layers[2].props.getRadius, 8);
  assert.equal(layers[2].props.getFillColor(ordinary), ordinary.color);
  const points = layers[3];
  assert.deepEqual(points.props.data, [recommended]);
  // A planned stop is drawn larger than the stations it was chosen from.
  assert.equal(points.props.getRadius, 10);
  assert.deepEqual(points.props.getFillColor(recommended), recommended.color);
  assert.equal(points.props.pickable, true);
  assert.equal(points.props.onClick, selectStation);
  assert.equal(build(input)[3], points);
  const changed = build({
    ...input,
    stationData: [ordinary, { ...recommended, recommended: false }],
  });
  assert.ok(
    changed.every(layer => !layer.props.id.startsWith('fuel-recommendation')),
  );
  assert.equal(
    changed.find(layer => layer.props.id === 'fuel-points').props.data.length,
    2,
  );
});

test('current, future, deadhead and selected future roads stay below all fuel points across selection and visibility changes', () => {
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
  const path = [
    [-80, 40],
    [-79, 40],
  ];
  const roads = [
    { id: 'current', routeRole: 'current', zIndex: 1 },
    { id: 'future', routeRole: 'future', zIndex: 0 },
    { id: 'deadhead', routeRole: 'deadhead', zIndex: 0 },
    { id: 'selected-future', routeRole: 'future', zIndex: 10 },
  ].map(line => ({ ...line, map: true, path, data: [path], strokeWeight: 4 }));
  const ordinary = {
    id: 'ordinary',
    position: [-79, 40],
    color: [210, 140, 20],
  };
  const recommended = {
    id: 'recommended',
    position: [-79, 40],
    color: [20, 150, 30],
    recommended: true,
    numbers: '1',
  };
  const clicks = [];
  const selectStation = info => clicks.push(info.object.id);
  const stop = {
    id: 1,
    position: [-79, 40],
    number: '2',
    job: 'Delivery',
    markerLabel: 'DEL 2',
    markerOffsetX: 0,
    markerOffsetY: -18,
  };
  const input = {
    lines: roads,
    stationData: [ordinary, recommended],
    stationsVisible: true,
    stopData: [stop],
    distanceData: [],
    vehicles: [{ unit: '11005', position: [-79, 40] }],
    selectStation,
  };
  function assertOrder(layers) {
    const ids = layers.map(layer => layer.props.id);
    for (const road of roads)
      for (const id of [road.id, `${road.id}-outline`]) {
        assert.ok(ids.includes(id), `${id} is rendered`);
        assert.ok(
          ids.indexOf(id) < ids.indexOf('fuel-points'),
          `${id} is below ordinary stations`,
        );
        assert.ok(
          ids.indexOf(id) < ids.indexOf('fuel-recommendation-points'),
          `${id} is below recommended stations`,
        );
      }
    assert.ok(
      ids.indexOf('fuel-points') < ids.indexOf('fuel-recommendation-points'),
    );
    assert.ok(
      ids.indexOf('fuel-recommendation-points') <
        ids.indexOf('fuel-recommendation-rings'),
    );
    assert.ok(
      ids.indexOf('fuel-recommendation-rings') <
        ids.indexOf('fuel-recommendation-numbers'),
    );
    for (const id of [
      'route-stop-1-points',
      'route-stop-1-numbers',
      'truck-icons',
      'truck-numbers',
    ])
      assert.ok(
        ids.indexOf(id) > ids.indexOf('fuel-recommendation-numbers'),
        `${id} retains foreground priority`,
      );
  }
  const initial = build(input);
  assertOrder(initial);
  const ordinaryLayer = initial.find(layer => layer.props.id === 'fuel-points');
  const recommendedLayer = initial.find(
    layer => layer.props.id === 'fuel-recommendation-points',
  );
  for (const [layer, station] of [
    [ordinaryLayer, ordinary],
    [recommendedLayer, recommended],
  ]) {
    assert.equal(
      layer.props.getFillColor(station),
      station.color,
      'price colors are unchanged',
    );
    assert.equal(layer.props.pickable, true);
    layer.props.onClick({ object: station });
  }
  assert.deepEqual(clicks, ['ordinary', 'recommended']);
  const selected = build({
    ...input,
    lines: roads.map(line =>
      line.id === 'selected-future'
        ? { ...line, zIndex: 1000, strokeWeight: 6 }
        : line,
    ),
  });
  assertOrder(selected);
  assert.equal(
    selected.find(layer => layer.props.id === 'fuel-points'),
    ordinaryLayer,
    'road emphasis does not rebuild the cached ordinary station layer',
  );
  assert.equal(
    selected.find(layer => layer.props.id === 'fuel-recommendation-points'),
    recommendedLayer,
  );
  const hidden = build({ ...input, stationsVisible: false });
  assert.ok(hidden.every(layer => layer.props.id !== 'fuel-points'));
  assert.deepEqual(
    hidden
      .filter(layer => layer.props.id.startsWith('fuel-'))
      .map(layer => layer.props.id),
    [
      'fuel-recommendation-points',
      'fuel-recommendation-rings',
      'fuel-recommendation-numbers',
    ],
  );
  assert.ok(hidden.some(layer => layer.props.id === 'current'));
  const restored = build(input);
  assertOrder(restored);
  assert.equal(
    restored
      .find(layer => layer.props.id === 'fuel-points')
      .props.getFillColor(ordinary),
    ordinary.color,
  );
  assert.equal(
    restored
      .find(layer => layer.props.id === 'fuel-recommendation-points')
      .props.getFillColor(recommended),
    recommended.color,
  );
});

test('fuel visits use compact rectangular order badges without changing selectable price-colored rings', () => {
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
  const station = {
    id: 'a',
    position: [-79, 40],
    recommended: true,
    numbers: '1/3',
    color: [210, 130, 10],
  };
  const selectStation = () => {},
    setHover = () => {};
  const input = {
    lines: [],
    stationData: [station],
    stationsVisible: true,
    stopData: [],
    distanceData: [],
    vehicles: [],
    selectStation,
    setHover,
  };
  const layers = build(input);
  const assertStation = result => {
    assert.deepEqual(
      result.map(layer => layer.props.id),
      [
        'fuel-recommendation-points',
        'fuel-recommendation-rings',
        'fuel-recommendation-numbers',
      ],
    );
    for (const layer of result) {
      assert.equal(layer.props.pickable, true);
      assert.equal(layer.props.onClick, selectStation);
      assert.equal(layer.props.onHover, setHover);
    }
    assert.equal(result[0].props.getFillColor(station), station.color);
    assert.deepEqual(result[1].props.getLineColor, [49, 94, 234]);
    assert.equal(result[0].props.getRadius, 10);
    assert.equal(result[1].props.getRadius, 14);
    const badge = result[2].props;
    assert.equal(badge.getText(badge.data[0]), `Fuel ${badge.data[0].numbers}`);
    assert.equal(badge.getPosition(station), station.position);
    // Above the ring, clear of it: the label used to sit on the marker.
    assert.deepEqual(badge.getPixelOffset, [0, -27]);
    assert.equal(badge.getSize, 12);
    assert.deepEqual(badge.backgroundPadding, [6, 4]);
    assert.equal(
      badge.backgroundBorderRadius,
      4,
      'fuel badges stay rectangular, unlike load circles',
    );
    assert.deepEqual(badge.getBackgroundColor, [30, 41, 59]);
    assert.deepEqual(badge.getColor, [255, 255, 255]);
    assert.equal(
      badge.getText(badge.data[0]).includes('mi'),
      false,
      'distance remains inside popup cards',
    );
  };
  assertStation(layers);
  assert.equal(build(input)[1], layers[1]);
  assert.equal(
    build(input)[2],
    layers[2],
    'unchanged camera updates reuse the order badge',
  );
  const dense = build({ ...input, pixelRatio: 2 });
  assert.equal(
    dense[1],
    layers[1],
    'font density does not rebuild station rings',
  );
  assert.deepEqual(dense[2].props.fontSettings, { sdf: false, fontSize: 24 });
  assertStation(
    build({ ...input, stationData: [{ ...station, numbers: '2/4' }] }),
  );
  assert.equal(station.numbers, '1/3', 'popup visit metadata stays untouched');
  assertStation(build({ ...input, stationsVisible: false }));
  const filtered = build({
    ...input,
    stationData: [{ ...station, recommended: false }],
  });
  assert.deepEqual(
    filtered.map(layer => layer.props.id),
    ['fuel-points'],
    'filtered future recommendations have no ring or order badge',
  );
});

test('ordinary stations and recommendations without visit numbers never receive a fuel order badge', () => {
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
  const stations = [
    { id: 'ordinary', numbers: '2' },
    { id: 'missing', recommended: true },
    { id: 'empty', recommended: true, numbers: ' ' },
    { id: 'numbered', recommended: true, numbers: '4' },
  ].map(station => ({ ...station, position: [-79, 40], color: [20, 150, 30] }));
  const layers = build({
    lines: [],
    stationData: stations,
    stationsVisible: true,
    stopData: [],
    distanceData: [],
    vehicles: [],
  });
  const labels = layers.find(
    layer => layer.props.id === 'fuel-recommendation-numbers',
  ).props;
  assert.deepEqual(
    labels.data.map(station => station.id),
    ['numbered'],
  );
  assert.equal(labels.getText(labels.data[0]), 'Fuel 4');
  // Above the ring it names, not on it: the offset clears the ring's radius
  // and half the label's own height, with a gap left over. At twenty-one it
  // sat across the marker.
  const half =
    metrics.fuelVisitLabelSize / 2 + metrics.fuelVisitLabelPadding[1];
  assert.deepEqual(labels.getPixelOffset, [0, -metrics.fuelVisitLabelOffset]);
  assert.ok(
    metrics.fuelVisitLabelOffset - half - metrics.recommendationRadius >= 2,
  );
  assert.deepEqual(
    stations.map(station => station.numbers),
    ['2', undefined, ' ', '4'],
  );
});

test('editing has one visible price-colored point, a larger ring and a distinct badge even when stations are hidden', () => {
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
  const editing = {
    id: 'edited',
    position: [-79, 40],
    editing: true,
    recommended: true,
    numbers: '2',
    color: [20, 150, 30],
  };
  const ordinary = { id: 'other', position: [-80, 40], color: [210, 140, 20] };
  const input = {
    lines: [],
    stationData: [editing, ordinary],
    stationsVisible: false,
    stopData: [],
    distanceData: [],
    vehicles: [],
    selectStation() {},
  };
  const layers = build(input);
  assert.deepEqual(
    layers.map(layer => layer.props.id),
    ['fuel-editing-points', 'fuel-editing-ring', 'fuel-editing-label'],
  );
  assert.ok(
    layers.every(
      layer => layer.props.data.length === 1 && layer.props.data[0] === editing,
    ),
  );
  assert.equal(layers[0].props.getFillColor(editing), editing.color);
  assert.equal(
    layers[1].props.filled,
    false,
    'highlight must not tint the existing price color',
  );
  assert.equal(layers[1].props.getRadius, 14);
  assert.equal(layers[2].props.getText(editing), 'Editing');
  assert.equal(
    build(input)[2],
    layers[2],
    'camera-only frames retain the same edit badge',
  );
  const visible = build({ ...input, stationsVisible: true });
  assert.equal(
    visible.some(layer => layer.props.id === 'fuel-recommendation-numbers'),
    false,
    'the edit badge replaces the active station order badge without overlap',
  );
  assert.ok(visible.some(layer => layer.props.id === 'fuel-points'));
  assert.deepEqual(
    build({
      ...input,
      stationData: [{ ...editing, editing: false }, ordinary],
    }).map(layer => layer.props.id),
    [
      'fuel-recommendation-points',
      'fuel-recommendation-rings',
      'fuel-recommendation-numbers',
    ],
    'closing the editor restores planned fuel without showing ordinary stations',
  );
});
