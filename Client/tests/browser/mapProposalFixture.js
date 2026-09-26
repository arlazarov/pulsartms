import { loadGoogleMaps } from '../../Scripts/fleetMap/provider/googleMapsLoader.ts';
import { createGpuScene } from '../../Scripts/fleetMap/rendering/gpuScene.ts';
import { currentRouteColor } from '../../Scripts/fleetMap/rendering/routePalette.ts';
import { createNextLoadsLayer } from '../../Scripts/fleetMap/routes/nextLoads.ts';
import { createStationLayer } from '../../Scripts/fleetMap/stations/stationLayer.ts';

// The owner's September 26 map as the product draws it, on the provider's
// own basemap: truck 54777 up from Houston to Rensselaer, its next two
// loads back down the same corridor and up I-95, two trucks standing at
// Albany, the day's stations and the planned fuel. The scene, roads, badges,
// trucks and stations are the production modules on a real vector map. The
// roads are straight runs between the towns the interstates pass through,
// not routed geometry: close enough to sit on the basemap's highways at an
// overview, and honest about it closer in.

const key = window.mapsKey;
delete window.mapsKey;
await loadGoogleMaps(key);
const host = document.getElementById('map');
const map = new google.maps.Map(host, {
  center: { lat: 37, lng: -84 },
  zoom: 5,
  mapId: 'DEMO_MAP_ID',
  mapTypeId: 'roadmap',
  colorScheme: 'LIGHT',
  clickableIcons: false,
  gestureHandling: 'greedy',
  renderingType: google.maps.RenderingType.VECTOR,
  isFractionalZoomEnabled: true,
  tilt: 0,
  heading: 0,
  disableDefaultUI: true,
});
const scene = createGpuScene(map);

// A road through the given places, sampled about every two kilometres.
function road(...places) {
  const points = [];
  for (let i = 0; i < places.length - 1; i++) {
    const [a, b] = [places[i], places[i + 1]];
    const km =
      Math.hypot(
        (b[0] - a[0]) * 111 * Math.cos((a[1] * Math.PI) / 180),
        (b[1] - a[1]) * 111,
      ) || 1;
    const steps = Math.max(2, Math.round(km / 2));
    for (let s = i === 0 ? 0 : 1; s <= steps; s++) {
      const t = s / steps;
      points.push({
        longitude: a[0] + (b[0] - a[0]) * t,
        latitude: a[1] + (b[1] - a[1]) * t,
      });
    }
  }
  return points;
}
const latLng = points =>
  points.map(p => ({ lng: p.longitude, lat: p.latitude }));

const town = {
  houston: [-95.37, 29.76],
  batonRouge: [-91.19, 30.45],
  mobile: [-88.04, 30.69],
  montgomery: [-86.3, 32.37],
  birmingham: [-86.8, 33.52],
  chattanooga: [-85.31, 35.05],
  knoxville: [-83.92, 35.96],
  bristol: [-82.19, 36.6],
  roanoke: [-79.94, 37.27],
  harrisonburg: [-78.87, 38.45],
  hagerstown: [-77.72, 39.64],
  harrisburg: [-76.88, 40.27],
  scranton: [-75.66, 41.41],
  milford: [-74.8, 41.32],
  newburgh: [-74.02, 41.5],
  kingston: [-74.0, 41.93],
  rensselaer: [-73.75, 42.64],
  albany: [-73.78, 42.66],
  schenectady: [-73.94, 42.81],
  amsterdam: [-74.19, 42.94],
  latham: [-73.76, 42.75],
  wytheville: [-81.08, 36.95],
  charlotte: [-80.84, 35.23],
  columbia: [-81.03, 34.0],
  greensboro: [-79.79, 36.07],
  richmond: [-77.44, 37.54],
  washington: [-77.04, 38.9],
  baltimore: [-76.61, 39.29],
  philadelphia: [-75.17, 39.95],
  newark: [-74.17, 40.73],
};
const t = name => town[name];

// The road being driven: Houston to Rensselaer by I-10, I-65, I-59, I-75
// and I-81, then I-84 and I-87. The truck is between Montgomery and
// Birmingham; what is behind it is drawn as travelled.
const truckAt = [-86.55, 32.95];
const behind = road(
  t('houston'),
  t('batonRouge'),
  t('mobile'),
  t('montgomery'),
  truckAt,
);
const ahead = road(
  truckAt,
  t('birmingham'),
  t('chattanooga'),
  t('knoxville'),
  t('bristol'),
  t('roanoke'),
  t('harrisonburg'),
  t('hagerstown'),
  t('harrisburg'),
  t('scranton'),
  t('milford'),
  t('newburgh'),
  t('kingston'),
  t('rensselaer'),
);
const traveled = new scene.Polyline({
  map,
  strokeWeight: 3,
  routeRole: 'traveled',
  routeColor: currentRouteColor,
  zIndex: 1,
});
traveled.setPath(latLng(behind));
const current = new scene.Polyline({
  map,
  strokeWeight: 3,
  routeRole: 'current',
  routeColor: currentRouteColor,
  zIndex: 2,
});
current.setPath(latLng(ahead));
const stops = [
  { position: { lng: -95.37, lat: 29.76 }, number: '1', job: 'Pickup' },
  { position: { lng: -73.75, lat: 42.64 }, number: '2', job: 'Delivery' },
].map(spec => new scene.StopMarker({ map, ...spec, color: currentRouteColor }));
stops[0].setDone?.(true);

// AMF1413: empty to Amsterdam, then down I-87, I-81 and I-77 to Columbia.
// AMF1415: empty to Greensboro, then I-85 and I-95 up to Latham.
const loads = [
  {
    id: 'load-1413',
    loadNumber: 1413,
    status: 'ready',
    deadhead: {
      miles: 32,
      points: road(t('rensselaer'), t('schenectady'), t('amsterdam')),
    },
    legs: [
      {
        miles: 860,
        seconds: 50000,
        points: road(
          t('amsterdam'),
          t('schenectady'),
          t('albany'),
          t('kingston'),
          t('newburgh'),
          t('milford'),
          t('scranton'),
          t('harrisburg'),
          t('hagerstown'),
          t('harrisonburg'),
          t('roanoke'),
          t('wytheville'),
          t('charlotte'),
          t('columbia'),
        ),
      },
    ],
    stops: [
      { latitude: 42.94, longitude: -74.19, job: 'Pickup' },
      { latitude: 34.0, longitude: -81.03, job: 'Delivery' },
    ],
    stopCount: 2,
  },
  {
    id: 'load-1415',
    loadNumber: 1415,
    status: 'ready',
    deadhead: {
      miles: 170,
      points: road(t('columbia'), t('charlotte'), t('greensboro')),
    },
    legs: [
      {
        miles: 640,
        seconds: 38000,
        points: road(
          t('greensboro'),
          t('richmond'),
          t('washington'),
          t('baltimore'),
          t('philadelphia'),
          t('newark'),
          t('newburgh'),
          t('kingston'),
          t('albany'),
          t('latham'),
        ),
      },
    ],
    stops: [
      { latitude: 36.07, longitude: -79.79, job: 'Pickup' },
      { latitude: 42.75, longitude: -73.76, job: 'Delivery' },
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
nextLoads.set(loads);
nextLoads.setVisible(true);

const trucks = [
  ['54777', 'on', truckAt, 32, 62, true],
  ['11005', 'off', [-88.2, 30.65], 0, 0],
  ['11006', 'idle', [-75.3, 42.35], 40, 0],
  ['11010', 'on', [-73.78, 42.68], 0, 0],
  ['11011', 'off', [-73.95, 42.82], 0, 0],
].map(
  ([
    unitNumber,
    engineState,
    [longitude, latitude],
    heading,
    speed,
    selected,
  ]) => {
    const truck = scene.createTruckMarker(map, () => {});
    truck.update({ unitNumber, engineState });
    truck.render({ latitude, longitude, heading, speed });
    truck.setSelected(selected === true);
    return truck;
  },
);

// The day's stations along the way, priced as the station layer prices
// them; two are the planned fuel stops.
const day = '2026-09-26';
const stationRows = [
  ['s1', -86.8, 33.45, 3.99],
  ['s2', -85.25, 35.0, 3.47],
  ['s3', -79.95, 37.22, 3.29],
  ['s4', -81.1, 36.9, 3.62],
  ['s5', -76.9, 40.24, 3.41],
  ['s6', -75.7, 41.38, 3.89],
  ['s7', -74.0, 41.9, 3.95],
  ['s8', -73.94, 42.78, 3.55],
  ['s9', -74.2, 42.97, 3.71],
  ['s10', -80.85, 35.18, null],
  // And the ones the plan passes nowhere near.
  ['s11', -84.5, 39.1, 3.35],
  ['s12', -82.99, 39.96, 3.58],
  ['s13', -85.76, 38.25, 3.44],
  ['s14', -86.78, 36.16, 3.91],
  ['s15', -84.39, 33.75, 3.39],
  ['s16', -81.66, 30.33, 3.52],
  ['s17', -80.19, 25.77, 3.99],
  ['s18', -77.03, 38.9, 3.66],
  ['s19', -78.64, 35.78, 3.48],
  ['s20', -76.6, 39.3, 3.72],
  ['s21', -75.16, 39.95, 3.81],
  ['s22', -74.0, 40.71, 4.05],
  ['s23', -72.68, 41.76, 3.93],
  ['s24', -76.15, 43.05, 3.4],
  ['s25', -78.88, 42.89, 3.61],
  ['s26', -77.61, 43.16, 3.55],
  ['s27', -73.94, 41.7, 3.77],
  ['s28', -73.2, 44.48, 3.69],
  ['s29', -87.63, 41.88, 3.5],
  ['s30', -90.2, 38.63, 3.31],
  ['s31', -92.29, 34.75, 3.29],
  ['s32', -90.05, 35.15, 3.42],
  ['s33', -89.4, 43.07, 3.57],
  ['s34', -86.16, 39.77, 3.46],
  ['s35', -81.38, 28.54, 3.63],
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

// An unchanged camera may not report idle again.
const idle = () =>
  new Promise(resolve => {
    google.maps.event.addListenerOnce(map, 'idle', () => resolve());
    setTimeout(resolve, 4000);
  });
window.proposal = {
  async setStations(visible) {
    await stationLayer.setStations(stationRows, day, false);
    await stationLayer.setRecommended([
      { id: stationRows[4].id, number: 1, point: stationRows[4] },
      { id: stationRows[2].id, number: 2, point: stationRows[2] },
    ]);
    await stationLayer.setVisible(visible);
  },
  async view(lng, lat, zoom) {
    const settled = idle();
    map.moveCamera({ center: { lat, lng }, zoom });
    await settled;
  },
  select(loadId, stopIndex) {
    nextLoads.selectStop(loadId, stopIndex);
  },
  clear() {
    nextLoads.clearSelection();
  },
  renderer: () => host.dataset.renderer,
};
window.proposal.trucks = trucks;
window.proposal.stops = stops;
window.proposal.roads = { traveled, current };
