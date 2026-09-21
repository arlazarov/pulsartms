import test from 'node:test';
import assert from 'node:assert/strict';
import { routeLayers } from '../../Scripts/fleetMap/rendering/routeAppearance.ts';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.js';
import {
  currentRouteColor,
  currentRouteLineColor,
  futureRouteColor,
} from '../../Scripts/fleetMap/rendering/routePalette.ts';

test('load palette reuses opaque named colors with readable contrast on white', () => {
  const colors = [
    currentRouteColor,
    ...Array.from({ length: 5 }, (_, index) => futureRouteColor(index)),
  ];
  assert.deepEqual(colors, [
    [40, 76, 220, 255],
    [124, 58, 237, 255],
    [32, 122, 99, 255],
    [159, 52, 80, 255],
    [176, 80, 9, 255],
    [128, 96, 50, 255],
  ]);
  assert.equal(
    new Set(colors.map(color => color.join(','))).size,
    colors.length,
  );
  assert.deepEqual(currentRouteLineColor, [0, 106, 235, 255]);
  for (const color of [...colors, currentRouteLineColor]) {
    assert.ok(Object.isFrozen(color));
    const linear = color
      .slice(0, 3)
      .map(value => value / 255)
      .map(value =>
        value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4,
      );
    const luminance =
      linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
    assert.ok(
      1.05 / (luminance + 0.05) >= 4.5,
      `Insufficient white contrast: ${color}`,
    );
  }
  assert.equal(futureRouteColor(5), futureRouteColor(0));
  for (const index of [-1, NaN, Infinity])
    assert.equal(futureRouteColor(index), futureRouteColor(0));
});

test('per-load route colors invalidate only changed appearance and keep role defaults available', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const line = {
    id: 'future',
    data: [
      [
        [0, 0],
        [1, 1],
      ],
    ],
    strokeWeight: 2,
    routeRole: 'future',
    routeColor: futureRouteColor(0),
  };
  const original = routeLayers(line, Layer);
  assert.equal(original[1].getColor, futureRouteColor(0));
  line.routeColor = [...futureRouteColor(0)];
  assert.equal(
    routeLayers(line, Layer),
    original,
    'equal colors keep their cached GPU pair',
  );
  line.routeColor = futureRouteColor(1);
  const recolored = routeLayers(line, Layer);
  assert.notEqual(recolored, original);
  assert.equal(recolored[1].getColor, futureRouteColor(1));
  assert.equal(recolored[1].data, original[1].data);
  assert.equal(recolored[1].getWidth, original[1].getWidth);
  line.strokeWeight = 4;
  assert.equal(
    routeLayers(line, Layer)[1].getColor,
    futureRouteColor(1),
    'selection keeps the load color',
  );
  delete line.routeColor;
  assert.deepEqual(routeLayers(line, Layer)[1].getColor, [145, 105, 201, 240]);
  line.routeRole = 'deadhead';
  // Empty miles carry no load, so they are grey wherever they appear.
  assert.deepEqual(routeLayers(line, Layer)[1].getColor, [100, 116, 139, 235]);
});

test('route outline shares geometry and cached layers survive camera-only updates', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const line = {
    id: 'route-1',
    data: [
      [
        [0, 0],
        [1, 1],
      ],
    ],
    strokeWeight: 3,
    routeRole: 'deadhead',
  };
  const layers = routeLayers(line, Layer);
  assert.equal(layers.length, 2);
  assert.equal(layers[0].data, layers[1].data);
  assert.equal(layers[0].getWidth, 5);
  assert.equal(layers[1].getWidth, 3);
  assert.equal(routeLayers(line, Layer), layers);
  line.visible = false;
  assert.equal(routeLayers(line, Layer)[1].visible, false);
  line.visible = true;
  assert.equal(routeLayers(line, Layer)[1].visible, true);
  line.strokeWeight = 4;
  assert.notEqual(routeLayers(line, Layer), layers);
  const emptyColor = line.cachedLayer[1].getColor;
  line.routeRole = 'future';
  assert.notDeepEqual(routeLayers(line, Layer)[1].getColor, emptyColor);
});

test('future routes retain their hue with a secondary stroke and bounded white outline', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const future = {
    id: 'future',
    data: [
      [
        [0, 0],
        [1, 1],
      ],
    ],
    strokeWeight: 2,
    routeRole: 'future',
  };
  const [outline, route] = routeLayers(future, Layer);
  assert.deepEqual(route.getColor, [145, 105, 201, 240]);
  assert.equal(route.getWidth, 3);
  assert.equal(outline.getWidth, 5);
  assert.deepEqual(outline.getColor, [255, 255, 255, 210]);
  assert.equal(outline.data, route.data);
  assert.equal(
    routeLayers(future, Layer)[1],
    route,
    'unchanged display retains the cached GPU layer',
  );
  assert.equal(
    routeLayers({ ...future, routeRole: 'current' }, Layer)[1].getColor,
    currentRouteLineColor,
  );
  assert.deepEqual(
    routeLayers({ ...future, routeRole: 'deadhead' }, Layer)[1].getColor,
    [100, 116, 139, 235],
  );
});

test('zoomed-out current future and empty routes keep a readable minimum width', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  for (const role of ['current', 'future', 'deadhead']) {
    const line = {
      id: role,
      data: [
        [
          [0, 0],
          [1, 1],
        ],
      ],
      strokeWeight: 2,
      routeRole: role,
    };
    const [outline, route] = routeLayers(line, Layer);
    assert.equal(route.getWidth, role === 'current' ? 5 : 3);
    assert.equal(outline.getWidth, role === 'current' ? 7 : 5);
    assert.equal(route.widthUnits, 'pixels');
    assert.equal(routeLayers(line, Layer)[1], route);
    line.strokeWeight = 4;
    line.routeSelected = true;
    assert.equal(
      routeLayers(line, Layer)[1].getWidth,
      role === 'current' ? 5 : 7,
      'explicit wider future strokes remain supported',
    );
  }
});

test('current route stays at five pixels while a selected future route gets explicit emphasis', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  for (const strokeWeight of [2, 3, 4]) {
    const line = {
      id: 'current',
      data: [
        [
          [0, 0],
          [1, 1],
        ],
      ],
      strokeWeight,
      routeRole: 'current',
    };
    const [outline, route] = routeLayers(line, Layer);
    assert.equal(route.getWidth, 5);
    assert.equal(outline.getWidth, 7);
    assert.equal(routeLayers(line, Layer)[1], route);
    line.strokeWeight = 6;
    assert.equal(
      routeLayers(line, Layer)[1].getWidth,
      7.5,
      'larger explicit emphasis remains supported',
    );
  }
  const future = {
    id: 'future',
    data: [
      [
        [0, 0],
        [1, 1],
      ],
    ],
    strokeWeight: 4,
    routeRole: 'future',
  };
  assert.equal(routeLayers(future, Layer)[1].getWidth, 3);
  future.routeSelected = true;
  assert.equal(
    routeLayers(future, Layer)[1].getWidth,
    7,
    'selection expands only the chosen future route',
  );
  future.routeSelected = false;
  assert.equal(routeLayers(future, Layer)[1].getWidth, 3);
});

test('future and deadhead dashes share cached extensions and align their white outlines without changing geometry', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const extensions = [{ dash: true, highPrecisionDash: true }];
  const path = Array.from({ length: 1000 }, (_, index) => [index / 10000, 0]);
  const data = [path];
  for (const role of ['future', 'deadhead']) {
    const line = {
      id: role,
      data,
      strokeWeight: 2,
      routeRole: role,
      routeColor: futureRouteColor(1),
    };
    const initial = routeLayers(line, Layer, extensions);
    for (const layer of initial) {
      assert.equal(layer.data, data);
      assert.equal(
        layer.getPath(path),
        path,
        'dashes never split, copy or densify route points',
      );
      assert.equal(layer.extensions, extensions);
      assert.equal(
        layer.dashJustified,
        false,
        'high-precision continuity must not restart per segment',
      );
      assert.equal(layer.capRounded, true);
      assert.equal(layer.jointRounded, true);
    }
    assert.equal(initial[1].getColor, futureRouteColor(1));
    for (let index = 0; index < 2; index++)
      assert.equal(
        (initial[0].getDashArray[index] * initial[0].getWidth) / 2,
        (initial[1].getDashArray[index] * initial[1].getWidth) / 2,
        'outline and fill have matching physical dash/gap lengths',
      );
    for (let frame = 0; frame < 100; frame++)
      assert.equal(routeLayers(line, Layer, extensions), initial);
    line.strokeWeight = 4;
    const selected = routeLayers(line, Layer, extensions);
    assert.notEqual(selected, initial);
    assert.equal(selected[0].data, data);
    assert.equal(selected[1].data[0].length, 1000);
    assert.equal(selected[1].extensions, extensions);
    assert.notEqual(
      routeLayers(line, Layer, [...extensions]),
      selected,
      'a new renderer extension port invalidates the cache',
    );
    line.routeRole = 'current';
    const solid = routeLayers(line, Layer, extensions);
    assert.ok(
      solid.every(
        layer =>
          layer.extensions === undefined && layer.getDashArray === undefined,
      ),
    );
    assert.equal(solid[1].data, data);
    assert.equal(
      solid[1].getColor,
      currentRouteLineColor,
      'current route can be brighter without recoloring stop circles',
    );
  }
});

test('scene-layer composition forwards one stable dash extension port only to future routes', () => {
  class Layer {
    constructor(props) {
      this.props = props;
    }
  }
  const routeDashExtensions = [{}];
  const render = createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
    routeDashExtensions,
  });
  const path = [
      [0, 0],
      [1, 1],
    ],
    data = [path];
  const lines = ['current', 'future'].map(routeRole => ({
    id: routeRole,
    routeRole,
    path,
    data,
    map: {},
    strokeWeight: 2,
  }));
  const input = {
    lines,
    stationData: [],
    stopData: [],
    distanceData: [],
    vehicles: [],
  };
  const initial = render(input);
  for (const layer of initial)
    assert.equal(
      layer.props.extensions,
      layer.props.id.startsWith('future') ? routeDashExtensions : undefined,
    );
  assert.deepEqual(render({ ...input, stationZoom: 15 }), initial);
});

test('selected next routes dim every other road and restore their appearance when selection is hidden or cleared', () => {
  class Layer {
    constructor(options) {
      this.props = options;
      Object.assign(this, options);
    }
  }
  const render = createSceneLayers({
    ScatterplotLayer: Layer,
    PathLayer: Layer,
    IconLayer: Layer,
    TextLayer: Layer,
  });
  const data = [
    [
      [0, 0],
      [1, 1],
    ],
  ];
  const current = {
    id: 'current',
    map: {},
    path: data[0],
    data,
    strokeWeight: 3,
    routeRole: 'current',
    zIndex: 2,
  };
  const future = {
    id: 'future',
    map: {},
    path: data[0],
    data,
    strokeWeight: 2,
    routeRole: 'future',
    zIndex: 0,
  };
  const state = {
    lines: [current, future],
    stationData: [],
    stopData: [],
    distanceData: [],
    vehicles: [],
  };
  const roads = () =>
    render(state).filter(layer =>
      ['current', 'future', 'current-outline', 'future-outline'].includes(
        layer.id,
      ),
    );
  assert.deepEqual(
    roads().map(layer => layer.id),
    ['future-outline', 'future', 'current-outline', 'current'],
  );
  future.zIndex = 10;
  future.routeSelected = true;
  const selected = roads();
  assert.deepEqual(
    selected.map(layer => layer.id),
    ['current-outline', 'current', 'future-outline', 'future'],
  );
  assert.equal(
    selected.at(-1).getWidth,
    5,
    'selection changes priority without increasing thickness',
  );
  assert.deepEqual(selected.at(-1).getColor, [145, 105, 201, 240]);
  assert.deepEqual(
    selected.map(layer => layer.opacity),
    [0.4, 0.4, 1, 1],
    'the chosen upcoming load is the one road at full strength',
  );
  const dimmedCurrent = current.cachedLayer;
  assert.equal(current.routeMuted, undefined);
  assert.equal(
    roads()[0],
    dimmedCurrent[0],
    'unchanged selection reuses current-route appearance',
  );
  future.visible = false;
  assert.ok(
    roads().every(layer => layer.opacity === 1),
    'a hidden selected route cannot dim the current route',
  );
  future.visible = true;
  assert.equal(roads()[0].opacity, 0.4);
  future.map = null;
  assert.ok(
    roads().every(layer => layer.opacity === 1),
    'a detached selected route cannot dim the current route',
  );
  future.map = {};
  future.zIndex = 0;
  future.routeSelected = false;
  future.strokeWeight = 2;
  assert.deepEqual(
    roads().map(layer => layer.id),
    ['future-outline', 'future', 'current-outline', 'current'],
  );
  // With nothing chosen the current road is at full strength and the
  // upcoming one stays a step behind it rather than matching it.
  assert.deepEqual(
    roads().map(layer => layer.opacity),
    [0.7, 0.7, 1, 1],
  );
  assert.ok(roads().every(layer => layer.data === data));
});

test('traveled route stays solid and dimmer than remaining route', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const line = {
    id: 'history',
    data: [],
    strokeWeight: 4,
    routeRole: 'traveled',
  };
  const history = routeLayers(line, Layer);
  const current = routeLayers({ ...line, routeRole: 'current' }, Layer);
  assert.ok(history[1].opacity > 0);
  assert.ok(history[1].opacity < current[1].opacity);
  assert.deepEqual(history[1].getColor, current[1].getColor);
  assert.equal(history[1].getDashArray, undefined);
});
