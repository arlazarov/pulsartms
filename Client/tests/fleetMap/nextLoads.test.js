import test from 'node:test';
import assert from 'node:assert/strict';
import { createNextLoadsLayer } from '../../Scripts/fleetMap/routes/nextLoads.ts';
import { futureRouteColor } from '../../Scripts/fleetMap/rendering/routePalette.ts';
import { routeLayers } from '../../Scripts/fleetMap/rendering/routeAppearance.ts';
import { createSceneLayers } from '../../Scripts/fleetMap/rendering/sceneLayers.js';

function selectionFixture() {
  const markers = [],
    lines = [],
    selections = [];
  class Line {
    constructor(options) {
      Object.assign(this, options);
      lines.push(this);
    }
    setPath(path) {
      this.path = path;
      this.data = [path];
    }
    setMap(map) {
      this.map = map;
    }
    setOptions(options) {
      Object.assign(this, options);
    }
  }
  class Stop {
    constructor(options) {
      this.label = null;
      Object.assign(this, options);
      markers.push(this);
    }
    setDistance() {
      assert.fail(
        'Future stop information belongs to the bottom popup, not a floating label',
      );
    }
    setNumber(number) {
      this.number = number;
    }
    setVisible(visible) {
      this.visible = visible;
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop, (...args) =>
    selections.push(args),
  );
  const loads = ['first', 'second'].map((id, index) => {
    const stops = [
      { latitude: 40 + index, longitude: -80, job: 'Pickup' },
      { latitude: 40 + index, longitude: -79, job: 'Delivery' },
      { latitude: 40 + index, longitude: -78, job: 'Delivery' },
    ].map((stop, index) => ({ ...stop, id: `${id}-${index}` }));
    return {
      id,
      loadNumber: 12 + index,
      stops,
      deadhead: { miles: 10, points: stops.slice(0, 2) },
      legs: [
        { miles: 100, points: stops.slice(0, 2) },
        { miles: 50, points: stops.slice(1) },
      ],
    };
  });
  layer.set(loads);
  return { layer, markers, lines, selections, loads };
}

test('different execution legs of one load have independent selection', () => {
  const { layer, markers, lines, selections, loads } = selectionFixture();
  const scoped = loads.map((load, index) => ({
    ...load,
    id: 'shared-load',
    executionLegId: `leg-${index}`,
  }));
  layer.set(scoped);
  markers[6].onSelect();
  assert.deepEqual(selections.at(-1), ['shared-load', 0, 'leg-0']);
  assert.deepEqual(
    markers.slice(6).map(marker => marker.highlighted),
    [true, true, true, false, false, false],
  );
  assert.deepEqual(
    lines.slice(6).map(line => line.routeSelected),
    [true, true, true, false, false, false],
  );
  layer.set([scoped[1]]);
  assert.deepEqual(selections.at(-1), [null, 0]);
});

test('stop click pins every road and marker for its load without floating labels', () => {
  const { layer, markers, lines, selections } = selectionFixture();
  assert.ok(
    markers.slice(0, 3).every(marker => marker.color === futureRouteColor(0)),
  );
  assert.ok(
    markers.slice(3).every(marker => marker.color === futureRouteColor(1)),
  );
  assert.deepEqual(
    lines.map(line => line.routeColor),
    [
      undefined,
      futureRouteColor(0),
      futureRouteColor(0),
      undefined,
      futureRouteColor(1),
      futureRouteColor(1),
    ],
  );
  markers[2].onSelect();
  assert.deepEqual(selections, [['first', 2]]);
  assert.deepEqual(
    markers.map(marker => marker.highlighted),
    [true, true, true, false, false, false],
  );
  assert.ok(markers.every(marker => marker.label === null));
  assert.deepEqual(
    lines.map(line => [line.strokeWeight, line.zIndex]),
    [
      [2, 10],
      [2, 10],
      [2, 10],
      [2, 0],
      [2, 0],
      [2, 0],
    ],
  );
  assert.deepEqual(
    lines.map(line => line.routeMuted),
    [false, false, false, true, true, true],
  );
  assert.deepEqual(
    lines.map(line => line.routeSelected),
    [true, true, true, false, false, false],
  );
  // Pointing at a load says which road its badges belong to, without a
  // label floating out of the badge to say it.
  assert.ok(markers.every(marker => typeof marker.onHover === 'function'));
  markers[3].onHover(true);
  assert.deepEqual(
    lines.map(line => line.routeSelected),
    [true, true, true, false, false, false],
    'a load picked on purpose outranks whatever the cursor is over',
  );
  markers[3].onHover(false);
  markers[3].onSelect();
  assert.deepEqual(selections.at(-1), ['second', 0]);
  assert.deepEqual(
    markers.map(marker => marker.highlighted),
    [false, false, false, true, true, true],
  );
  assert.ok(markers.every(marker => marker.label === null));
  assert.deepEqual(
    lines.map(line => [line.strokeWeight, line.zIndex]),
    [
      [2, 0],
      [2, 0],
      [2, 0],
      [2, 10],
      [2, 10],
      [2, 10],
    ],
  );
  assert.deepEqual(
    lines.map(line => line.routeMuted),
    [true, true, true, false, false, false],
  );
  assert.deepEqual(
    lines.map(line => line.routeSelected),
    [false, false, false, true, true, true],
  );
  layer.clearSelection();
  assert.ok(
    markers.every(marker => !marker.highlighted && marker.label === null),
  );
  assert.ok(lines.every(line => line.strokeWeight === 2 && line.zIndex === 0));
  assert.ok(lines.every(line => line.routeMuted === false));
  assert.ok(lines.every(line => line.routeSelected === false));
  assert.deepEqual(
    lines.map(line => line.routeColor),
    [
      undefined,
      futureRouteColor(0),
      futureRouteColor(0),
      undefined,
      futureRouteColor(1),
      futureRouteColor(1),
    ],
    'selection never changes the load or deadhead color',
  );
});

test('unchanged polling preserves selected roads and never allocates or writes floating labels', () => {
  const { layer, markers, lines, loads, selections } = selectionFixture();
  markers[0].onSelect();
  const paths = lines.map(line => line.path);
  for (let index = 0; index < 3; index++) layer.set(structuredClone(loads));
  assert.equal(markers.length, 6);
  assert.equal(lines.length, 6);
  assert.ok(paths.every((path, index) => path === lines[index].path));
  assert.ok(markers.slice(0, 3).every(marker => marker.highlighted));
  assert.ok(markers.every(marker => marker.label === null));
  assert.deepEqual(selections, [['first', 0]]);
});

test('selection emphasizes the chosen road and subdues others without replacing route geometry', () => {
  const { layer, markers, lines, loads } = selectionFixture();
  class GpuLayer {
    constructor(props) {
      this.props = props;
      Object.assign(this, props);
    }
  }
  const extensions = [{}];
  const compose = createSceneLayers({
    PathLayer: GpuLayer,
    ScatterplotLayer: GpuLayer,
    IconLayer: GpuLayer,
    TextLayer: GpuLayer,
    routeDashExtensions: extensions,
  });
  const currentPath = [
    [0, 0],
    [1, 1],
  ];
  const current = {
    id: 'current',
    map: {},
    routeRole: 'current',
    strokeWeight: 4,
    path: currentPath,
    data: [currentPath],
  };
  const render = () => {
    compose({
      lines: [current, ...lines],
      stationData: [],
      stopData: [],
      distanceData: [],
      vehicles: [],
    });
    return lines.map(line => line.cachedLayer);
  };
  const initial = render(),
    paths = lines.map(line => line.data);
  const currentLayers = current.cachedLayer;
  markers[0].onSelect();
  const selected = render();
  const mutedCurrent = current.cachedLayer;
  assert.ok(
    mutedCurrent.every(
      part => part.opacity === 0.4 && part.data === currentLayers[0].data,
    ),
  );
  assert.equal(mutedCurrent[1].getWidth, 5);
  assert.equal(mutedCurrent[1].getColor, currentLayers[1].getColor);
  assert.equal(
    Object.hasOwn(current, 'routeMuted'),
    false,
    'selection context never mutates the current route object',
  );
  for (const [index, pair] of selected.entries()) {
    assert.ok(
      pair.every(
        part => part.opacity === (index < 3 ? 1 : 0.4) && part.visible,
      ),
    );
    assert.equal(pair[1].getWidth, index < 3 ? 5 : 3);
    assert.equal(pair[0].data, paths[index]);
    assert.equal(pair[1].data, paths[index]);
    assert.equal(
      pair[1].getColor,
      initial[index][1].getColor,
      'per-load hue and deadhead alpha remain unchanged',
    );
    if (index < 3) {
      assert.notEqual(
        pair,
        initial[index],
        'selection gives the chosen future route a wider stroke',
      );
      assert.equal(
        pair[1].data,
        initial[index][1].data,
        'emphasis must reuse the exact geometry',
      );
    }
  }
  for (let frame = 0; frame < 20; frame++)
    assert.deepEqual(
      render(),
      selected,
      'camera-only reads reuse appearance caches',
    );
  assert.equal(current.cachedLayer, mutedCurrent);
  layer.set(structuredClone(loads));
  markers[1].onSelect();
  assert.deepEqual(
    render(),
    selected,
    'another stop in the same load keeps the same route layers',
  );
  markers[3].onSelect();
  assert.deepEqual(
    render().map(pair => pair[1].opacity),
    [0.4, 0.4, 0.4, 1, 1, 1],
  );
  assert.equal(
    current.cachedLayer,
    mutedCurrent,
    'switching selected next loads keeps the same dimmed current layer',
  );
  assert.deepEqual(
    render().map(pair => pair[1].getWidth),
    [3, 3, 3, 5, 5, 5],
  );
  layer.clearSelection();
  // Clearing the selection restores the upcoming roads to their resting
  // strength: one step behind the road being driven, and no further step
  // for being further off. The colour a road shares with its badges is the
  // only thing saying which load they are, and fading the later loads faded
  // exactly the ones that were hardest to follow.
  assert.deepEqual(
    render().map(pair => pair[1].opacity),
    [0.7, 0.7, 0.7, 0.7, 0.7, 0.7],
  );
  assert.ok(render().every(pair => pair[1].getWidth === 3));
  assert.ok(current.cachedLayer.every(part => part.opacity === 1));
  assert.equal(current.cachedLayer[1].getWidth, 5);
  assert.ok(lines.every((line, index) => line.data === paths[index]));
  const restoredCurrent = current.cachedLayer;
  current.routeMuted = true;
  assert.equal(
    routeLayers(current, GpuLayer, extensions),
    restoredCurrent,
    'a current source flag alone never implies an active next-load selection',
  );
});

test('selected identity survives numbering and same-id geometry updates without extra selection callbacks', () => {
  const { layer, markers, lines, selections, loads } = selectionFixture();
  markers[1].onSelect();
  const originalMarkers = [...markers],
    originalLines = [...lines];
  layer.setStopOffset(2);
  assert.deepEqual(
    markers.map(marker => marker.number),
    ['3', '4', '5', '6', '7', '8'],
  );
  assert.ok(markers.every(marker => marker.label === null));
  assert.deepEqual(markers, originalMarkers);
  assert.deepEqual(lines, originalLines);
  const refreshed = structuredClone(loads);
  refreshed[0].stops[2].longitude = -77;
  refreshed[0].legs[1].points[1].longitude = -77;
  layer.set(refreshed);
  assert.ok(originalMarkers.every(marker => marker.map === null));
  assert.ok(originalLines.every(line => line.map === null));
  assert.ok(
    markers
      .slice(6, 9)
      .every(marker => marker.highlighted && marker.label === null),
  );
  assert.ok(
    lines
      .slice(6, 9)
      .every(
        line =>
          line.strokeWeight === 2 && line.zIndex === 10 && !line.routeMuted,
      ),
  );
  originalMarkers[3].onSelect();
  assert.deepEqual(
    selections,
    [['first', 1]],
    'detached markers cannot select another load',
  );
  assert.ok(markers.slice(6, 9).every(marker => marker.highlighted));
  layer.set(refreshed);
  assert.equal(markers.length, 12);
  assert.equal(lines.length, 12);
});

test('hiding or removing the selected load clears its labels and cannot restore its selection', () => {
  const { layer, markers, lines, selections, loads } = selectionFixture();
  markers[0].onSelect();
  layer.setVisible(false);
  assert.deepEqual(selections.at(-1), [null, 0]);
  assert.ok(
    markers.every(
      marker => !marker.visible && !marker.highlighted && marker.label === null,
    ),
  );
  assert.ok(lines.every(line => !line.visible && line.strokeWeight === 2));
  markers[0].onSelect();
  assert.equal(selections.length, 2, 'hidden markers cannot select a load');
  layer.setVisible(true);
  assert.ok(
    markers.every(
      marker => marker.visible && !marker.highlighted && marker.label === null,
    ),
  );
  markers[0].onSelect();
  layer.set([loads[1]]);
  assert.deepEqual(selections.at(-1), [null, 0]);
  assert.ok(
    markers
      .slice(6)
      .every(marker => !marker.highlighted && marker.label === null),
  );
  layer.set(loads);
  assert.ok(
    markers
      .slice(9)
      .every(marker => !marker.highlighted && marker.label === null),
  );
  markers[9].onSelect();
  const count = selections.length;
  layer.dispose();
  markers[9].onSelect();
  assert.equal(
    selections.length,
    count,
    'disposal and stale clicks do not invoke selection callbacks',
  );
  assert.ok(markers.every(marker => marker.map === null));
});

test('a pending pickup remains selectable without labels or a loaded route', () => {
  const markers = [];
  const lines = [];
  class Line {
    constructor() {
      lines.push(this);
    }
    setPath(points) {
      this.path = points;
    }
    setOptions() {}
    setMap() {}
  }
  class Stop {
    constructor(options) {
      this.label = null;
      Object.assign(this, options);
      markers.push(this);
    }
    setDistance() {
      assert.fail('Pending pickups must not create floating labels');
    }
    setNumber(value) {
      this.number = value;
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop);
  const loads = [
    {
      id: 'pending',
      loadNumber: 1376,
      stops: [],
      legs: [],
      stopCount: 2,
      deadhead: {
        miles: 10,
        points: [
          { latitude: 40, longitude: -80 },
          { latitude: 41, longitude: -79 },
        ],
      },
    },
  ];
  layer.set(loads);
  markers[0].onSelect();
  assert.equal(markers[0].highlighted, true);
  assert.equal(markers[0].job, 'Pickup');
  const path = lines[0].path;
  layer.set(structuredClone(loads));
  assert.equal(markers[0].label, null);
  assert.equal(lines.length, 1);
  assert.equal(lines[0].path, path);
  assert.equal(markers.length, 1);
});

test('click pins all stop circles by load identity without road geometry or labels', () => {
  const markers = [];
  const selections = [];
  class Line {}
  class Stop {
    constructor(options) {
      this.label = null;
      Object.assign(this, options);
      markers.push(this);
    }
    setDistance() {
      assert.fail('Selected future stops must not create labels');
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop, (...args) =>
    selections.push(args),
  );
  layer.set([
    {
      id: 'load-1',
      loadNumber: 12,
      legs: [],
      stops: [
        { latitude: 40, longitude: -80, job: 'Pickup' },
        { latitude: 41, longitude: -79, job: 'Delivery' },
      ],
    },
  ]);
  // Pointing at a load lights it; it never puts a label on the badge.
  assert.ok(markers.every(marker => typeof marker.onHover === 'function'));
  markers[0].onHover(true);
  assert.ok(markers.every(marker => marker.highlighted));
  markers[0].onHover(false);
  assert.ok(markers.every(marker => !marker.highlighted));
  markers[1].onSelect();
  assert.ok(markers.every(marker => marker.highlighted));
  assert.deepEqual(
    markers.map(marker => marker.number),
    ['1', '2'],
  );
  assert.ok(markers.every(marker => marker.label === null));
  assert.deepEqual(selections, [['load-1', 1]]);
  markers[1].onSelect();
  assert.ok(
    markers.every(marker => marker.highlighted && marker.label === null),
  );
  layer.clearSelection();
  assert.ok(
    markers.every(marker => !marker.highlighted && marker.label === null),
  );
  assert.deepEqual(selections.at(-1), [null, 0]);
});

test('reopening uses cached routes and changing selection releases the cache', () => {
  const markers = [];
  const lines = [];
  class Line {
    constructor(options) {
      Object.assign(this, options);
      lines.push(this);
    }
    setPath(points) {
      this.path = points;
    }
    setMap(map) {
      this.map = map;
    }
    setOptions(options) {
      Object.assign(this, options);
    }
  }
  class Stop {
    constructor(options) {
      Object.assign(this, options);
      markers.push(this);
    }
    setDistance(value) {
      this.label = value;
    }
    setVisible(value) {
      this.visible = value;
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop);
  layer.set([
    {
      loadNumber: 12,
      stops: [{ latitude: 40, longitude: -80, job: 'Pickup' }],
      legs: [
        {
          points: [
            { latitude: 40, longitude: -80 },
            { latitude: 41, longitude: -79 },
          ],
        },
      ],
    },
  ]);
  const path = lines[0].path;
  layer.setVisible(false);
  assert.equal(lines[0].visible, false);
  assert.ok(lines[0].map, 'hidden lines remain mounted');
  assert.equal(markers[0].visible, false);
  layer.setVisible(true);
  assert.equal(lines[0].visible, true);
  assert.equal(lines.length, 1);
  assert.equal(lines[0].path, path);
  assert.equal(markers.length, 1, 'reopening keeps the same marker');
  assert.equal(markers[0].visible, true);
  assert.equal(markers[0].number, '1');
  layer.setVisible(false);
  layer.clear();
  layer.setVisible(true);
  assert.equal(markers.length, 1);
  assert.equal(markers[0].map, null);
  layer.dispose();
});

test('pending routes reserve stop numbers before their geometry arrives', () => {
  const markers = [];
  class Line {
    setPath() {}
    setMap() {}
    setOptions() {}
  }
  class Stop {
    constructor(options) {
      Object.assign(this, options);
      markers.push(this);
    }
    setDistance(value) {
      this.label = value;
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop);
  layer.setStopOffset(1);
  layer.set([
    { loadNumber: 12, stopCount: 2, stops: [], legs: [] },
    {
      loadNumber: 13,
      stopCount: 2,
      stops: [{ latitude: 40, longitude: -80, job: 'Pickup' }],
      legs: [],
    },
  ]);
  assert.equal(markers[0].number, '4');
});

test('future numbering continues across loaded and empty legs and the selected load retains pickup and delivery emphasis', () => {
  const markers = [];
  class Line {
    setPath() {}
    setMap() {}
    setOptions(options) {
      Object.assign(this, options);
    }
  }
  class Stop {
    constructor(options) {
      this.label = null;
      Object.assign(this, options);
      markers.push(this);
    }
    setDistance() {
      assert.fail('Mileage belongs only to the bottom popup');
    }
    setNumber(number) {
      this.number = number;
    }
  }
  const loads = [0, 1].map(i => {
    const stops = [
      { latitude: 40 + i, longitude: -80, job: 'Pickup' },
      { latitude: 40 + i, longitude: -79, job: 'Delivery' },
    ];
    return {
      loadNumber: 12 + i,
      stops,
      legs: [{ miles: 100, points: stops }],
      deadhead: { miles: 10, points: stops },
    };
  });
  const layer = createNextLoadsLayer({}, Line, Stop);
  layer.setStopOffset(1);
  layer.set(loads);
  assert.deepEqual(
    markers.map(m => m.number),
    ['2', '3', '4', '5'],
  );
  markers[3].onSelect();
  assert.equal(
    markers[2].highlighted,
    true,
    'pickup is highlighted with its delivery',
  );
  assert.equal(markers[3].highlighted, true);
  assert.equal(markers[0].highlighted, false, 'another load remains unchanged');
  assert.ok(markers.every(marker => marker.label === null));
  layer.setStopOffset(2);
  assert.equal(markers.length, 4);
  assert.deepEqual(
    markers.map(m => m.number),
    ['3', '4', '5', '6'],
  );
  assert.ok(markers.every(marker => marker.label === null));
  layer.clearSelection();
  assert.ok(markers.every(marker => !marker.highlighted));
});

test('saved empty route is drawn before the loaded route is ready and preserves selection when stops arrive', () => {
  const objects = [];
  class Line {
    constructor(options) {
      Object.assign(this, options);
      objects.push(this);
    }
    setPath(path) {
      this.path = path;
    }
    setOptions(options) {
      Object.assign(this, options);
    }
    setMap(map) {
      this.map = map;
    }
  }
  class Stop {
    constructor(options) {
      this.label = null;
      Object.assign(this, options);
      objects.push(this);
    }
    setDistance() {
      assert.fail('Saved routes must not restore floating labels');
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop);
  const load = {
    loadNumber: 12,
    legs: [],
    stops: [],
    deadhead: {
      miles: 160.819,
      points: [
        { latitude: 40, longitude: -80 },
        { latitude: 41, longitude: -79 },
      ],
    },
  };
  layer.set([load]);
  assert.equal(objects[0].routeRole, 'deadhead');
  assert.deepEqual(objects[0].path, [
    { lat: 40, lng: -80 },
    { lat: 41, lng: -79 },
  ]);
  assert.equal(objects[1].label, null);
  objects[1].onSelect();
  assert.equal(objects[0].strokeWeight, 2);
  assert.equal(objects[0].zIndex, 10);
  layer.clearSelection();
  assert.equal(objects[0].strokeWeight, 2);
  assert.equal(objects[0].zIndex, 0);
  objects[1].onSelect();
  assert.equal(objects[1].label, null);
  load.stops = [{ latitude: 41, longitude: -79, job: 'Pick Up' }];
  layer.set([load]);
  assert.equal(objects[3].highlighted, true);
  assert.equal(objects[2].strokeWeight, 2);
  assert.equal(objects[3].label, null);
  layer.set([load]);
  assert.equal(objects.length, 4);
  layer.clear();
  assert.ok(objects.every(object => object.map === null));
});

test('next loads use separate geometry and release it when hidden or replaced', () => {
  const objects = [];
  class Line {
    constructor(options) {
      Object.assign(this, options);
      objects.push(this);
    }
    setPath(path) {
      this.path = path;
    }
    setOptions(options) {
      Object.assign(this, options);
    }
    setMap(map) {
      this.map = map;
      this.released = true;
    }
  }
  class Stop {
    constructor(options) {
      this.label = null;
      Object.assign(this, options);
      objects.push(this);
    }
    setDistance() {
      assert.fail('Future roads must not create floating labels');
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop);
  layer.set([
    {
      loadNumber: 12,
      legs: [
        {
          points: [
            { latitude: 40, longitude: -80 },
            { latitude: 41, longitude: -79 },
          ],
        },
      ],
      stops: [{ latitude: 41, longitude: -79, job: 'Drop Off' }],
    },
  ]);
  assert.equal(objects[0].routeRole, 'future');
  objects[1].onSelect();
  assert.equal(objects[1].label, null);
  assert.equal(objects[1].number, '1');
  assert.deepEqual(objects[0].path, [
    { lat: 40, lng: -80 },
    { lat: 41, lng: -79 },
  ]);
  layer.clear();
  assert.equal(objects[0].released, true);
  assert.ok(objects.every(x => x.map === null));
  layer.set([]);
});

test('coincident stops from different loads stay separately selectable in their own colors', () => {
  const markers = [];
  const selections = [];
  class Line {
    setPath() {}
    setMap() {}
    setOptions(options) {
      Object.assign(this, options);
    }
  }
  class Stop {
    constructor(options) {
      this.label = null;
      Object.assign(this, options);
      markers.push(this);
    }
    setDistance() {
      assert.fail('Coincident future stops must not create labels');
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop, (...args) =>
    selections.push(args),
  );
  const loads = ['first', 'second'].map((id, index) => ({
    id,
    loadNumber: 12,
    legs: [],
    stops: [
      { latitude: 40, longitude: -80, job: 'Pick Up', name: `${id} pickup` },
      {
        latitude: 41 + index,
        longitude: -79,
        job: 'Delivery',
        name: `${id} delivery`,
      },
    ],
  }));
  layer.set(loads);
  assert.equal(markers.length, 4);
  assert.deepEqual(
    markers.map(marker => marker.number),
    ['1', '2', '3', '4'],
  );
  assert.equal(markers[0].color, futureRouteColor(0));
  assert.equal(markers[2].color, futureRouteColor(1));
  assert.equal(markers[0].label, null);
  // Pointing at one of two loads that meet here lights that one alone, and
  // still puts no label on the badge.
  assert.ok(markers.every(marker => typeof marker.onHover === 'function'));
  markers[2].onHover(true);
  assert.equal(markers[0].highlighted, false);
  assert.equal(markers[2].highlighted, true);
  assert.equal(markers[2].label, null);
  markers[2].onHover(false);
  assert.equal(markers[2].highlighted, false);
  markers[0].onSelect();
  assert.equal(markers[0].label, null);
  assert.equal(markers[1].highlighted, true);
  assert.equal(markers[2].highlighted, false);
  assert.deepEqual(selections.at(-1), ['first', 0]);
  markers[2].onSelect();
  assert.equal(markers[0].label, null);
  assert.equal(markers[1].highlighted, false);
  assert.equal(markers[1].label, null);
  assert.equal(markers[2].highlighted, true);
  assert.equal(markers[2].label, null);
  assert.deepEqual(selections.at(-1), ['second', 0]);
  layer.set(loads);
  assert.equal(markers.length, 4);
  assert.ok(markers.slice(2).every(marker => marker.highlighted));
  assert.equal(markers[0].label, null);
  markers[0].onSelect();
  assert.deepEqual(selections.at(-1), ['first', 0]);
  assert.equal(markers[0].label, null);
  layer.clear();
  assert.equal(markers[0].map, null);
  layer.set(loads);
  layer.dispose();
  const count = markers.length;
  layer.dispose();
  layer.set(loads);
  assert.equal(
    markers.length,
    count,
    'disposed next loads must not recreate markers',
  );
  assert.ok(markers.every(marker => marker.map === null));
});

test('each coincident occurrence selects its own stop while load colors and refresh identity stay stable', () => {
  const markers = [],
    selections = [];
  class Line {
    setPath() {}
    setMap() {}
    setOptions(options) {
      Object.assign(this, options);
    }
  }
  class Stop {
    constructor(options) {
      Object.assign(this, options);
      markers.push(this);
    }
    setNumber(number) {
      this.number = number;
    }
  }
  const layer = createNextLoadsLayer({}, Line, Stop, (...args) =>
    selections.push(args),
  );
  const delivery = { latitude: 41, longitude: -79, job: 'Delivery' };
  const loads = [
    {
      id: 'first',
      loadNumber: 12,
      legs: [],
      stops: [
        { latitude: 40, longitude: -80, job: 'Pickup' },
        { ...delivery, id: 'first-delivery' },
        {
          ...delivery,
          latitude: 41.0001,
          id: 'second-delivery',
          job: 'Drop Off',
        },
      ],
    },
    {
      id: 'second',
      loadNumber: 13,
      legs: [],
      stops: [{ ...delivery, id: 'third-delivery' }],
    },
  ];
  layer.set(loads);
  assert.equal(markers.length, 4);
  assert.deepEqual(
    markers.map(marker => marker.number),
    ['1', '2', '3', '4'],
  );
  assert.equal(markers[1].color, futureRouteColor(0));
  assert.equal(markers[2].color, futureRouteColor(0));
  assert.equal(markers[3].color, futureRouteColor(1));
  assert.deepEqual(markers[2].position, { lat: 41.0001, lng: -79 });
  markers[0].onSelect();
  markers[2].onSelect();
  markers[2].onSelect();
  assert.deepEqual(
    selections,
    [
      ['first', 0],
      ['first', 2],
      ['first', 2],
    ],
    'the numbered circle opens that occurrence immediately and never cycles to another one',
  );
  layer.set(structuredClone(loads));
  layer.setStopOffset(1);
  assert.equal(markers.length, 4);
  assert.deepEqual(
    markers.map(marker => marker.number),
    ['2', '3', '4', '5'],
  );
  markers[1].onSelect();
  assert.deepEqual(
    selections.at(-1),
    ['first', 1],
    'the earlier delivery remains independently selectable',
  );

  const updated = structuredClone(loads);
  updated[0].stops[1].name = 'Updated receiving location';
  layer.set(updated);
  assert.equal(markers.length, 8);
  markers[1].onSelect();
  assert.equal(
    selections.length,
    4,
    'detached occurrence callbacks remain inert',
  );
  markers[5].onSelect();
  assert.deepEqual(
    selections.at(-1),
    ['first', 1],
    'same-identity refresh retains the selected occurrence',
  );
  assert.ok(markers.slice(4, 7).every(marker => marker.highlighted));
  assert.equal(markers[7].highlighted, false);
  markers[7].onSelect();
  assert.deepEqual(selections.at(-1), ['second', 0]);
  assert.ok(markers.slice(4, 7).every(marker => !marker.highlighted));
  assert.equal(markers[7].highlighted, true);

  layer.clearSelection();
  assert.deepEqual(selections.at(-1), [null, 0]);
  markers[6].onSelect();
  assert.deepEqual(
    selections.at(-1),
    ['first', 2],
    'dismissal does not change which occurrence a circle opens',
  );
  layer.dispose();
});

// Colour is the only thing saying which road a badge belongs to, and three
// upcoming loads in one corner of the map is a lot to ask of it. Pointing
// at either the badge or the road answers it outright, while the eye is
// already there and without picking anything.
test('pointing at a load lights its road and its badges, and lets go when the cursor does', () => {
  const { layer, markers, lines, selections } = selectionFixture();
  const roads = () => lines.map(line => line.routeSelected === true);
  const badges = () => markers.map(marker => marker.highlighted === true);

  lines[0].onHover({ object: lines[0] });
  assert.deepEqual(roads(), [true, true, true, false, false, false]);
  assert.deepEqual(badges(), [true, true, true, false, false, false]);
  assert.deepEqual(selections, [], 'pointing picks nothing');

  // Arriving is reported before leaving, so the departure of the road just
  // left must not put out the one the cursor is on now.
  lines[3].onHover({ object: lines[3] });
  lines[0].onHover({ object: null });
  assert.deepEqual(roads(), [false, false, false, true, true, true]);

  lines[3].onHover({ object: null });
  assert.deepEqual(roads(), [false, false, false, false, false, false]);
  assert.deepEqual(badges(), [false, false, false, false, false, false]);

  // A load picked on purpose outranks the cursor, and outlives it.
  markers[0].onSelect();
  lines[3].onHover({ object: lines[3] });
  assert.deepEqual(roads(), [true, true, true, false, false, false]);
  lines[3].onHover({ object: null });
  assert.deepEqual(roads(), [true, true, true, false, false, false]);
});

// A next load's badges stand at its own pickup and delivery, which can be a
// day's drive from the truck. Picking its road lit the road and nothing
// else: the circles it was picked for were off the screen, and there was no
// way to ask the map for them.
test('picking a load road selects that load and offers it to be revealed', () => {
  const revealed = [];
  const markers = [],
    lines = [],
    selections = [];
  class Line {
    constructor(options) {
      Object.assign(this, options);
      lines.push(this);
    }
    setPath(path) {
      this.path = path;
    }
    setOptions(options) {
      Object.assign(this, options);
    }
  }
  class Stop {
    constructor(options) {
      Object.assign(this, options);
      markers.push(this);
    }
    setNumber() {}
    setVisible() {}
  }
  const layer = createNextLoadsLayer(
    {},
    Line,
    Stop,
    (...args) => selections.push(args),
    geometry => revealed.push(geometry),
  );
  layer.set([
    {
      id: 'far',
      loadNumber: 1385,
      executionLegId: 'leg',
      stops: [
        { latitude: 35.6, longitude: -80.8, job: 'Pick Up' },
        { latitude: 42.9, longitude: -74.2, job: 'Drop Off' },
      ],
      legs: [
        {
          points: [
            { latitude: 35.6, longitude: -80.8 },
            { latitude: 42.9, longitude: -74.2 },
          ],
        },
      ],
    },
  ]);
  assert.equal(markers.length, 2, 'a circle at each end of the load');

  lines[0].onClick();

  assert.deepEqual(selections, [['far', 0, 'leg']]);
  assert.ok(markers.every(marker => marker.highlighted));
  // Everything the load stands on: its road and the stops at its ends, so
  // the map can decide whether any of it is on the screen already.
  const geometry = revealed.at(-1);
  assert.ok(geometry.length >= 4);
  assert.ok(
    geometry.some(point => point.lat === 35.6 && point.lng === -80.8) &&
      geometry.some(point => point.lat === 42.9 && point.lng === -74.2),
  );

  // A badge picked by hand reveals its load the same way.
  revealed.length = 0;
  markers[1].onSelect();
  assert.equal(selections.length, 2);
  assert.ok(revealed.at(-1).length >= 4);
});
