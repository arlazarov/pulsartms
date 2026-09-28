import test from 'node:test';
import assert from 'node:assert/strict';
import { routeLayers } from '../../Scripts/fleetMap/rendering/routeAppearance.ts';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.ts';
import { sceneMetrics } from '../../Scripts/fleetMap/rendering/sceneMetrics.ts';

// An upcoming road at rest, and the white keyline the dark map draws
// around it.
const secondary = sceneMetrics.routeSecondaryWidth;
const keyline = secondary + sceneMetrics.routeOutlineWidth;
// The light map draws every road as a finer line of its colour in a haze of
// the same colour, with no white casing (the owner, September 27): three
// quarters of the width, never under three pixels, the haze five wider.
const fine = width => Math.max(3, width * 0.75);
// Empty miles as the HUD draws them (the owner, September 27): a fine
// dashed amber line over a faint amber halo, no white casing, one width
// whether picked or not.
const lightAmber = [217, 119, 6, 235];
const darkAmber = [251, 191, 36, 235];
const emptyWidth = 2.5;
const emptyHalo = 8;
const emptyDashes = [2.2, 2.2];

// The map's theme is the page's; without a page the light map stands.
function onDarkMap(check) {
  const outer = globalThis.document;
  globalThis.document = { documentElement: { dataset: { theme: 'dark' } } };
  try {
    check();
  } finally {
    globalThis.document = outer;
  }
}
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
  // Empty miles carry no load, so they are amber wherever they appear.
  assert.deepEqual(routeLayers(line, Layer)[1].getColor, lightAmber);
  onDarkMap(() => {
    line.strokeWeight = 3;
    assert.deepEqual(routeLayers(line, Layer)[1].getColor, darkAmber);
  });
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
  assert.equal(layers[0].getWidth, emptyHalo);
  assert.equal(layers[1].getWidth, emptyWidth);
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

test('future routes retain their hue with a secondary stroke and a bounded outline', () => {
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
  assert.equal(route.getWidth, fine(secondary));
  // Light: a haze of the road's own colour, not a white casing.
  assert.equal(outline.getWidth, fine(secondary) + 5);
  assert.deepEqual(outline.getColor, [145, 105, 201, 28]);
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
    lightAmber,
  );
  // The dark map keeps the bounded white keyline. A theme switch makes the
  // map and its lines again, so the dark line is a new one.
  onDarkMap(() => {
    const [darkOutline, darkRoute] = routeLayers(
      { id: 'future', data: future.data, strokeWeight: 2, routeRole: 'future' },
      Layer,
    );
    assert.equal(darkRoute.getWidth, secondary);
    assert.equal(darkOutline.getWidth, keyline);
    assert.deepEqual(darkOutline.getColor, [255, 255, 255, 210]);
    assert.equal(darkOutline.data, darkRoute.data);
  });
});

test('zoomed-out current future and empty routes keep a readable minimum width', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const check = (role, dark) => {
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
    const drawn = width => (dark ? width : fine(width));
    const casing = width =>
      dark ? width + sceneMetrics.routeOutlineWidth : fine(width) + 5;
    const [outline, route] = routeLayers(line, Layer);
    if (role === 'deadhead') {
      assert.equal(route.getWidth, emptyWidth);
      assert.equal(outline.getWidth, emptyHalo);
    } else {
      const rest = role === 'current' ? 5 : secondary;
      assert.equal(route.getWidth, drawn(rest));
      assert.equal(outline.getWidth, casing(rest));
      assert.ok(route.getWidth >= 3, 'never thinner than three pixels');
    }
    assert.equal(route.widthUnits, 'pixels');
    assert.equal(routeLayers(line, Layer)[1], route);
    line.strokeWeight = 4;
    line.routeSelected = true;
    assert.equal(
      routeLayers(line, Layer)[1].getWidth,
      role === 'deadhead' ? emptyWidth : drawn(role === 'current' ? 5 : 7),
      'explicit wider future strokes remain supported',
    );
  };
  for (const role of ['current', 'future', 'deadhead']) check(role, false);
  onDarkMap(() => {
    for (const role of ['current', 'future', 'deadhead']) check(role, true);
  });
});

// Measured on the dark map, which keeps the full widths; the light map
// draws the same widths finer (the owner, September 27).
test('current route stays at five pixels while a selected future route gets explicit emphasis', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  onDarkMap(() => {
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
    assert.equal(routeLayers(future, Layer)[1].getWidth, secondary);
    future.routeSelected = true;
    assert.equal(
      routeLayers(future, Layer)[1].getWidth,
      7,
      'selection expands only the chosen future route',
    );
    future.routeSelected = false;
    assert.equal(routeLayers(future, Layer)[1].getWidth, secondary);
  });
  const light = {
    id: 'current',
    data: [],
    strokeWeight: 2,
    routeRole: 'current',
  };
  assert.equal(routeLayers(light, Layer)[1].getWidth, fine(5));
});

test('empty-mile dashes keep cached extensions without changing geometry', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const extensions = [{ dash: true, highPrecisionDash: true }];
  const path = Array.from({ length: 1000 }, (_, index) => [index / 10000, 0]);
  const data = [path];
  for (const role of ['deadhead']) {
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
      assert.equal(layer.capRounded, true);
      assert.equal(layer.jointRounded, true);
    }
    // The dashed line carries the extension; the halo under it is solid,
    // so there are no two dash patterns to keep aligned.
    const [halo, dashes] = initial;
    assert.equal(dashes.extensions, extensions);
    assert.equal(
      dashes.dashJustified,
      false,
      'high-precision continuity must not restart per segment',
    );
    assert.deepEqual(dashes.getDashArray, emptyDashes);
    assert.equal(halo.getDashArray, undefined);
    assert.equal(halo.pickable, false);
    assert.deepEqual(dashes.getColor, lightAmber);
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

test('scene-layer composition forwards one stable dash extension port only to empty miles', () => {
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
  const lines = ['current', 'deadhead'].map(routeRole => ({
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
  // Only the dashed line of the empty miles; its halo is solid (the owner,
  // September 27).
  for (const layer of initial)
    assert.equal(
      layer.props.extensions,
      layer.props.id === 'deadhead' ? routeDashExtensions : undefined,
    );
  assert.ok(initial.some(layer => layer.props.id === 'deadhead'));
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
    fine(5),
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
    [sceneMetrics.routeFutureOpacity, sceneMetrics.routeFutureOpacity, 1, 1],
  );
  assert.ok(roads().every(layer => layer.data === data));
});

// The road already driven is a HUD trace (the owner, September 27): a fine
// solid line of the instrument ink over a faint halo of the same, no white
// casing, quieter and finer than the road ahead.
test('traveled route stays solid and dimmer than remaining route', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const check = ink => {
    const line = {
      id: 'history',
      data: [],
      strokeWeight: 4,
      routeRole: 'traveled',
    };
    const history = routeLayers(line, Layer);
    const current = routeLayers({ ...line, routeRole: 'current' }, Layer);
    assert.ok(history[1].getColor[3] > 0);
    assert.ok(history[1].getColor[3] < current[1].getColor[3]);
    assert.ok(history[0].getColor[3] < history[1].getColor[3]);
    assert.deepEqual(history[1].getColor.slice(0, 3), ink);
    assert.deepEqual(history[0].getColor.slice(0, 3), ink);
    assert.ok(history[1].getWidth < current[1].getWidth);
    assert.equal(history[1].getDashArray, undefined);
    assert.equal(history[0].pickable, false);
    assert.equal(history[0].data, history[1].data);
    assert.equal(routeLayers(line, Layer), history);
  };
  check([14, 116, 144]);
  onDarkMap(() => check([34, 211, 238]));
});

// An unpicked later load's road is fine dashes in the load's colour, told
// from empty miles (dashed amber) by the dashes' rhythm; the picked one is
// solid at full width (the owner, September 27, replacing the solid rule of
// September 26).
test('an unpicked upcoming road is fine dashes in its colour and empty miles are dashed amber', () => {
  class Layer {
    constructor(options) {
      Object.assign(this, options);
    }
  }
  const road = role => ({
    id: role,
    data: [
      [
        [0, 0],
        [1, 1],
      ],
    ],
    strokeWeight: 2,
    routeRole: role,
    routeColor: futureRouteColor(0),
  });
  const extensions = [{ dash: true }];
  const check = dark => {
    const [outline, loaded] = routeLayers(road('future'), Layer, extensions);
    assert.deepEqual(loaded.getColor, futureRouteColor(0));
    assert.deepEqual(loaded.getDashArray, sceneMetrics.routeFutureDashArray);
    assert.equal(loaded.extensions, extensions);
    assert.deepEqual(
      outline.getColor,
      dark ? [255, 255, 255, 210] : [...futureRouteColor(0).slice(0, 3), 28],
    );
    // The outline under the dashes keeps the same physical dashes and gaps.
    for (let index = 0; index < 2; index++)
      assert.ok(
        Math.abs(
          outline.getDashArray[index] * outline.getWidth -
            loaded.getDashArray[index] * loaded.getWidth,
        ) < 1e-9,
        'outline and fill have matching physical dash/gap lengths',
      );
    const picked = routeLayers(
      { ...road('future'), routeSelected: true },
      Layer,
      extensions,
    )[1];
    assert.equal(picked.getDashArray, undefined);
    assert.equal(picked.getWidth, dark ? 5 : fine(5));
    const empty = routeLayers(road('deadhead'), Layer, extensions)[1];
    assert.deepEqual(empty.getColor, dark ? darkAmber : lightAmber);
    assert.deepEqual(empty.getDashArray, emptyDashes);
    assert.notDeepEqual(empty.getDashArray, loaded.getDashArray);
    assert.equal(empty.extensions, extensions);
    // Picked, empty miles stay one fine dashed line.
    assert.equal(
      routeLayers(
        { ...road('deadhead'), routeSelected: true },
        Layer,
        extensions,
      )[1].getWidth,
      emptyWidth,
    );
  };
  check(false);
  onDarkMap(() => check(true));
});
