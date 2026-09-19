import test from 'node:test';
import assert from 'node:assert/strict';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.js';
import { currentRouteColor } from '../../Scripts/fleetMap/rendering/routePalette.js';
import { stopMarkerIcon } from '../../Scripts/fleetMap/rendering/stopAppearance.js';

test('scene reuses static layers across motion, invalidates only changed stops and cleans up', t => {
  let now = 0;
  t.mock.method(performance, 'now', () => now);
  let frame,
    reads = 0;
  const originalRaf = globalThis.requestAnimationFrame,
    originalCancel = globalThis.cancelAnimationFrame;
  globalThis.requestAnimationFrame = callback => {
    frame = callback;
    return 1;
  };
  globalThis.cancelAnimationFrame = () => {
    frame = null;
  };
  t.after(() => {
    globalThis.requestAnimationFrame = originalRaf;
    globalThis.cancelAnimationFrame = originalCancel;
  });
  const observers = [],
    overlays = [],
    listeners = new Map();
  class Observer {
    constructor(callback) {
      this.callback = callback;
      observers.push(this);
    }
    observe() {}
    disconnect() {
      this.disconnected = true;
    }
  }
  const originalObserver = globalThis.MutationObserver;
  globalThis.MutationObserver = Observer;
  t.after(() => {
    globalThis.MutationObserver = originalObserver;
  });
  class Overlay {
    constructor(props) {
      this.props = props;
      this.updates = 0;
      overlays.push(this);
    }
    setMap() {}
    setProps(props) {
      this.props = props;
      this.updates++;
    }
    finalize() {
      this.finalized = true;
    }
  }
  class Layer {
    constructor(props) {
      this.props = props;
      this.id = props.id;
    }
  }
  const clusterEvents = [];
  const map = {
    moveCamera(options) {
      clusterEvents.push(options);
    },
    getDiv: () => ({ dataset: {}, clientWidth: 1000, clientHeight: 700 }),
    setOptions() {},
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
  assert.equal(
    overlays[0].props.useDevicePixels,
    true,
    'Retina density is handled by the renderer',
  );
  const touchClick = overlays[0].props.onClick;
  const flush = () => {
    const callback = frame;
    frame = null;
    callback?.();
  };
  const layers = () =>
    Object.fromEntries(
      overlays[0].props.layers.map(layer => [layer.id, layer]),
    );
  const route = new scene.Polyline({
    map,
    strokeWeight: 4,
    strokeColor: '#2e50e7',
  });
  route.setPath([
    { lng: -80, lat: 35 },
    { lng: -79, lat: 36 },
  ]);
  let selectedStation = null;
  const stations = scene.createStationPointLayer(map, id => {
    selectedStation = id;
  });
  stations.setPoint(
    'fuel',
    { lng: -80, lat: 35 },
    'rgb(0,128,0)',
    false,
    false,
  );
  stations.setVisible(true);
  let distance = '72 mi · 116 km',
    clicked = false;
  const hoverEvents = [];
  const stop = new scene.StopMarker({
    position: { lng: -80, lat: 35 },
    number: '1',
    onHover: value => hoverEvents.push(value),
  });
  stop.onSelect = () => {
    clicked = true;
  };
  stop.setDistance(distance);
  const truck = scene.createTruckMarker(map, () => {});
  truck.update({ unitNumber: '11006', engineState: 'On' });
  truck.render({ longitude: -80, latitude: 35, heading: 90, speed: 45 });
  flush();
  const initial = layers(),
    initialReads = reads;
  now += 200;
  assert.equal(scene.consumeTruckClick(), false);
  assert.equal(initial[route.id].props.pickable, true);
  initial[route.id].props.onClick({ object: route.path });
  assert.equal(
    scene.consumeTruckClick(),
    true,
    'the displayed road is not an empty map click',
  );
  now += 200;
  assert.equal(scene.consumeTruckClick(), false);
  initial[`${route.id}-outline`].props.onClick({ object: route.path });
  assert.equal(
    scene.consumeTruckClick(),
    true,
    'the route outline protects the inspector too',
  );
  scene.setClusterSelect(() => clusterEvents.push('stop-follow'));
  const neighbor = scene.createTruckMarker(map, () => {});
  neighbor.update({ unitNumber: '11007' });
  neighbor.render({ longitude: -80, latitude: 35 });
  map.getZoom = () => 5;
  listeners.get('zoom_changed')();
  flush();
  for (const id of ['truck-clusters', 'truck-cluster-anchors']) {
    now += 200;
    assert.equal(scene.consumeTruckClick(), false);
    clusterEvents.length = 0;
    const layer = layers()[id];
    layer.props.onClick({ object: layer.props.data[0] });
    assert.equal(clusterEvents[0], 'stop-follow');
    assert.ok(Math.abs(clusterEvents[1].center.lat - 35) < 1e-9);
    assert.ok(Math.abs(clusterEvents[1].center.lng + 80) < 1e-9);
    assert.equal(clusterEvents[1].zoom, 18);
    assert.equal(
      scene.consumeTruckClick(),
      true,
      'group labels and anchors must not dismiss the inspector',
    );
  }
  neighbor.setVisible(false);
  map.getZoom = () => 12;
  listeners.get('zoom_changed')();
  flush();
  assert.equal(initial['fuel-points'].props.getRadius, 8);
  assert.equal(initial['route-stop-1-points'].props.getSize, 34);
  assert.equal(initial['route-stop-1-numbers'].props.getSize, 15);
  assert.equal(initial['route-stop-distances'].props.getSize, 14);
  assert.equal(initial['truck-numbers'].props.getSize, 13);
  assert.deepEqual(initial['truck-numbers'].props.backgroundPadding, [9, 4]);
  assert.equal(initial['truck-icons'].props.getSize({ unit: '11006' }), 28);
  assert.deepEqual(
    initial['truck-numbers'].props.getPixelOffset(
      initial['truck-numbers'].props.data[0],
    ),
    [0, -30],
  );
  assert.deepEqual(
    initial['route-stop-distances'].props.getPixelOffset(
      initial['route-stop-distances'].props.data[0],
    ),
    [0, -34],
  );
  for (const [id, fontSize] of [
    ['route-stop-distances', 14],
    ['route-stop-distances-content', 14],
    ['route-stop-1-numbers', 15],
    ['truck-numbers', 13],
  ]) {
    assert.deepEqual(initial[id].props.fontSettings, { sdf: false, fontSize });
    const sampler =
      initial[id].props._subLayerProps.characters.textureParameters;
    assert.equal(sampler.mipmapFilter, 'nearest');
    assert.equal(sampler.minFilter, 'linear');
    assert.equal(sampler.magFilter, 'linear');
  }
  assert.equal(
    initial['route-stop-1-points'].props.iconAtlas,
    stopMarkerIcon(currentRouteColor).url,
  );
  assert.equal(initial['route-stop-1-numbers'].props.background, false);
  assert.equal(initial['route-stop-distances'].props.fontWeight, 400);
  assert.equal(initial['route-stop-distances'].props.getTextAnchor, 'start');
  assert.equal(
    initial['route-stop-distances'].props.getAlignmentBaseline,
    'bottom',
  );
  assert.equal(
    initial['route-stop-distances'].props._subLayerProps.characters.visible,
    false,
  );
  assert.equal(initial['route-stop-distances-content'].props.fontWeight, 400);
  let touchQueries = 0;
  overlays[0].pickObject = () => {
    touchQueries++;
    return { object: { id: 'fuel' } };
  };
  const touchEvent = { srcEvent: { domEvent: { pointerType: 'touch' } } };
  touchClick({ x: 20, y: 30 }, touchEvent);
  assert.equal(selectedStation, 'fuel');
  assert.equal(
    scene.consumeTruckClick(),
    true,
    'touch fallback suppresses map background click',
  );
  stations.setVisible(false);
  touchClick({ x: 20, y: 30 }, touchEvent);
  assert.equal(touchQueries, 1, 'hidden stations cannot be selected');
  stations.setVisible(true);
  assert.equal(
    overlays.length,
    1,
    'fleet layers share a single ordered canvas',
  );
  const order = Object.keys(initial);
  assert.ok(
    order.indexOf(`${route.id}-outline`) < order.indexOf('fuel-points'),
  );
  assert.ok(order.indexOf(route.id) < order.indexOf('fuel-points'));
  assert.ok(
    order.indexOf('fuel-points') < order.indexOf('route-stop-1-points'),
  );
  assert.ok(order.indexOf('fuel-points') < order.indexOf('truck-icons'));
  assert.ok(order.indexOf(route.id) < order.indexOf('route-stop-1-points'));
  assert.ok(order.indexOf(route.id) < order.indexOf('truck-icons'));
  for (let i = 1; i <= 120; i++) {
    truck.render({
      longitude: -80 + i / 10000,
      latitude: 35,
      heading: 90,
      speed: 45,
    });
    flush();
    for (const id of [
      route.id,
      'fuel-points',
      'route-stop-1-points',
      'route-stop-1-numbers',
      'route-stop-distances',
      'route-stop-distances-content',
    ]) {
      assert.equal(layers()[id], initial[id], id);
    }
  }
  assert.equal(reads, initialReads, 'motion never queries stop DOM');
  assert.equal(
    listeners.has('zoom_changed'),
    true,
    'integer zoom changes update truck clustering',
  );
  assert.equal(initial['route-stop-1-numbers'].props.pickable, true);
  initial['route-stop-1-points'].props.onClick({
    object: initial['route-stop-1-points'].props.data[0],
  });
  assert.equal(clicked, true);
  clicked = false;
  initial['route-stop-1-numbers'].props.onClick({
    object: initial['route-stop-1-numbers'].props.data[0],
  });
  assert.equal(
    clicked,
    true,
    'the complete badge opens the same stop as its geographic anchor',
  );
  const movingTruckLayer = layers()['truck-icons'];
  assert.equal(
    movingTruckLayer.props.getIcon(movingTruckLayer.props.data[0]),
    initial['truck-icons'].props.getIcon(initial['truck-icons'].props.data[0]),
    'motion reuses the cached status icon',
  );
  assert.match(
    decodeURIComponent(
      movingTruckLayer.props.getIcon(movingTruckLayer.props.data[0]).url,
    ),
    /<path[^>]*fill="#16a34a"/,
  );
  assert.match(
    decodeURIComponent(
      movingTruckLayer.props.getIcon({ engine: 'on', speed: 0 }).url,
    ),
    /<circle[^>]*fill="#16a34a"/,
  );
  assert.match(
    decodeURIComponent(
      movingTruckLayer.props.getIcon({ engine: 'off', speed: 0 }).url,
    ),
    /<circle[^>]*fill="#64748b"/,
  );
  movingTruckLayer.props.onHover({ object: movingTruckLayer.props.data[0] });
  flush();
  assert.equal(
    layers()['truck-icons'].props.getSize({ unit: '11006' }),
    28 * 1.1,
  );
  assert.equal(layers()['truck-numbers'].props.getSize, 13);
  assert.deepEqual(layers()['truck-numbers'].props.backgroundPadding, [9, 4]);
  layers()['truck-icons'].props.onHover({ object: null });
  flush();
  const unhoveredTruckLayer = layers()['truck-icons'];
  distance = '71 mi · 114 km';
  stop.setDistance(distance);
  flush();
  assert.equal(
    layers()['truck-icons'],
    unhoveredTruckLayer,
    'distance updates do not rebuild trucks',
  );
  assert.equal(layers()['route-stop-1-points'], initial['route-stop-1-points']);
  assert.equal(
    layers()['route-stop-1-numbers'],
    initial['route-stop-1-numbers'],
  );
  assert.notEqual(
    layers()['route-stop-distances'],
    initial['route-stop-distances'],
  );
  assert.notEqual(
    layers()['route-stop-distances-content'],
    initial['route-stop-distances-content'],
  );
  assert.equal(layers()['route-stop-distances'].props.data[0].text, distance);
  truck.setSelected(true);
  flush();
  assert.deepEqual(
    layers()['truck-numbers'].props.getBackgroundColor(
      layers()['truck-numbers'].props.data[0],
    ),
    [49, 94, 234],
  );
  stations.setPoint(
    'fuel',
    { lng: -80, lat: 35 },
    'rgb(200,128,0)',
    true,
    true,
  );
  stations.redraw();
  flush();
  assert.equal(layers()['fuel-points'], undefined);
  assert.deepEqual(
    layers()['fuel-recommendation-points'].props.data[0].color,
    [200, 128, 0],
  );
  stop.setDistance(null);
  flush();
  assert.equal(layers()['route-stop-distances'], undefined);
  assert.equal(layers()['route-stop-distances-content'], undefined);
  stop.highlighted = true;
  flush();
  const highlightedOrder = Object.keys(layers());
  assert.ok(
    highlightedOrder.indexOf('route-stop-1-numbers') >
      highlightedOrder.indexOf('route-stop-1-points'),
  );
  assert.equal(
    layers()['route-stop-1-points'],
    initial['route-stop-1-points'],
    'highlight preserves the stable pair',
  );
  stop.highlighted = false;
  flush();
  layers()['route-stop-1-points'].props.onHover({
    object: layers()['route-stop-1-points'].props.data[0],
  });
  stop.map = null;
  flush();
  assert.deepEqual(hoverEvents, [true, false]);
  assert.equal(stop.onHover, null);
  assert.equal(stop.onSelect, null);
  assert.equal(layers()['route-stop-1-points'], undefined);
  new scene.StopMarker({
    position: { lng: -80, lat: 35 },
    number: '3',
    transientLabel: true,
  });
  new scene.StopMarker({
    position: { lng: -80.0001, lat: 35 },
    number: '4',
    transientLabel: true,
  });
  flush();
  const paired = Object.keys(layers()).filter(id =>
    /^route-stop-\d+-/.test(id),
  );
  assert.deepEqual(paired, [
    'route-stop-2-anchor',
    'route-stop-2-points',
    'route-stop-2-numbers',
    'route-stop-3-anchor',
    'route-stop-3-points',
    'route-stop-3-numbers',
  ]);
  const pairedLayers = paired.map(id => layers()[id]);
  listeners.get('idle')?.();
  flush();
  assert.deepEqual(
    paired.map(id => layers()[id]),
    pairedLayers,
    'camera movement reuses marker pairs',
  );
  stations.setPoint(
    'fuel',
    { lng: -80, lat: 35 },
    'rgb(200,128,0)',
    true,
    false,
    '1',
    true,
  );
  stations.setVisible(false);
  stations.redraw();
  flush();
  assert.equal(layers()['fuel-recommendation-points'], undefined);
  const editedPoint = layers()['fuel-editing-points'].props.data[0];
  assert.equal(editedPoint.editing, true);
  assert.deepEqual(
    editedPoint.color,
    [200, 128, 0],
    'editing retains the same fuel-price color',
  );
  overlays[0].pickObject = () => ({ object: editedPoint });
  selectedStation = null;
  touchClick({ x: 20, y: 30 }, touchEvent);
  assert.equal(
    selectedStation,
    'fuel',
    'the single editing exception remains selectable while ordinary stations are hidden',
  );
  stations.setPoint(
    'fuel',
    { lng: -80, lat: 35 },
    'rgb(200,128,0)',
    true,
    false,
    '1',
    false,
  );
  stations.redraw();
  flush();
  assert.equal(layers()['fuel-editing-points'], undefined);
  assert.equal(layers()['fuel-editing-label'], undefined);
  const plannedPoint = layers()['fuel-recommendation-points'].props.data[0];
  overlays[0].pickObject = () => ({ object: plannedPoint });
  selectedStation = null;
  touchClick({ x: 20, y: 30 }, touchEvent);
  assert.equal(
    selectedStation,
    'fuel',
    'planned fuel remains selectable with ordinary stations hidden',
  );
  for (let attempt = 0; attempt < 3; attempt++) {
    const savedRoad = layers()[route.id];
    scene.setRouteEditing(true);
    flush();
    assert.equal(layers()[route.id], undefined);
    scene.setRouteEditing(false);
    flush();
    assert.notEqual(
      layers()[route.id],
      savedRoad,
      'Cancel must not reuse a finalized Deck layer',
    );
    assert.equal(
      layers()[route.id].props.data,
      savedRoad.props.data,
      'Cancel restores the same saved geometry without recalculation',
    );
  }
  truck.render({ longitude: -79, latitude: 35 });
  let routeSelections = 0;
  const preview = new scene.Polyline({
    map,
    routeRole: 'preview',
    strokeWeight: 3,
    onClick: () => routeSelections++,
  });
  preview.setPath([
    { lng: -80, lat: 35 },
    { lng: -79, lat: 36 },
  ]);
  scene.setRouteEditing(true);
  flush();
  now += 200;
  layers()[preview.id].props.onClick({ object: preview.path });
  assert.equal(routeSelections, 1);
  assert.equal(
    scene.consumeTruckClick(),
    false,
    'route editing retains its map click for adding via points',
  );
  scene.dispose();
  assert.equal(frame, null);
  assert.ok(overlays.every(overlay => overlay.finalized));
  assert.ok(observers.every(observer => observer.disconnected));
  assert.equal(listeners.size, 0);
});
