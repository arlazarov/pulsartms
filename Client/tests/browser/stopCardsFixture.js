import { Deck, OrthographicView } from '@deck.gl/core';
import {
  ScatterplotLayer,
  PathLayer,
  IconLayer,
  TextLayer,
} from '@deck.gl/layers';
import { createScene } from '../../Scripts/fleetMap/rendering/scene.js';
import { PathStyleExtension } from '@deck.gl/extensions';
import { createRouteStops } from '../../Scripts/fleetMap/routes/routeStops.ts';
import { createNextLoadsLayer } from '../../Scripts/fleetMap/routes/nextLoads.js';
import { stopEtaLabels } from '../../Scripts/fleetMap/routes/stopEtaLabels.ts';
import { createStationPopup } from '../../Scripts/fleetMap/stations/stationPopup.ts';
import { createStationLayer } from '../../Scripts/fleetMap/stations/stationLayer.js';
import { fuelRecommendations } from '../../Scripts/fleetMap/stations/fuelRecommendations.ts';
import { createDetailsCard } from '../../Scripts/fleetMap/ui/detailsCard.ts';

const host = document.getElementById('map');
const markers = [];
let overlay;
const recommendedFuelLayers = layers =>
  layers.filter(layer => layer.props.id === 'fuel-recommendation-points');
const fuelVisitNumbers = layers =>
  recommendedFuelLayers(layers).flatMap(layer =>
    layer.props.data.map(row => row.numbers),
  );
const stopCircleLayers = layers =>
  layers.filter(
    layer =>
      layer.props.id.startsWith('route-stop-') &&
      layer.props.id.endsWith('-points'),
  );
class OfflineOverlay {
  constructor(props) {
    this.props = props;
    overlay = this;
  }
  setMap() {
    this.deck = new Deck({
      parent: host,
      views: [new OrthographicView({ id: 'offline' })],
      initialViewState: { target: [0, 0, 0], zoom: 0 },
      controller: false,
      ...this.props,
      style: { ...this.props.style, pointerEvents: 'auto' },
      onAfterRender: () => {
        window.fixtureFrames = (window.fixtureFrames || 0) + 1;
        const layers = this.deck?.props.layers ?? [];
        window.fixtureRenderedFuel = {
          frame: window.fixtureFrames,
          visitNumbers: fuelVisitNumbers(layers),
          loaded: recommendedFuelLayers(layers).every(layer => layer.isLoaded),
        };
        window.fixtureRenderedStops = {
          frame: window.fixtureFrames,
          labels: stopCircleLayers(layers)
            .flatMap(layer => layer.props.data.map(row => row.markerLabel))
            .sort(),
          loaded: stopCircleLayers(layers).every(layer => layer.isLoaded),
        };
      },
    });
  }
  setProps(props) {
    this.props = props;
    this.deck.setProps(props);
  }
  finalize() {
    this.deck.finalize();
  }
}
const map = {
  getDiv: () => host,
  getZoom: () => 10,
  setOptions() {},
  addListener: () => ({ remove() {} }),
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
const truckSpecs = [
  ['11001', 'on', 0],
  ['11002', 'on', 45],
  ['11003', 'on', 90],
  ['11004', 'idle', 0],
  ['11005', 'off', 0],
  ['11006', 'on', 225],
];
const trucks = truckSpecs.map(([unitNumber, engineState, heading], index) => {
  const truck = scene.createTruckMarker(map, () => {});
  truck.update({ unitNumber, engineState });
  truck.render({ longitude: -510 + index * 204, latitude: -110, heading });
  truck.setSelected(unitNumber === '11006');
  return truck;
});
class Marker extends scene.StopMarker {
  constructor(options) {
    super(options);
    markers.push(this);
  }
}
const point = (x, y = 100) => ({ latitude: y, longitude: x });
window.google = {
  maps: { OverlayView: { preventMapHitsAndGesturesFrom() {} } },
};
const popup = createDetailsCard(map, { onClose: () => current.close() });
const current = createRouteStops(map, Marker, popup, () => {});
const currentPlan = {
  id: 'plan',
  dispatchId: '33333333-3333-3333-3333-333333333333',
  truckId: 'truck',
  fromCurrentPosition: true,
  stops: [
    {
      id: 'current-stop',
      job: 'Drop Off',
      name: 'Schaeffler Group USA Inc',
      point: point(-360),
      address: '308 Springhill Farm Rd, building 3, Fort Mill, SC, 29715, US',
      scheduledDate: '2026-09-09',
      scheduledTime: '07:00:00',
      scheduledDate2: '2026-09-09',
      scheduledTime2: '14:00:00',
      commodity: 'STEEL COILS',
      notes:
        'Shipper appointment confirmation number: PU123456. Receiver appointment confirmation number: DL654321. Shipper BOL: 42845601. Service for Load sentinel.',
    },
  ],
  route: { legs: [{ miles: 1000 }] },
  tracking: { nextStopId: 'current-stop' },
};
current.setPlan(currentPlan);
current.setLoadReference({
  dispatchId: currentPlan.dispatchId,
  loadNumber: 1441,
  orderNumber: 'CURRENT-1441',
});
current.setProgress(135);
const future = createNextLoadsLayer(map, scene.Polyline, Marker);
future.setStopOffset(1);
const futureLoads = [
  {
    id: 'future',
    loadNumber: 1442,
    status: 'ready',
    stopCount: 2,
    stops: [
      {
        ...point(0),
        id: 'pickup',
        job: 'Pickup',
        name: 'East logistics terminal',
      },
      {
        ...point(360),
        id: 'delivery',
        job: 'Delivery',
        name: 'Capital distribution centre',
      },
    ],
    legs: [{ miles: 240, points: [point(0), point(360)] }],
    deadhead: { miles: 38, points: [point(-360), point(0)] },
  },
];
future.set(futureLoads);
const forecast = {
  validUntil: new Date(Date.now() + 120000).toISOString(),
  stops: [
    {
      dispatchId: 'current',
      stopId: 'current-stop',
      arrival: '2026-09-09T18:02:00-04:00',
      timeZoneId: 'America/Toronto',
      appointment: '2026-09-09T14:00:00-04:00',
      lateMinutes: 242,
    },
    {
      dispatchId: 'future',
      stopId: 'pickup',
      arrival: '2026-09-09T11:00:00-04:00',
      timeZoneId: 'America/Toronto',
      appointment: '2026-09-09T11:00:00-04:00',
      lateMinutes: 0,
    },
    {
      dispatchId: 'future',
      stopId: 'delivery',
      arrival: '2026-09-10T18:00:00-04:00',
      timeZoneId: 'America/Toronto',
      appointment: '2026-09-10T17:00:00-04:00',
      lateMinutes: 60,
    },
  ],
};
const labels = stopEtaLabels(forecast);
current.setEtas(
  new Map([['current-stop', labels.get('current:current-stop')]]),
);
markers[1].onSelect();
const fuel = createStationPopup();
fuel.update({
  station: {
    name: 'Reference fuel stop',
    address: '100 Example Road, Toronto, ON',
    country: 'CA',
  },
  discount: {
    retailPrice: 1.679,
    discountPrice: 1.459,
    priceAfterIfta: 1.389,
    savings: 0.22,
  },
});
document
  .querySelector('#fuel .fleet-map-details-card__body')
  .append(fuel.element);
const recommendedFuel = createStationLayer(
  map,
  () => current.close(),
  scene.createStationPointLayer,
);
const fuelQuote = {
  currency: 'USD',
  product: 'Diesel',
  unit: 'US gal',
  retailPrice: 4.2,
  discountPrice: 3.8,
  priceAfterIfta: 3.6,
  savings: 0.4,
  effectiveFrom: '2026-09-01',
  effectiveTo: '2026-09-30',
};
await recommendedFuel.setStations(
  [
    { id: 'ordinary-fuel', name: 'Ordinary overlapping fuel stop' },
    { id: 'recommended-fuel', name: 'Recommended fuel stop' },
  ].map(station => ({
    ...station,
    address: '200 Example Road, Albany, NY',
    country: 'US',
    latitude: 0,
    longitude: 120,
    discounts: [fuelQuote],
    cashDiscount: fuelQuote,
    iftaDiscount: fuelQuote,
  })),
  '2026-09-09',
  false,
);
await recommendedFuel.setVisible(true);
window.fixtureFuelVisible = visible => recommendedFuel.setVisible(visible);
const fuelVisits = [
  {
    stationId: 'recommended-fuel',
    number: 1,
    buyGallons: 35,
    arrivalGallons: 40,
    departureGallons: 75,
    unit: 'gal',
    milesAhead: 10,
    currentRouteMile: 20,
  },
  {
    stationId: 'recommended-fuel',
    number: 2,
    buyGallons: 164,
    arrivalGallons: 26,
    departureGallons: 190,
    unit: 'gal',
    fillToTarget: true,
    milesAhead: 927,
  },
];
window.fixtureFuelVisits = count =>
  recommendedFuel.setRecommended(
    fuelRecommendations(
      { tankGallons: 200, fuelPlan: { stops: fuelVisits.slice(0, count) } },
      { progressMiles: 10 },
    ).stops,
  );
await window.fixtureFuelVisits(2);
window.fixtureFuelReport = () => {
  const ring = overlay.props.layers.find(
    layer => layer.props.id === 'fuel-recommendation-rings',
  );
  const badge = overlay.props.layers.find(
    layer => layer.props.id === 'fuel-recommendation-numbers',
  );
  const pointLayers = overlay.props.layers.filter(layer =>
    ['fuel-points', 'fuel-recommendation-points'].includes(layer.props.id),
  );
  return {
    labels: overlay.props.layers
      .filter(
        layer => layer instanceof TextLayer && layer.props.id.includes('fuel'),
      )
      .map(layer => layer.props.id),
    visitNumbers: fuelVisitNumbers(overlay.props.layers),
    badge: badge && {
      text: badge.props.data.map(row => badge.props.getText(row)),
      offset:
        typeof badge.props.getPixelOffset === 'function'
          ? badge.props.getPixelOffset(badge.props.data[0])
          : badge.props.getPixelOffset,
      size: badge.props.getSize,
      padding: badge.props.backgroundPadding,
      radius: badge.props.backgroundBorderRadius,
      background: badge.props.getBackgroundColor,
      fontSize: badge.props.fontSettings.fontSize,
      pickable: badge.props.pickable,
    },
    layerOrder: overlay.props.layers.map(layer => layer.props.id),
    points: pointLayers.map(layer => ({
      id: layer.props.id,
      stations: layer.props.data.map(station => station.id),
    })),
    colors: pointLayers.flatMap(layer =>
      layer.props.data.map(station => ({
        id: station.id,
        color: layer.props.getFillColor(station),
      })),
    ),
    ring: ring && {
      visible: ring.props.visible,
      radius:
        typeof ring.props.getRadius === 'function'
          ? ring.props.getRadius(ring.props.data[0])
          : ring.props.getRadius,
      width: ring.props.getLineWidth,
      pickable: ring.props.pickable,
      positions: ring.props.data.map(row => row.position),
    },
  };
};
window.fixtureFuelPoint = () => {
  const [x, y] = overlay.deck.getViewports()[0].project([120, 0]);
  const rect = host.getBoundingClientRect();
  return { x: rect.x + x, y: rect.y + y };
};
window.fixtureFuelProgress = () => recommendedFuel.setProgress(15);
window.fixtureReport = () =>
  overlay.props.layers
    .filter(
      layer =>
        layer.props.id.startsWith('route-stop-') &&
        layer.props.id.includes('distance'),
    )
    .flatMap(layer =>
      layer.props.data.map(row => ({ text: row.text, position: row.position })),
    );
window.fixtureRoadReport = () =>
  overlay.props.layers
    .filter(layer => /^route-\d+$/.test(layer.props.id))
    .map(layer => ({
      id: layer.props.id,
      width: layer.props.getWidth,
      visible: layer.props.visible,
      color: layer.props.getColor,
    }));
window.fixtureStopMetrics = () =>
  overlay.props.layers
    .filter(layer => layer.props.id.startsWith('route-stop-'))
    .map(layer => ({
      id: layer.props.id,
      radius: layer.props.getRadius,
      border: layer.props.getLineWidth,
      type:
        layer instanceof IconLayer
          ? 'icon'
          : layer instanceof TextLayer
            ? 'text'
            : 'scatterplot',
      numberSize: layer.props.getSize,
      sizeUnits: layer.props.sizeUnits,
      pickable: layer.props.pickable,
      labels:
        layer.props.getText &&
        layer.props.data.map(row => layer.props.getText(row)),
      background: layer.props.background,
      icon: layer.props.iconMapping?.[
        typeof layer.props.getIcon === 'function'
          ? layer.props.getIcon(layer.props.data[0])
          : layer.props.getIcon
      ],
      textColor: layer.props.getColor,
      rows: layer.props.data.map(row => {
        const viewport = overlay.deck.getViewports()[0];
        const [x, y] = viewport.project(row.position);
        const canvas = overlay.deck.canvas.getBoundingClientRect(),
          map = host.getBoundingClientRect();
        const offset =
          typeof layer.props.getPixelOffset === 'function'
            ? layer.props.getPixelOffset(row)
            : [0, 0];
        return {
          label: row.markerLabel,
          job: row.job,
          offset,
          x:
            canvas.left -
            map.left +
            ((x + offset[0]) * canvas.width) / viewport.width,
          y:
            canvas.top -
            map.top +
            ((y + offset[1]) * canvas.height) / viewport.height,
        };
      }),
    }));
window.fixtureStopNumbers = offset => future.setStopOffset(offset);
window.fixtureCircleOnly = enabled => {
  overlay.deck.setProps({
    layerFilter: enabled
      ? ({ layer }) =>
          layer.props.id.startsWith('route-stop-') &&
          !layer.props.id.endsWith('-anchor')
      : null,
  });
};
window.fixtureTruckReport = () => {
  const icons = overlay.props.layers.find(
    layer => layer.props.id === 'truck-icons',
  );
  const numbers = overlay.props.layers.find(
    layer => layer.props.id === 'truck-numbers',
  );
  if (!icons || !numbers) return null;
  return {
    loaded: icons.isLoaded,
    trucks: icons.props.data.map(truck => ({
      unit: truck.unit,
      engine: truck.engine,
      size: icons.props.getSize(truck),
      angle: icons.props.getAngle(truck),
      textureWidth: icons.props.getIcon(truck).width,
      textureHeight: icons.props.getIcon(truck).height,
      labelSize: numbers.props.getSize,
      labelPadding: numbers.props.backgroundPadding,
      labelPhysicalFontSize: numbers.props.fontSettings.fontSize,
      labelBackground: numbers.props.getBackgroundColor(truck),
    })),
  };
};
window.fixtureRefresh = () => {
  current.setProgress(135);
  current.setEtas(
    new Map([['current-stop', labels.get('current:current-stop')]]),
  );
  future.set(structuredClone(futureLoads));
};
window.fixtureHours = mode => {
  const fresh = {
    validUntil: new Date(Date.now() + 120000).toISOString(),
    routeUpdatePending: mode === 'pending',
    cycleAtCalculation: {
      remainingMinutes: 60,
      nextRecapAt: new Date(Date.now() + 86400000).toISOString(),
      nextRecapMinutes: 185,
      homeTimeZoneId: 'America/Toronto',
      recapVerified: true,
    },
    stops: [
      {
        ...forecast.stops[0],
        arrival: '2026-09-09T12:00:00-04:00',
        lateMinutes: mode === 'unknown' ? 60 : 0,
        hours: {
          cycleVerified: mode !== 'unknown',
          cycleAtArrivalMinutes: -480,
          cycleAfterStopMinutes: mode === 'short' ? -480 : -600,
          drivingShortfallMinutes: 480,
          firstCycleShortageAt: '2026-09-09T04:00:00-04:00',
          alternatives:
            mode === 'recap' || mode === 'restart' || mode === 'pending'
              ? [
                  {
                    kind: mode === 'restart' ? 'restart' : 'recap',
                    arrival: '2026-09-09T13:00:00-04:00',
                    departure: '2026-09-09T15:00:00-04:00',
                    cycleAfterStopMinutes: 60,
                    lateMinutes: 0,
                  },
                ]
              : [],
        },
      },
    ],
  };
  const value =
    mode === 'legacy'
      ? labels.get('current:current-stop')
      : stopEtaLabels(fresh).get('current:current-stop');
  current.setEtas(new Map([['current-stop', value]]));
};
window.fixtureToggleFuture = visible => future.setVisible(visible);
window.fixtureSelectFuture = () => markers[1].onSelect();
window.fixtureCurrentJob = job => {
  currentPlan.stops[0].job = job;
  current.setPlan(structuredClone(currentPlan));
};
window.fixtureCurrentAddress = address => {
  currentPlan.stops[0].address = address;
  current.setPlan(structuredClone(currentPlan));
};
window.fixtureFocusCurrent = () =>
  overlay.deck.setProps({ viewState: { target: [-360, 0, 0], zoom: 0 } });
window.fixtureCurrentPoint = () => {
  const [x, y] = overlay.deck.getViewports()[0].project(markers[0].position);
  const rect = host.getBoundingClientRect();
  return { x: rect.x + x, y: rect.y + y };
};
window.fixtureMarkerReport = () =>
  markers.map(marker => ({
    number: marker.number,
    distance: marker.distance,
    highlighted: marker.highlighted,
    job: marker.job,
    position: marker.position,
    clickable: typeof marker.onSelect === 'function',
  }));
window.fixtureDispose = () => {
  trucks.forEach(truck => truck.dispose());
  current.clear();
  popup.dispose();
  future.dispose();
  fuel.dispose();
  recommendedFuel.dispose();
  scene.dispose();
};
