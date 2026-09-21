import { createDockedDetails } from '../../Scripts/fleetMap/ui/dockedDetails.ts';
import { createRouteLayer } from '../../Scripts/fleetMap/routes/routeLayer.ts';
import { createStationLayer } from '../../Scripts/fleetMap/stations/stationLayer.ts';
import { stopEtaLabels } from '../../Scripts/fleetMap/routes/stopEtaLabels.ts';
import { fuelRecommendations } from '../../Scripts/fleetMap/stations/fuelRecommendations.ts';
import { createCameraViewport } from '../../Scripts/fleetMap/ui/cameraViewport.ts';

const host = document.querySelector('.fleet-map-inspector__native');
const shell = host.closest('.fleet-map-inspector');
const title = shell.querySelector('.fleet-map-inspector__title');
const summary = shell.querySelector('.fleet-map-info-content');
const markers = new Map(),
  stationButtons = new Map(),
  mapListeners = new Map();
let repeatedScenario = false;
const events = [],
  edits = [];
const truckId = '11111111-1111-1111-1111-111111111111';
const dispatchId = '22222222-2222-2222-2222-222222222222';
const stationId = '33333333-3333-3333-3333-333333333333';
const stopId = '44444444-4444-4444-4444-444444444444';
const date = '2026-09-10';
const controls = document.getElementById('fixture-markers');
const button = (label, click) => {
  const node = document.createElement('button');
  node.type = 'button';
  node.className = 'btn';
  node.textContent = label;
  node.addEventListener('click', click);
  controls.append(node);
  return node;
};
const inspector = createDockedDetails(
  host,
  (kind, revision) => {
    events.push({ kind, revision, truckId });
    shell.dataset.inspectorMode = kind;
    shell.classList.toggle('has-selection', kind !== 'closed');
    shell.classList.add('is-expanded');
    host.hidden = kind !== 'stop' && kind !== 'fuel';
    summary.hidden = kind !== 'truck';
    title.textContent =
      kind === 'fuel'
        ? 'Fuel station'
        : kind === 'stop'
          ? 'Route stop'
          : 'Truck 54777';
  },
  () => 'truck',
  document.getElementById('fleet-map'),
);
const map = {
  getDiv: () => document.getElementById('fleet-map'),
  getZoom: () => 6,
  addListener(name, callback) {
    mapListeners.set(name, callback);
    return {
      remove() {
        mapListeners.delete(name);
      },
    };
  },
};
const viewport = createCameraViewport(map.getDiv(), map);
window.google = {
  maps: {
    LatLng: class {
      constructor(position) {
        Object.assign(this, position);
      }
    },
  },
};
class Line {
  constructor(options) {
    Object.assign(this, options);
    this.path = [];
  }
  setOptions(options) {
    Object.assign(this, options);
  }
  setPath(path) {
    this.path = path;
  }
  getPath() {
    return {
      setAt: (index, point) => {
        this.path[index] = point;
      },
      removeAt: index => this.path.splice(index, 1),
    };
  }
  setMap(value) {
    this.map = value;
  }
}
class Stop {
  constructor(options) {
    Object.assign(this, options);
    this.node = button(
      repeatedScenario
        ? `Select load stop ${this.number}`
        : 'Select current stop',
      () => this.onSelect?.(),
    );
    markers.set(this, this);
  }
  get map() {
    return this._map;
  }
  set map(value) {
    this._map = value;
    if (this.node) this.node.hidden = !value;
  }
  setNumber(value) {
    this.number = value;
    if (repeatedScenario) this.node.textContent = `Select load stop ${value}`;
  }
  setJob(value) {
    this.job = value;
  }
  setDistance() {}
}
const route = createRouteLayer(
  map,
  () => {},
  () => {
    inspector.activate('stop');
    stations.closePopup();
  },
  Line,
  Stop,
  inspector.popupFactory('stop'),
);
const stations = createStationLayer(
  map,
  () => {
    inspector.activate('fuel');
    route.closePopup();
  },
  (_map, select) => ({
    setPoint(id) {
      if (!stationButtons.has(id))
        stationButtons.set(
          id,
          button('Select fuel station', () => select(id)),
        );
    },
    removePoint(id) {
      stationButtons.get(id)?.remove();
      stationButtons.delete(id);
    },
    setVisible() {},
    redraw() {},
    hitTest() {
      return null;
    },
    dispose() {
      for (const node of stationButtons.values()) node.remove();
      stationButtons.clear();
    },
  }),
  inspector.popupFactory('fuel'),
  selection => edits.push(selection),
);
const plan = {
  id: 'route',
  version: 1,
  truckId,
  dispatchId,
  fromCurrentPosition: true,
  stops: [
    {
      id: stopId,
      job: 'Delivery',
      name: 'Schaeffler Group USA Inc',
      address: '308 Springhill Farm Rd, building 3, Fort Mill, SC 29715, US',
      point: { latitude: 35, longitude: -80 },
      scheduledDate: date,
      scheduledTime: '16:00:00',
      notes: 'Receiver appointment confirmation number: DL654321.',
    },
  ],
  tracking: { nextStopId: stopId },
  route: {
    legs: [
      {
        miles: 100,
        points: [
          { latitude: 36, longitude: -81 },
          { latitude: 35, longitude: -80 },
        ],
      },
    ],
  },
};
route.setPlan(plan, false, { progressMiles: 25 });
route.setLoadReference({
  dispatchId,
  loadNumber: 1375,
  loadLabel: 'AMF1375',
  orderNumber: '567086821',
});
function eta(hour) {
  const labels = stopEtaLabels({
    validUntil: new Date(Date.now() + 120000).toISOString(),
    stops: [
      {
        dispatchId,
        stopId,
        arrival: `${date}T${hour}:00Z`,
        timeZoneId: 'America/New_York',
        appointment: `${date}T20:00:00Z`,
        lateMinutes: 0,
      },
    ],
  });
  route.setEtas(new Map([[stopId, labels.get(`${dispatchId}:${stopId}`)]]));
}
eta('18:30');
const quote = {
  effectiveFrom: date,
  effectiveTo: date,
  currency: 'USD',
  unit: 'US gal',
  product: 'Diesel',
  retailPrice: 6.289,
  discountPrice: 5.613,
  priceAfterIfta: 5.286,
  savings: 0.676,
};
const station = {
  id: stationId,
  name: 'LOVES #706',
  address: '3499 Lee Jackson Hwy, Staunton, VA 24401, USA',
  country: 'US',
  latitude: 38.1,
  longitude: -79.2,
  discounts: [quote],
  cashDiscount: quote,
  iftaDiscount: quote,
};
stations.setEditContext({ truckId, dispatchId });
await stations.setStations([station], date, false);
await stations.setVisible(true);
const recommendations = fuelRecommendations(
  {
    dispatchId,
    tankGallons: 200,
    fuelPlan: {
      stops: [
        {
          stationId,
          dispatchId,
          beforeStopId: stopId,
          number: 1,
          buyGallons: 160,
          arrivalGallons: 40,
          departureGallons: 200,
          purchaseCostUsd: 123.45,
          fillToTarget: true,
          milesAhead: 85,
          unit: 'US gal',
        },
      ],
    },
  },
  null,
).stops;
await stations.setRecommended(recommendations);
function mode(kind, restoreFocus = true) {
  inspector.setMode(kind, restoreFocus);
  route.closePopup();
  stations.closePopup();
}
shell
  .querySelector('.fleet-map-inspector__close')
  .addEventListener('click', () => mode('closed'));
shell
  .querySelector('.fleet-map-inspector__back')
  .addEventListener('click', () => mode('truck'));
button('Blank map', () =>
  mode(
    ['stop', 'fuel', 'next-stop'].includes(inspector.mode) ? 'truck' : 'closed',
    false,
  ),
);
inspector.setMode('truck');
window.nativeInspector = {
  ready: true,
  events,
  edits,
  plan,
  refreshViewport: () => viewport.refresh(),
  async poll() {
    eta('19:15');
    quote.discountPrice = 5.5;
    await stations.setStations([station], date, false);
    await stations.setRecommended(recommendations);
  },
  mode,
  repeatStops() {
    repeatedScenario = true;
    const address = '675 Basket Rd, Webster, NY 14580, US';
    const stops = [
      { address, city: 'Webster', time: '02:00:00' },
      {
        address: '100 Amsterdam Rd, Amsterdam, NY 12010, US',
        city: 'Amsterdam',
        time: '11:00:00',
      },
      { address, city: 'Webster', time: '13:00:00' },
      { address, city: 'Webster', time: '14:00:00' },
      {
        address: '1 Main St, Port St Lucie, FL 34952, US',
        city: 'Port St Lucie',
        time: '05:00:00',
      },
    ].map((value, index) => ({
      id: `88888888-8888-8888-8888-${String(index + 1).padStart(12, '0')}`,
      job: index === 4 ? 'Delivery' : 'Pickup',
      name: value.city,
      address: value.address,
      sequence: index + 1,
      scheduledDate: index === 4 ? '2026-09-12' : '2026-09-11',
      scheduledTime: value.time,
      point: {
        latitude: index === 4 ? 27.3 : index === 1 ? 42.9 : 43.2,
        longitude: index === 4 ? -80.3 : index === 1 ? -74.2 : -77.4,
      },
    }));
    const repeated = {
      ...plan,
      version: 2,
      stops,
      tracking: { nextStopId: stops[0].id },
      route: {
        legs: stops.map((stop, index) => ({
          miles: 100,
          points: [
            index ? stops[index - 1].point : { latitude: 43, longitude: -77 },
            stop.point,
          ],
        })),
      },
    };
    route.setPlan(repeated, false, { progressMiles: 0 });
    route.setLoadReference({
      dispatchId,
      loadNumber: 1383,
      loadLabel: 'AMF1383',
      orderNumber: 'fixture-order',
    });
    return markers.size;
  },
  async dispose() {
    route.dispose();
    stations.dispose();
    inspector.dispose();
    viewport.dispose();
    for (const marker of markers.values()) marker.node.remove();
    return mapListeners.size;
  },
};
