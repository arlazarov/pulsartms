import { Deck, MapView } from '@deck.gl/core';
import {
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
} from '@deck.gl/layers';
import { PathStyleExtension } from '@deck.gl/extensions';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.ts';
import { currentRouteColor } from '../../Scripts/fleetMap/rendering/routePalette.ts';
import { createNextLoadsLayer } from '../../Scripts/fleetMap/routes/nextLoads.ts';
import { createStationLayer } from '../../Scripts/fleetMap/stations/stationLayer.ts';

// A truck's current road and its next loads around Albany and along I-90,
// drawn by the production scene, next-loads layer and station layer on an
// offline map view: the roads, their dashes and dots, the badges and the
// station price colours are the real ones; the basemap is not (there is
// none), so this shows the roads against a plain ground only. Geometry is
// synthetic but dense, as a routed road is, and two loads share the
// Thruway between Utica and Amsterdam exactly, point for point.

const host = document.getElementById('map');
const listeners = new Map();
let overlay;
class OfflineOverlay {
  constructor(props) {
    this.props = props;
    overlay = this;
  }
  setMap() {
    this.deck = new Deck({
      parent: host,
      views: [new MapView({ id: 'offline' })],
      initialViewState: { longitude: -75, latitude: 42.8, zoom: 7 },
      controller: false,
      ...this.props,
      style: { ...this.props.style, pointerEvents: 'auto' },
      onAfterRender: () => {
        window.roadFrames = (window.roadFrames || 0) + 1;
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
let mapZoom = 7;
const map = {
  getDiv: () => host,
  getZoom: () => mapZoom,
  getBounds: () => null,
  setOptions() {},
  addListener(name, callback) {
    listeners.set(name, callback);
    return { remove: () => listeners.delete(name) };
  },
};
const scene = createScene(map, {
  GoogleMapsOverlay: OfflineOverlay,
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
  routeDashExtensions: [
    new PathStyleExtension({ dash: true, highPrecisionDash: true }),
  ],
});

// A road through the given places, sampled about every half kilometre
// with the small bends of a real highway. The same places give the same
// points, so two loads on one stretch share it exactly.
function road(...places) {
  const points = [];
  for (let i = 0; i < places.length - 1; i++) {
    const [a, b] = [places[i], places[i + 1]];
    const km =
      Math.hypot(
        (b[0] - a[0]) * 111 * Math.cos((a[1] * Math.PI) / 180),
        (b[1] - a[1]) * 111,
      ) || 1;
    const steps = Math.max(2, Math.round(km / 0.5));
    for (let s = i === 0 ? 0 : 1; s <= steps; s++) {
      const t = s / steps;
      const bend = Math.sin(t * Math.PI * 3) * 0.01;
      points.push({
        longitude: a[0] + (b[0] - a[0]) * t + bend * (b[1] - a[1]),
        latitude: a[1] + (b[1] - a[1]) * t - bend * (b[0] - a[0]),
      });
    }
  }
  return points;
}
const place = {
  kingston: [-74.0, 41.93],
  rensselaer: [-73.75, 42.64],
  amsterdam: [-74.19, 42.94],
  utica: [-75.23, 43.1],
  syracuse: [-76.15, 43.05],
  binghamton: [-75.91, 42.1],
  albany: [-73.78, 42.65],
};
const thruway = road(place.amsterdam, place.utica);

const corridor = new URLSearchParams(location.search).has('corridor');

// The corridor case, as the owner saw 54777 on September 26: the current
// load runs from Houston up the Appalachian corridor to Rensselaer, and
// both next loads run back down and up that same corridor, so their roads
// lie on the current one for most of their length.
const spine = [
  [-95.37, 29.76],
  [-91.2, 30.45],
  [-88.0, 30.7],
  [-87.2, 33.0],
  [-85.3, 35.0],
  [-81.5, 36.6],
  [-78.8, 38.5],
  [-77.2, 40.2],
  [-75.9, 41.2],
  [-75.9, 42.1],
];
const spineRoad = road(...spine);
const fromBinghamton = spineRoad.findIndex(
  p => p.latitude > 41.2 && p.longitude > -76,
);
const currentPath = corridor
  ? [...spineRoad, ...road([-75.9, 42.1], place.rensselaer).slice(1)]
  : road(place.kingston, [-73.9, 42.3], place.rensselaer);

// The road being driven, to the delivery at Rensselaer.
const current = new scene.Polyline({
  map,
  strokeWeight: 3,
  routeRole: 'current',
  routeColor: currentRouteColor,
  zIndex: 2,
});
current.setPath(currentPath.map(p => ({ lng: p.longitude, lat: p.latitude })));
const currentStops = [
  {
    position: corridor
      ? { lng: spine[0][0], lat: spine[0][1] }
      : { lng: -74.3, lat: 41.6 },
    number: '1',
    job: 'Pickup',
  },
  {
    position: { lng: place.rensselaer[0], lat: place.rensselaer[1] },
    number: '2',
    job: 'Delivery',
  },
].map(
  spec =>
    new scene.StopMarker({
      map,
      ...spec,
      color: currentRouteColor,
    }),
);
currentStops[0].setDone?.(true);

// Two upcoming loads. AMF1413: 53 empty miles to Amsterdam, then the
// Thruway west to Syracuse. AMF1415: empty to Utica, then back east on the
// same Thruway to Albany.
const loads = [
  {
    id: 'load-1413',
    loadNumber: 1413,
    status: 'ready',
    deadhead: {
      miles: 53,
      points: road(place.rensselaer, [-73.95, 42.82], place.amsterdam),
    },
    legs: [
      {
        miles: 130,
        seconds: 8200,
        points: [...thruway, ...road(place.utica, place.syracuse).slice(1)],
      },
    ],
    stops: [
      {
        latitude: place.amsterdam[1],
        longitude: place.amsterdam[0],
        job: 'Pickup',
      },
      {
        latitude: place.syracuse[1],
        longitude: place.syracuse[0],
        job: 'Delivery',
      },
    ],
    stopCount: 2,
  },
  {
    id: 'load-1415',
    loadNumber: 1415,
    status: 'ready',
    deadhead: {
      miles: 58,
      points: road(place.syracuse, place.utica),
    },
    legs: [
      {
        miles: 95,
        seconds: 6000,
        points: [
          ...[...thruway].reverse(),
          ...road(place.amsterdam, place.albany).slice(1),
        ],
      },
    ],
    stops: [
      { latitude: place.utica[1], longitude: place.utica[0], job: 'Pickup' },
      {
        latitude: place.albany[1],
        longitude: place.albany[0],
        job: 'Delivery',
      },
    ],
    stopCount: 2,
  },
];

// Corridor loads: AMF1413 from Syracuse down the corridor to Columbia,
// SC; AMF1415 from Greensboro, NC back up it to Albany.
const columbia = [-81.0, 34.0];
const greensboro = [-79.8, 36.07];
const down = [...spineRoad.slice(0, fromBinghamton + 1)].reverse();
const southOfVirginia = down.findIndex(p => p.latitude < 36.8);
const corridorLoads = [
  {
    id: 'load-1413',
    loadNumber: 1413,
    status: 'ready',
    deadhead: { miles: 145, points: road(place.rensselaer, place.syracuse) },
    legs: [
      {
        miles: 780,
        seconds: 45000,
        points: [
          ...road(place.syracuse, [-75.9, 42.1]),
          ...down.slice(1, southOfVirginia + 1),
          ...road(
            [down[southOfVirginia].longitude, down[southOfVirginia].latitude],
            columbia,
          ).slice(1),
        ],
      },
    ],
    stops: [
      {
        latitude: place.syracuse[1],
        longitude: place.syracuse[0],
        job: 'Pickup',
      },
      { latitude: columbia[1], longitude: columbia[0], job: 'Delivery' },
    ],
    stopCount: 2,
  },
  {
    id: 'load-1415',
    loadNumber: 1415,
    status: 'ready',
    deadhead: { miles: 95, points: road(columbia, greensboro) },
    legs: [
      {
        miles: 620,
        seconds: 36000,
        points: [
          ...road(greensboro, [
            down[southOfVirginia].longitude,
            down[southOfVirginia].latitude,
          ]),
          ...[...down.slice(1, southOfVirginia + 1)].reverse().slice(1),
          ...road([-75.9, 42.1], place.albany).slice(1),
        ],
      },
    ],
    stops: [
      { latitude: greensboro[1], longitude: greensboro[0], job: 'Pickup' },
      {
        latitude: place.albany[1],
        longitude: place.albany[0],
        job: 'Delivery',
      },
    ],
    stopCount: 2,
  },
];

const nextLoads = createNextLoadsLayer(
  map,
  scene.Polyline,
  scene.StopMarker,
  (id, index) => {
    window.selectedNext = id === null ? null : `${id}:${index}`;
  },
);
nextLoads.setStopOffset(2);
nextLoads.set(corridor ? corridorLoads : loads);
nextLoads.setVisible(true);

// The day's stations along the way, priced as the station layer prices
// them (per currency, cheapest green to dearest red); two are planned.
const day = '2026-09-26';
const stationRows = [
  ['s1', -73.97, 42.7, 3.29],
  ['s2', -74.4, 42.92, 3.62],
  ['s3', -74.85, 43.02, 3.89],
  ['s4', -75.4, 43.08, 3.41],
  ['s5', -75.9, 43.07, 3.95],
  ['s6', -76.3, 43.0, 3.55],
  ['s7', -74.05, 42.2, 3.71],
  ['s8', -73.85, 41.95, 3.99],
  ['s9', -75.1, 42.6, 3.47],
  ['s10', -74.6, 42.4, null],
].map(([id, longitude, latitude, price]) => ({
  id: `00000000-0000-4000-8000-${String(id.slice(1)).padStart(12, '0')}`,
  name: `Station ${id}`,
  latitude,
  longitude,
  cashDiscount:
    price === null
      ? null
      : {
          currency: 'USD',
          unit: 'US gal',
          discountPrice: price,
          retailPrice: price + 0.5,
          savings: 0.5,
          effectiveFrom: day,
          effectiveTo: day,
        },
  discounts: [],
}));
const stationLayer = createStationLayer(
  map,
  () => {},
  scene.createStationPointLayer,
  () => ({ show() {}, hide() {}, dispose() {} }),
);
window.roadFixture = {
  async setStations(visible) {
    await stationLayer.setStations(stationRows, day, false);
    await stationLayer.setRecommended([
      { id: stationRows[1].id, number: 1, point: stationRows[1] },
      { id: stationRows[3].id, number: 2, point: stationRows[3] },
    ]);
    await stationLayer.setVisible(visible);
  },
  view(longitude, latitude, zoom) {
    mapZoom = zoom;
    overlay.deck.setProps({
      initialViewState: { longitude, latitude, zoom },
    });
    listeners.get('zoom_changed')?.();
    listeners.get('idle')?.();
  },
  select(loadId, stopIndex) {
    nextLoads.selectStop(loadId, stopIndex);
  },
  clear() {
    nextLoads.clearSelection();
  },
};
window.roadFixture.nextLoads = nextLoads;
window.roadFixture.scene = scene;
