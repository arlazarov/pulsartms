import { Deck, MapView } from '@deck.gl/core';
import {
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
} from '@deck.gl/layers';
import { PathStyleExtension } from '@deck.gl/extensions';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.ts';
import {
  currentRouteColor,
  futureRouteColor,
} from '../../Scripts/fleetMap/rendering/routePalette.ts';
import { createRouteLayer } from '../../Scripts/fleetMap/routes/routeLayer.ts';

const host = document.getElementById('map');
const listeners = new Map();
let overlay;
window.markerClicks = [];
class OfflineOverlay {
  constructor(props) {
    this.props = props;
    overlay = this;
  }
  setMap() {
    this.deck = new Deck({
      parent: host,
      views: [new MapView({ id: 'offline' })],
      initialViewState: { longitude: -80, latitude: 36.5, zoom: 6 },
      controller: false,
      ...this.props,
      style: { ...this.props.style, pointerEvents: 'auto' },
      onAfterRender: () => {
        window.markerFrames = (window.markerFrames || 0) + 1;
      },
    });
  }
  setProps(props) {
    this.props = props;
    this.deck.setProps(props);
  }
  pickObject(query) {
    return this.deck.pickObject(query);
  }
  finalize() {
    this.deck.finalize();
  }
}
let mapZoom = 6;
const map = {
  getDiv: () => host,
  getZoom: () => mapZoom,
  setOptions() {},
  moveCamera({ center, zoom }) {
    mapZoom = zoom;
    overlay.deck.setProps({
      initialViewState: { longitude: center.lng, latitude: center.lat, zoom },
    });
    listeners.get('zoom_changed')?.();
    window.clusterCamera = { center, zoom };
  },
  addListener(name, callback) {
    listeners.set(name, callback);
    return {
      remove() {
        listeners.delete(name);
      },
    };
  },
};
const routeDashExtensions = [
  new PathStyleExtension({ dash: true, highPrecisionDash: true }),
];
const scene = createScene(map, {
  GoogleMapsOverlay: OfflineOverlay,
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
  routeDashExtensions,
});
const current = new scene.Polyline({
  map,
  strokeWeight: 3,
  routeRole: 'current',
  routeColor: currentRouteColor,
});
current.setPath([
  { lng: -83, lat: 34.5 },
  { lng: -81, lat: 36 },
  { lng: -78.5, lat: 37.5 },
]);
const future = new scene.Polyline({
  map,
  strokeWeight: 3,
  routeRole: 'future',
  routeColor: futureRouteColor(0),
});
future.setPath([
  { lng: -78.5, lat: 37.5 },
  { lng: -77.5, lat: 38.5 },
  { lng: -80, lat: 39 },
]);
const truckSpecs = [
  {
    unitNumber: '54777',
    engineState: 'on',
    longitude: -81.2,
    latitude: 35.9,
    heading: 35,
    speed: 45,
  },
  {
    unitNumber: '11005',
    engineState: 'off',
    longitude: -83.4,
    latitude: 37.4,
    heading: 0,
    speed: 0,
  },
  {
    unitNumber: '11006',
    engineState: 'idle',
    longitude: -76.8,
    latitude: 35.5,
    heading: 90,
    speed: 0,
  },
  {
    unitNumber: '11007',
    engineState: 'on',
    longitude: -81.7,
    latitude: 38.2,
    heading: 180,
    speed: 0,
  },
];
const trucks = truckSpecs.map(spec => {
  const truck = scene.createTruckMarker(map, () =>
    window.markerClicks.push(spec.unitNumber),
  );
  truck.update(spec);
  truck.render(spec);
  truck.setSelected(spec.unitNumber === '54777');
  return truck;
});
const stops = [
  {
    position: { lng: -83, lat: 34.5 },
    number: '1',
    job: 'Delivery',
    color: currentRouteColor,
  },
  {
    position: { lng: -78.5, lat: 37.5 },
    number: '2',
    job: 'Pickup',
    color: futureRouteColor(0),
  },
  {
    position: { lng: -80, lat: 39 },
    number: '12',
    job: 'Delivery',
    color: futureRouteColor(0),
  },
].map(
  spec =>
    new scene.StopMarker({
      ...spec,
      onSelect: () => window.markerClicks.push(`stop-${spec.number}`),
    }),
);
const stationSpecs = [
  ['green', -84, 35.4, 'rgb(21,128,61)', 5.119],
  ['amber', -79.6, 34.7, 'rgb(245,158,11)', 5.613],
  ['red', -76.5, 37.8, 'rgb(185,28,28)', 6.289],
  ['planned', -79.1, 36.4, 'rgb(21,128,61)', 5.286],
  ['crowded', -79.11, 36.4, 'rgb(245,158,11)', 5.913],
  ['unavailable', -83, 38.8, 'rgb(128,144,165)', null],
];
const stations = scene.createStationPointLayer(map, id =>
  window.markerClicks.push(id),
);
for (const [id, lng, lat, color, price] of stationSpecs)
  stations.setPoint(
    id,
    { lng, lat },
    color,
    id === 'planned',
    false,
    id === 'planned' ? '1' : '',
    false,
    price,
  );
stations.setVisible(true);
const screenPoint = (position, offsetX = 0, offsetY = 0) => {
  const projected = overlay.deck.getViewports()[0].project(position);
  const x = projected[0] + offsetX,
    y = projected[1] + offsetY;
  const rect = host.getBoundingClientRect();
  return { x: rect.x + x, y: rect.y + y, mapX: x, mapY: y };
};
window.markerReport = () => {
  const layers = overlay.props.layers;
  const get = id => layers.find(layer => layer.props.id === id)?.props;
  const icons = layers.find(layer => layer.props.id === 'truck-icons');
  const price = get('fuel-price-labels');
  return {
    loaded:
      !!icons?.isLoaded &&
      layers
        .filter(
          layer => layer instanceof IconLayer || layer instanceof TextLayer,
        )
        .every(layer => layer.isLoaded),
    trucks: icons?.props.data.map(truck => ({
      unit: truck.unit,
      selected: truck.selected,
      ...screenPoint(truck.position),
      size: icons.props.getSize(truck),
      svg: decodeURIComponent(icons.props.getIcon(truck).url),
      angle: icons.props.getAngle(truck),
      label: screenPoint(truck.position, ...truck.labelOffset),
      labelOffset: truck.labelOffset,
      position: truck.position,
    })),
    stops: layers
      .filter(layer => /^route-stop-\d+-points$/.test(layer.props.id))
      .flatMap(layer =>
        layer.props.data.map(stop => ({
          number: stop.number,
          size: layer.props.getSize,
          color: stop.color,
          ...screenPoint(stop.position, stop.markerOffsetX, stop.markerOffsetY),
        })),
      ),
    stations: ['fuel-points', 'fuel-recommendation-points'].flatMap(
      id =>
        get(id)?.data.map(row => ({
          id: row.id,
          color: get(id).getFillColor(row),
          radius:
            typeof get(id).getRadius === 'function'
              ? get(id).getRadius(row)
              : get(id).getRadius,
          ...screenPoint(row.position),
        })) ?? [],
    ),
    prices:
      price?.data.map(row => ({
        id: row.id,
        text: price.getText(row),
        fontSize: price.fontSettings.fontSize,
        color: row.color,
        ...screenPoint(row.position),
      })) ?? [],
    roads: [current, future].map(line => ({
      role: line.routeRole,
      opacity: get(line.id)?.opacity,
      width: get(line.id)?.getWidth,
      originalGeometry: get(line.id)?.data === line.data,
    })),
    clusters:
      get('truck-clusters')?.data.map(group => {
        const point = screenPoint(group.position);
        return {
          count: group.count,
          anchor: point,
          position: group.position,
          hasAnchor:
            get('truck-cluster-anchors')?.data.includes(group) === true,
          x: point.x + group.pixelOffset[0],
          y: point.y + group.pixelOffset[1],
        };
      }) ?? [],
    layerIds: layers.map(layer => layer.props.id),
  };
};
window.showTruckClusters = () => {
  trucks[1].render({ longitude: -80, latitude: 36.5 });
  trucks[2].render({ longitude: -80.01, latitude: 36.5 });
};
window.showNearbyTrucks = () => {
  for (const stop of stops) stop.map = null;
  stops.length = 0;
  for (const truck of trucks) truck.setSelected(false);
  trucks[0].update({ unitNumber: '54777', engineState: 'off' });
  trucks[0].render({ longitude: -80, latitude: 36.5 });
  trucks[1].render({ longitude: -79.99995, latitude: 36.50003 });
  map.moveCamera({ center: { lng: -80, lat: 36.5 }, zoom: 17 });
};
window.restoreTruckOverview = () => {
  trucks.forEach((truck, index) => truck.render(truckSpecs[index]));
  map.moveCamera({ center: { lng: -80, lat: 36.5 }, zoom: 6 });
};
window.selectNextRoute = selected =>
  future.setOptions({ routeSelected: selected, zIndex: selected ? 10 : 0 });
window.showCoincidentStops = () => {
  for (const stop of stops) stop.map = null;
  stops.length = 0;
  for (const number of ['1', '3', '4'])
    stops.push(
      new scene.StopMarker({
        position: { lng: -80, lat: 36.5 },
        number,
        job: 'Pickup',
        color: currentRouteColor,
        onSelect: () => window.markerClicks.push(`visit-${number}`),
      }),
    );
};
let routeProbe, selectionProbe;
window.showRouteProbe = role => {
  current.setMap(null);
  future.setMap(null);
  stations.setVisible(false);
  // Planned markers survive the layer toggle; remove them for isolated road pixel sampling.
  for (const [id] of stationSpecs) stations.removePoint(id);
  stations.redraw();
  for (const truck of trucks) truck.setVisible(false);
  for (const stop of stops) stop.map = null;
  if (!routeProbe) {
    routeProbe = new scene.Polyline({ map, strokeWeight: 2 });
    routeProbe.setPath(
      Array.from({ length: 161 }, (_, index) => ({
        lng: -85 + index / 16,
        lat: 36.5,
      })),
    );
  }
  if (role === 'current-muted') {
    if (!selectionProbe) {
      selectionProbe = new scene.Polyline({
        map,
        strokeWeight: 2,
        routeRole: 'future',
        routeSelected: true,
      });
      selectionProbe.setPath([
        { lng: -100, lat: 0 },
        { lng: -99, lat: 0 },
      ]);
    }
    selectionProbe.setMap(map);
  } else selectionProbe?.setMap(null);
  routeProbe.setOptions({
    routeRole: role.startsWith('current') ? 'current' : 'future',
    routeColor: futureRouteColor(0),
    routeMuted: role === 'future-muted',
  });
};
window.routeProbeReport = () => {
  const layers = overlay.props.layers.filter(
    layer =>
      layer.props.id === routeProbe?.id ||
      layer.props.id === `${routeProbe?.id}-outline`,
  );
  const line = layers.find(layer => layer.props.id === routeProbe?.id)?.props;
  return line
    ? {
        loaded: layers.every(layer => layer.isLoaded),
        role:
          line.opacity < 1
            ? `${routeProbe.routeRole}-muted`
            : routeProbe.routeRole,
        pointCount: line.data[0].length,
        start: screenPoint(line.data[0][0]),
        end: screenPoint(line.data[0].at(-1)),
        color: line.getColor,
        outlineColor: layers.find(layer => layer.props.id.endsWith('-outline'))
          ?.props.getColor,
        width: line.getWidth,
        opacity: line.opacity,
        extensions: line.extensions?.map(extension => extension.opts),
        originalGeometry: layers.every(
          layer => layer.props.data === routeProbe.data,
        ),
      }
    : null;
};
let savedRouteLayer;
window.showSavedRouteProbe = progress => {
  routeProbe?.setMap(null);
  selectionProbe?.setMap(null);
  if (!savedRouteLayer) {
    window.google = {
      maps: {
        LatLng: class {
          constructor(point) {
            Object.assign(this, point);
          }
        },
      },
    };
    window.savedRouteNotifications = [];
    savedRouteLayer = createRouteLayer(
      map,
      (...values) => window.savedRouteNotifications.push(values),
      () => {},
      scene.Polyline,
      scene.StopMarker,
      () => ({ hide() {}, show() {}, dispose() {} }),
    );
    savedRouteLayer.setPlan(
      {
        id: 'saved',
        truckId: '54777',
        dispatchId: 'load',
        version: 1,
        stops: [],
        tracking: {},
        route: {
          legs: [
            {
              miles: 100,
              points: [
                { latitude: 36.5, longitude: -85 },
                { latitude: 36.5, longitude: -80 },
                { latitude: 36.5, longitude: -75 },
              ],
            },
          ],
        },
      },
      false,
      progress,
    );
  } else savedRouteLayer.setProgress(progress);
};
window.savedRouteProbeReport = () => ({
  paths: overlay.props.layers
    .filter(layer => layer.props.id.endsWith('-outline'))
    .flatMap(layer => layer.props.data),
  notifications: window.savedRouteNotifications,
});
window.setRouteProbeEditing = value => scene.setRouteEditing(value);
window.disposeMarkerFixture = () => {
  savedRouteLayer?.dispose();
  for (const truck of trucks) truck.dispose();
  for (const stop of stops) stop.map = null;
  stations.dispose();
  scene.dispose();
  return listeners.size;
};
