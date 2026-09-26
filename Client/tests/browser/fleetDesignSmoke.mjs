// The Fleet and fuel cards of the September 26 design, drawn by the
// compiled Client: the truck card closed and open, a route stop, the fuel
// plan, the station list, a planned and an ordinary station. The page, its
// styles and the production docked inspector, stop cards and station
// layer are real; the provider map, GPU markers and the API are not (see
// fleetDesignMapFixture.js). Synthetic data only; nothing is written.
//
// MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot \
//   node tests/browser/fleetDesignSmoke.mjs
// FLEET_DESIGN_CASE=1440-light narrows the matrix to one case.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'MAP_TEST_ARTIFACT_DIR must identify staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput(
  'fleet-design',
  process.env.FLEET_DESIGN_OUTPUT_DIR,
);
const origin = 'http://localhost:5079';
const uuid = n => `33333333-3333-4333-8333-${String(n).padStart(12, '0')}`;
const success = response => ({ success: true, response, errors: [] });
const now = Date.now();
const iso = minutes => new Date(now + minutes * 60000).toISOString();
const today = new Date(now).toISOString().slice(0, 10);
const userId = uuid(1);

const mapStub = (
  await build({
    entryPoints: [resolve('tests/browser/fleetDesignMapFixture.js')],
    bundle: true,
    write: false,
    format: 'esm',
    platform: 'browser',
  })
).outputFiles[0].text;
const stubIntegrity = `sha256-${createHash('sha256').update(mapStub).digest('base64')}`;
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g,
  (_all, open, json, close) => {
    const map = JSON.parse(json);
    for (const name of Object.keys(map.integrity ?? {}))
      if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(name))
        map.integrity[name] = stubIntegrity;
    return open + JSON.stringify(map) + close;
  },
);

// Stations: two the plan buys at, one it passes.
const stations = {
  loves504: {
    id: uuid(501),
    name: 'LOVES #504',
    address: "1000 Love's Drive, Dandridge, TN 37725, US",
    city: 'Dandridge',
    region: 'TN',
    point: { latitude: 36.02, longitude: -83.41 },
    price: 3.7,
  },
  loves432: {
    id: uuid(502),
    name: 'LOVES #432',
    address: '4501 Highway 61, Sikeston, MO 63801, US',
    city: 'Sikeston',
    region: 'MO',
    point: { latitude: 36.88, longitude: -89.58 },
    price: 3.4,
  },
  pilot218: {
    id: uuid(503),
    name: 'PILOT #218',
    address: '6711 Highway 51, Covington, TN 38019, US',
    city: 'Covington',
    region: 'TN',
    point: { latitude: 35.56, longitude: -89.65 },
    price: 3.62,
  },
};

// The trucks and what each one is for.
const trucks = [
  {
    key: 'us',
    id: uuid(11),
    unit: '54777',
    driver: 'Iurii Lisnik',
    trailer: 'GG1030',
    dispatch: uuid(21),
    load: 1409,
    order: '568011987',
    speed: 0,
    fuel: 77,
    engine: 'Off',
    location: '113 Will Garrett Road, Toomsuba, MS 39364',
    point: { latitude: 32.43, longitude: -88.47 },
    country: 'US',
    hos: {
      breakMs: 28800000,
      driveMs: 1560000,
      shiftMs: 1560000,
      cycleMs: 69999999,
    },
    duty: {
      status: 'sleeperBerth',
      statusMinutes: 444,
      restMinutes: 444,
      reset: 34,
    },
    cycleShort: true,
    fuelPlan: true,
  },
  {
    key: 'ca',
    id: uuid(12),
    unit: '11006',
    driver: 'Maksims Ostanins',
    trailer: '9P1571',
    dispatch: uuid(22),
    load: 1395,
    order: '568011988',
    speed: 58,
    fuel: 12,
    engine: 'On',
    location:
      'Unit 14, 2250 Upper Middle Road East, Burlington Industrial Park, Burlington, ON L7P 3W3, Canada',
    point: { latitude: 43.39, longitude: -79.78 },
    country: 'CA',
    hos: {
      breakMs: 3000000,
      driveMs: 25200000,
      shiftMs: 30000000,
      cycleMs: 205200000,
    },
    duty: { status: 'offDuty', statusMinutes: 60, restMinutes: 300, reset: 36 },
    fuelPlan: true,
    belowReserve: true,
    longStop: true,
  },
  {
    key: 'unknown',
    id: uuid(13),
    unit: '61200',
    driver: 'Blair Fixture',
    trailer: '',
    dispatch: uuid(23),
    load: 1410,
    order: '',
    speed: 0,
    fuel: null,
    engine: '',
    location: '',
    point: { latitude: 39.2, longitude: -79.1 },
    country: 'US',
    hos: null,
    duty: null,
    noEta: true,
  },
  {
    key: 'low',
    id: uuid(14),
    unit: '11007',
    // Still on the way to its pickup: the card says what is left of the
    // load, which a truck at its last stop does not.
    toPickup: true,
    driver: 'Casey Fixture',
    trailer: '53R118',
    dispatch: uuid(24),
    load: 1414,
    order: '568011990',
    speed: 67,
    fuel: 25,
    engine: 'On',
    location: 'I-90 W, Erie, PA 16510',
    point: { latitude: 42.1, longitude: -80.0 },
    country: 'US',
    hos: {
      breakMs: 1800000,
      driveMs: 3000000,
      shiftMs: 18000000,
      cycleMs: 36000000,
    },
    duty: {
      status: 'driving',
      statusMinutes: 135,
      restMinutes: null,
      reset: 34,
    },
    fuelPlan: false,
  },
];
const byId = new Map(
  trucks.flatMap(t => [
    [t.id, t],
    [t.dispatch, t],
  ]),
);

function stopsFor(truck) {
  const delivery = truck.longStop
    ? {
        name: 'Target Distribution Center #3802 — Inbound Receiving Door 14',
        address:
          '1730 New York State Route 5S, Building C, Amsterdam, NY 12010, US',
      }
    : {
        name: 'Callanan Industries',
        address: '290 Riverside Ave, Rensselaer, NY 12144, US',
      };
  return [
    {
      id: uuid(truck.load * 10 + 1),
      name: 'Hanes Distribution',
      address: '100 Industrial Blvd, Meridian, MS 39301, US',
      sequence: 1,
      point: { latitude: 32.36, longitude: -88.7 },
      job: 'Pickup',
      scheduledDate: today,
      scheduledTime: '06:00:00',
      appointmentTimeZoneId: 'America/Chicago',
      notes: 'PU # 7781204',
    },
    {
      id: uuid(truck.load * 10 + 2),
      ...delivery,
      sequence: 2,
      point: { latitude: 42.64, longitude: -73.75 },
      job: 'Delivery',
      scheduledDate: new Date(now + 2 * 86400000).toISOString().slice(0, 10),
      scheduledTime: '08:00:00',
      // A long stop books a window over two days: the head says it as two
      // lines.
      ...(truck.longStop
        ? {
            scheduledDate2: new Date(now + 3 * 86400000)
              .toISOString()
              .slice(0, 10),
            scheduledTime2: '15:00:00',
          }
        : { scheduledTime2: '13:00:00' }),
      appointmentTimeZoneId: 'America/New_York',
      notes: 'DEL # 5510932',
    },
  ];
}

function fuelStop(truck, station, number, ahead, arrival, buy) {
  const cost = Math.round(buy * station.price * 100) / 100;
  return {
    number,
    visitKey: `${station.id}:${number}`,
    dispatchId: truck.dispatch,
    beforeStopId: uuid(truck.load * 10 + 2),
    stationId: station.id,
    name: station.name,
    address: station.address,
    point: station.point,
    milesAhead: ahead,
    routeMilesAhead: ahead,
    arrivalGallons: arrival,
    buyGallons: buy,
    purchaseCostUsd: cost,
    departureGallons: arrival + buy,
    fillToTarget: false,
    yourPrice: station.price,
    economicPrice: station.price,
    cashUsdPerGallon: station.price,
    economicUsdPerGallon: station.price,
    currency: 'USD',
    unit: 'US gal',
    detourMiles: number === 1 ? 2 : 1,
    detourMinutes: 4,
    priceDate: today,
    estimatedArrival: iso(ahead),
    priceEstimated: false,
    warning:
      truck.belowReserve && number === 1
        ? 'Below reserve: estimated arrival 18.0 US gal; 12.0 US gal below the configured reserve.'
        : '',
    sent: null,
  };
}

function planning(truck) {
  const stops = stopsFor(truck);
  const fuelStops = truck.fuelPlan
    ? [
        fuelStop(
          truck,
          stations.loves504,
          1,
          420,
          truck.belowReserve ? 18 : 130,
          25,
        ),
        fuelStop(truck, stations.loves432, 2, 680, 113, 80),
      ]
    : [];
  const eta = truck.noEta
    ? {
        calculatedAt: iso(0),
        validUntil: iso(10),
        stops: [],
        unavailableReason: 'ETA unavailable: HOS clocks are not available.',
        assumptions: [],
      }
    : {
        calculatedAt: iso(0),
        validUntil: iso(30),
        // The forecast starts at the stop the truck is heading for.
        stops: stops.slice(1).map((stop, n, rest, i = n + 1) => ({
          stopId: stop.id,
          dispatchId: truck.dispatch,
          arrival: iso(i === 0 ? -600 : 1500),
          timeZoneId: stop.appointmentTimeZoneId,
          appointment: iso(i === 0 ? -620 : 2400),
          lateMinutes: 0,
          drivingMinutes: 900,
          restMinutes: 600,
          hours: {
            cycleAtArrivalMinutes: truck.cycleShort && i === 1 ? -40 : 3420,
            cycleAfterStopMinutes: truck.cycleShort && i === 1 ? -100 : 3360,
            drivingShortfallMinutes: null,
            firstCycleShortageAt:
              truck.cycleShort && i === 1 ? iso(1200) : null,
            cycleVerified: true,
            alternatives: [],
            unavailableReason: null,
          },
        })),
        unavailableReason: null,
        assumptions: [],
        dutyStatus: truck.duty && {
          status: truck.duty.status,
          statusStartedAt: iso(-truck.duty.statusMinutes),
          restStartedAt:
            truck.duty.restMinutes === null
              ? null
              : iso(-truck.duty.restMinutes),
          observedAt: iso(0),
          cycleResetHours:
            truck.duty.restMinutes === null ? null : truck.duty.reset,
          cycleResetCountry:
            truck.duty.restMinutes === null ? null : truck.country,
          cycleResetRemainingMinutes:
            truck.duty.restMinutes === null
              ? null
              : Math.max(0, truck.duty.reset * 60 - truck.duty.restMinutes),
          jurisdiction: truck.country,
        },
      };
  const plan = {
    id: uuid(truck.load * 10 + 5),
    dispatchId: truck.dispatch,
    executionLegId: null,
    assignmentRevision: 1,
    truckId: truck.id,
    version: 1,
    calculatedAt: iso(-5),
    originalPlannedMiles: 1400,
    fromCurrentPosition: true,
    profile: { tankGallons: 250 },
    stops,
    tracking: {
      nextStopId: truck.toPickup ? stops[0].id : stops[1].id,
      nextStopLabel: truck.toPickup ? 'Pickup' : 'Delivery',
      passedStopIds: truck.toPickup ? [] : [stops[0].id],
      visitedStops: {},
      allStopsPassed: false,
    },
    route: {
      miles: 1400,
      seconds: 90000,
      warnings: [],
      points: [],
      legs: [
        { miles: 174, seconds: 12000, points: [truck.point, stops[0].point] },
        {
          miles: 1226,
          seconds: 78000,
          points: [stops[0].point, stops[1].point],
        },
      ],
    },
    fuelPlan: truck.fuelPlan
      ? {
          truckId: truck.id,
          calculatedAt: iso(-5),
          pricingDate: today,
          manuallyEdited: false,
          dispatchIds: [truck.dispatch],
          needsRefresh: false,
          startingGallons: Math.round((truck.fuel ?? 0) * 2.5),
          purchaseGallons: fuelStops.reduce((s, x) => s + x.buyGallons, 0),
          purchaseCostUsd: fuelStops.reduce((s, x) => s + x.purchaseCostUsd, 0),
          arrivalGallons: 160,
          remainingMiles: 1226,
          stops: fuelStops,
          stopArrivals: [
            {
              dispatchId: truck.dispatch,
              stopId: stops[1].id,
              gallons: 160,
              percent: 64,
            },
          ],
          notes: [],
        }
      : null,
  };
  return {
    truckId: truck.id,
    dispatchId: truck.dispatch,
    loadNumber: truck.load,
    message: null,
    executionLegId: null,
    assignmentRevision: 1,
    hos: truck.hos && {
      ...truck.hos,
      updatedAt: iso(0),
      currentDutyStatus: truck.duty?.status,
    },
    state: {
      profile: { tankGallons: 250, reserveGallons: 30 },
      apiConfigured: true,
      fuelPercent: truck.fuel,
      fuelUpdatedAt: iso(-2),
      progress: {
        progressMiles: 174,
        remainingMiles: 1226,
        remainingSeconds: 78000,
        position: truck.point,
        offRoute: false,
        locationStale: false,
      },
      eta,
      plan,
      fuelStopArrivals: plan.fuelPlan?.stopArrivals ?? [],
    },
  };
}

// The editor's draft of the same plan: its stops as edits, the road they
// sit on as one segment before the delivery, and the plan as its values.
function fuelPreview(truck) {
  const stops = stopsFor(truck);
  const plan = planning(truck).state.plan.fuelPlan;
  return {
    plan,
    stops: (plan?.stops ?? []).map(stop => ({
      stationId: stop.stationId,
      beforeStopId: stop.beforeStopId,
      buyGallons: stop.buyGallons,
      fillToTarget: stop.fillToTarget,
      purchaseLimitGallons: 180,
    })),
    expectedCalculatedAt: plan?.calculatedAt ?? null,
    tankGallons: 250,
    fillLimitGallons: 240,
    errors: [],
    valuesAvailable: true,
    segments: [
      {
        beforeStopId: stops[1].id,
        afterStop: stops[0],
        beforeStop: stops[1],
        dispatchId: truck.dispatch,
      },
    ],
    quantityChoices: null,
  };
}

function dispatch(truck) {
  return {
    id: truck.dispatch,
    truckId: truck.id,
    loadNumber: truck.load,
    orderNumber: truck.order,
    status: 'in_transit',
    customerName: 'Fixture Customer',
    truckNumber: truck.unit,
    trailerNumber: truck.trailer,
    driverName: truck.driver,
    stops: stopsFor(truck).map(stop => ({
      id: stop.id,
      sequence: stop.sequence,
      type: stop.job,
      name: stop.name,
      address: stop.address,
      scheduledDate: stop.scheduledDate,
      scheduledTime: stop.scheduledTime,
      scheduledTime2: stop.scheduledTime2 ?? null,
      reference: stop.notes,
    })),
  };
}

// The truck's next load, as the next-loads read returns it, and its
// details as the load read does.
const nextRoute = {
  id: uuid(31),
  loadNumber: 1395,
  status: 'ready',
  legs: [
    {
      miles: 150,
      seconds: 9000,
      points: [
        { latitude: 42.94, longitude: -74.19 },
        { latitude: 43.05, longitude: -76.15 },
      ],
    },
  ],
  stops: [
    {
      id: uuid(311),
      latitude: 42.94,
      longitude: -74.19,
      job: 'Pickup',
      name: 'Hudson Valley Foods',
    },
    {
      id: uuid(312),
      latitude: 42.95,
      longitude: -74.19,
      job: 'Delivery',
      name: 'Target DC #3802',
    },
  ],
  deadhead: {
    miles: 103,
    points: [
      { latitude: 42.64, longitude: -73.75 },
      { latitude: 42.94, longitude: -74.19 },
    ],
  },
  stopCount: 2,
};
const nextDispatch = {
  id: nextRoute.id,
  loadNumber: 1395,
  orderNumber: '568011987',
  status: 'assigned',
  customerName: 'Fixture Customer',
  truckNumber: '11006',
  trailerNumber: '9P1571',
  driverName: 'Maksims Ostanins',
  stops: nextRoute.stops.map((stop, i) => ({
    id: stop.id,
    sequence: i + 1,
    job: stop.job,
    name: stop.name,
    address:
      i === 0
        ? '12 Industrial Pkwy, Amsterdam, NY 12010, US'
        : '1730 NY-5S, Amsterdam, NY 12010, US',
    scheduledDate: new Date(now + 86400000).toISOString().slice(0, 10),
    scheduledTime: i === 0 ? '07:00:00' : '09:00:00',
    stopNo: i === 0 ? 'PU 441' : 'DEL 3802',
    truckNumber: '11006',
    trailerNumber: '9P1571',
    driverName: 'Maksims Ostanins',
  })),
};

const stationRows = Object.values(stations).map(station => ({
  id: station.id,
  externalId: station.name,
  name: station.name,
  city: station.city,
  region: station.region,
  country: 'US',
  address: station.address,
  latitude: station.point.latitude,
  longitude: station.point.longitude,
  cashDiscount: {
    unit: 'US gal',
    currency: 'USD',
    product: 'Diesel',
    retailPrice: station.price + 0.6,
    discountPrice: station.price,
    priceAfterIfta: station.price - 0.08,
    savings: 0.6,
    effectiveFrom: today,
    effectiveTo: today,
  },
  discounts: [
    {
      unit: 'US gal',
      currency: 'USD',
      product: 'Diesel',
      retailPrice: station.price + 0.6,
      discountPrice: station.price,
      priceAfterIfta: station.price - 0.08,
      savings: 0.6,
      effectiveFrom: today,
      effectiveTo: today,
    },
  ],
}));

function answer(path, url, theme) {
  const truckMatch = path.match(/^\/api\/fleet\/trucks\/([^/]+)\/(.+)$/);
  const loadMatch = path.match(/^\/api\/dispatch\/([^/]+)(\/.*)?$/);
  if (path === '/api/auth/me')
    return {
      id: userId,
      name: 'Fixture Dispatcher',
      email: 'fixture@example.invalid',
      isAdmin: true,
    };
  if (path === '/api/settings/appearance') return success({ theme });
  if (path === '/api/settings/dispatch')
    return success({ loadNumberPrefix: 'AMF', revision: 1 });
  if (path === '/api/settings/planning')
    return success({
      preferences: { useIfta: false },
      revision: 1,
      updatedAt: null,
    });
  if (path === '/api/driver-groups')
    return success({ selected: null, groups: [] });
  if (path === '/api/messaging/unread')
    return success({ conversations: 0, more: false, newest: 0 });
  if (path === '/api/fleet/locations') {
    const rows = trucks.map(truck => ({
      truckId: truck.id,
      unitNumber: truck.unit,
      driverName: truck.driver,
      trailerNumber: truck.trailer,
      trailerSource: 'execution',
      ...truck.point,
      speed: truck.speed,
      heading: 90,
      engineState: truck.engine,
      fuelPercent: truck.fuel,
      formattedLocation: truck.location,
      updatedAt: iso(-1),
    }));
    return success({ trucks: rows, points: rows });
  }
  if (path === '/api/fleet/hos')
    return success(
      Object.fromEntries(
        trucks.map(truck => [
          truck.id,
          {
            driverName: truck.driver,
            hos: truck.hos && {
              ...truck.hos,
              updatedAt: iso(0),
              currentDutyStatus: truck.duty?.status,
            },
          },
        ]),
      ),
    );
  if (path === '/api/fleet/planning/previews') return success([]);
  if (truckMatch && byId.get(truckMatch[1])) {
    const truck = byId.get(truckMatch[1]);
    if (truckMatch[2] === 'planning' || truckMatch[2] === 'planning/preview')
      return success(planning(truck));
    if (truckMatch[2] === 'weather')
      return success({
        celsius: 16.5,
        condition: 'CLEAR',
        description: 'Clear',
        isDaytime: false,
        updatedAt: iso(-10),
      });
  }
  if (path === `/api/dispatch/${nextRoute.id}`) return success(nextDispatch);
  if (loadMatch && byId.get(loadMatch[1])) {
    const truck = byId.get(loadMatch[1]);
    if (!loadMatch[2]) return success(dispatch(truck));
    if (loadMatch[2] === '/planning/automatic') return success(planning(truck));
    if (loadMatch[2] === '/planning/fuel/edit/preview')
      return success(fuelPreview(truck));
    if (loadMatch[2] === '/next-routes')
      return success({ routes: [], truckId: truck.id });
  }
  if (path.startsWith('/api/dispatch/truck/')) {
    const truck = byId.get(path.split('/')[4]);
    if (truck && path.endsWith('/next-routes'))
      return success({
        revision: `next-${truck.key}`,
        unchanged: false,
        routes: truck.key === 'us' ? [nextRoute] : [],
      });
    if (truck) return success([dispatch(truck)]);
  }
  if (path === '/api/fuel/stations') return success(stationRows);
  if (path === '/api/fuel/price-overview')
    return success(
      Object.values(stations).map(station => ({
        id: station.id,
        currency: 'USD',
        cashPrice: station.price,
        iftaPrice: station.price - 0.08,
      })),
    );
  return undefined;
}

const cases = [
  ['1440-light', 1440, 900, 'light', 1],
  ['1024-light', 1024, 800, 'light', 1],
  ['390-light', 390, 844, 'light', 1],
  ['1440-dark', 1440, 900, 'dark', 1],
  ['390-text200', 390, 844, 'light', 2],
].filter(
  ([name]) =>
    !process.env.FLEET_DESIGN_CASE || process.env.FLEET_DESIGN_CASE === name,
);
const report = {
  artifact,
  scope:
    'Compiled Fleet Map page; production docked inspector, route stop cards, arrival window and station layer/cards; synthetic in-memory API; no provider map, GPU markers, database or writes.',
  cases: [],
  errors: [],
  unexpectedRequests: [],
};
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});

// What a card looks like to a person at this size: nothing wider than the
// map, nothing clipped sideways.
async function measure(page) {
  return page.evaluate(() => {
    const card = document.querySelector('.fleet-map-inspector');
    if (!card) return null;
    const box = card.getBoundingClientRect();
    const clipped = [...card.querySelectorAll('*')]
      .filter(
        node =>
          node.scrollWidth > node.clientWidth + 1 &&
          getComputedStyle(node).overflowX !== 'visible' &&
          !node.matches('input, select, textarea'),
      )
      .map(node => node.className?.toString?.() ?? node.tagName);
    return {
      left: Math.round(box.left),
      width: Math.round(box.width),
      height: Math.round(box.height),
      viewport: window.innerWidth,
      outside: box.left < -0.5 || box.right > window.innerWidth + 0.5,
      clipped,
    };
  });
}

try {
  for (const [name, width, height, theme, text] of cases) {
    const context = await browser.newContext({
      viewport: { width, height },
      colorScheme: theme,
      hasTouch: width < 768,
      locale: 'en-US',
      timezoneId: 'America/New_York',
      reducedMotion: 'reduce',
      serviceWorkers: 'block',
    });
    await context.addInitScript(
      ({ userId, theme, text }) => {
        localStorage.setItem(
          'auth_session',
          JSON.stringify({
            Id: userId,
            AccessToken: 'fixture',
            RefreshToken: 'fixture',
          }),
        );
        // Enlarged text as a reader sets it: on the root, once it exists.
        const apply = () => {
          const root = document.documentElement;
          if (!root) return;
          root.dataset.theme = theme;
          if (text !== 1) root.style.fontSize = `${text * 100}%`;
        };
        apply();
        document.addEventListener('DOMContentLoaded', apply);
      },
      { userId, theme, text },
    );
    await installReleaseArtifact(context, artifact, origin);
    await context.route('**/*', async route => {
      const request = route.request(),
        url = new URL(request.url());
      if (url.origin !== origin) {
        report.unexpectedRequests.push(`${request.method()} ${url.href}`);
        return route.abort('blockedbyclient');
      }
      const path = url.pathname;
      if (path === '/api/messaging/changes') {
        const mailbox = url.searchParams.get('mailbox');
        if (mailbox) await new Promise(done => setTimeout(done, 5000));
        return route
          .fulfill({
            status: 200,
            json: success({
              mailbox: mailbox ?? '00000000-0000-4000-8000-00000000c4a9',
              resync: !mailbox,
              conversations: [],
            }),
          })
          .catch(() => {});
      }
      if (path.startsWith('/api/')) {
        // Asking for a fresh plan reads the same synthetic one: the
        // fixture holds no state and writes nothing.
        const refresh =
          request.method() === 'POST' &&
          (/^\/api\/fleet\/trucks\/[^/]+\/planning$/.test(path) ||
            /^\/api\/dispatch\/[^/]+\/planning\/fuel\/edit\/preview$/.test(
              path,
            ));
        if (request.method() !== 'GET' && !refresh) {
          report.unexpectedRequests.push(`${request.method()} ${path}`);
          return route.abort('blockedbyclient');
        }
        const value = answer(path, url, theme);
        if (value === undefined) {
          report.unexpectedRequests.push(`GET ${path}`);
          return route.fulfill({
            status: 404,
            json: { success: false, errors: ['Not in fixture'] },
          });
        }
        return route.fulfill({ status: 200, json: value });
      }
      if (/\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(path))
        return route.fulfill({
          status: 200,
          contentType: 'text/javascript',
          body: mapStub,
        });
      if (request.isNavigationRequest())
        return route.fulfill({
          status: 200,
          contentType: 'text/html',
          body: html,
        });
      return route.fallback();
    });
    const page = await context.newPage();
    page.on('pageerror', error =>
      report.errors.push(`${name}: ${error.message}`),
    );
    page.on('console', message => {
      if (message.type() === 'error')
        report.errors.push(`${name}: ${message.text()}`);
    });
    const steps = [];
    const requests = [];
    page.on('request', request => {
      if (request.url().includes('/api/'))
        requests.push(`${Date.now()} ${new URL(request.url()).pathname}`);
    });
    const shot = async (step, fullCard = false) => {
      await page.waitForTimeout(250);
      const file = resolve(output, `${name}-${step}.png`);
      const card = page.locator('.fleet-map-inspector');
      if (fullCard && (await card.count()))
        await card.first().screenshot({ path: file });
      else await page.screenshot({ path: file, fullPage: false });
      steps.push({ step, file, layout: await measure(page) });
    };
    const attempt = async (step, action) => {
      try {
        await action();
      } catch (error) {
        steps.push({
          step,
          failed: String(error.message).split('\n').slice(0, 3).join(' | '),
          recentRequests: requests.slice(-8),
          mapCalls: await page
            .evaluate(() => window.designFixture?.requests.slice(-15))
            .catch(() => null),
          at: Date.now(),
        });
        await page
          .screenshot({ path: resolve(output, `${name}-${step}-failed.png`) })
          .catch(() => {});
        // A failed step must not leave the phone's filters over the card.
        await page
          .locator('.fleet-map-mobile-filters[aria-expanded="true"]')
          .click({ timeout: 1000 })
          .catch(() => {});
      }
    };
    const select = async truck => {
      await page.evaluate(id => window.designFixture.selectTruck(id), truck.id);
      await page
        .locator('.fleet-map-inspector.has-selection')
        .waitFor({ timeout: 10000 });
      await page.waitForFunction(
        unit =>
          document
            .querySelector('.fleet-map-inspector')
            ?.textContent?.includes(unit),
        truck.unit,
      );
      await page.waitForTimeout(600);
    };
    // A phone's card is open whole and hides the toggle (September 26).
    const toggleDetails = async open => {
      const toggle = page.locator('.fleet-map-mobile-summary__toggle');
      if (!(await toggle.isVisible())) return;
      if (((await toggle.getAttribute('aria-expanded')) === 'true') !== open)
        await toggle.click();
    };

    await page.goto(`${origin}/fleet/map`);
    await page.waitForFunction(() => window.designFixture, null, {
      timeout: 30000,
    });

    for (const truck of trucks) {
      await attempt(`${truck.key}-compact`, async () => {
        await select(truck);
        await toggleDetails(false);
        await shot(`${truck.key}-compact`);
      });
      if (truck.key === 'us' || truck.key === 'ca' || width < 768)
        await attempt(`${truck.key}-expanded`, async () => {
          await toggleDetails(true);
          await shot(`${truck.key}-expanded`);
          // The card scrolls inside the map; its lower half as well.
          const scrolled = await page.evaluate(() => {
            const card = document.querySelector('.fleet-map-inspector');
            if (!card || card.scrollHeight <= card.clientHeight + 1)
              return false;
            card.scrollTop = card.scrollHeight;
            return true;
          });
          if (scrolled) await shot(`${truck.key}-expanded-end`);
        });
    }

    const us = trucks[0];
    await attempt('route-stop', async () => {
      await select(us);
      await page.evaluate(() => window.designFixture.openStop(2));
      await page
        .locator('.fleet-map-inspector[data-inspector-mode="stop"]')
        .waitFor({ timeout: 5000 });
      await shot('route-stop');
    });
    await attempt('next-load-stop', async () => {
      await select(us);
      // Next loads on, as the dispatcher turns it on: the box itself is
      // visually hidden, so it is its label that is clicked, and on a
      // phone the filters are opened for it and closed again after.
      const toggle = page.locator('input[aria-label="Next loads"]');
      const label = page.locator('label[title="Next loads"]');
      const filters = page.locator('.fleet-map-mobile-filters');
      const phone = !(await label.isVisible().catch(() => false));
      if (phone) await filters.click();
      if (!(await toggle.isChecked())) await label.click();
      if (phone) await filters.click();
      await page.waitForFunction(
        () => window.designFixture.requests.includes('setNextLoadsBytes'),
        null,
        { timeout: 10000 },
      );
      // The plan above carries assignment revision 1 and no execution leg.
      await page.evaluate(
        ([truck, current, load]) =>
          window.designFixture.selectNextStop(truck, current, load, 1, 1),
        [us.id, us.dispatch, nextRoute.id],
      );
      await page
        .locator(
          '[aria-label="Selected next load"] .fleet-route-popup__company',
        )
        .waitFor({ timeout: 10000 });
      await shot('next-load-stop');
    });
    await attempt('fuel-plan', async () => {
      await select(us);
      await toggleDetails(true);
      await page
        .locator('.fleet-map-inspector__actions')
        .getByRole('button', { name: 'Fuel', exact: true })
        .click();
      await page.locator('#fleet-map-fuel-panel').waitFor({ timeout: 5000 });
      await shot('fuel-plan');
    });
    // The plan card in edit: the same place, one list, the chosen stop
    // opened under its line.
    await attempt('fuel-editor', async () => {
      await page
        .getByRole('button', { name: 'Edit plan', exact: true })
        .click({ timeout: 3000 });
      await page
        .locator(
          '.fuel-plan-editor__stop.is-selected .fuel-plan-editor__detail',
        )
        .waitFor({ timeout: 10000 });
      await shot('fuel-editor');
      await page
        .getByRole('button', { name: 'Cancel', exact: true })
        .click({ timeout: 3000 });
      await page.locator('#fleet-map-fuel-panel').waitFor({ timeout: 5000 });
    });
    await attempt('fuel-stations', async () => {
      const list = page.getByRole('button', {
        name: 'Stations',
        exact: true,
      });
      // Readiness is measured, not waited out: when the list's section
      // mounts, and when the day's stations reach it.
      const started = Date.now();
      await list.click({ timeout: 3000 });
      await page
        .locator('section.fleet-fuel-stations')
        .waitFor({ state: 'attached', timeout: 5000 });
      const mounted = Date.now() - started;
      await page.waitForFunction(
        () =>
          document.querySelector('.fleet-fuel-stations__row') ||
          document.querySelector('.fleet-fuel-stations__empty') ||
          document.querySelector('.fleet-fuel-stations [role="alert"]'),
        null,
        { timeout: 5000 },
      );
      steps.push({
        step: 'fuel-stations-ready',
        mountedMs: mounted,
        readyMs: Date.now() - started,
        stationRequests: requests.filter(r => r.includes('/api/fuel/stations')),
      });
      await shot('fuel-stations');
    });
    await attempt('planned-station', async () => {
      await select(us);
      await toggleDetails(true);
      await page
        .locator('.fleet-map-inspector__actions')
        .getByRole('button', { name: 'Fuel', exact: true })
        .click();
      await page
        .locator('.fleet-fuel-plan__view')
        .first()
        .click({ timeout: 5000 });
      await page
        .locator('.fleet-map-inspector[data-inspector-mode="fuel"]')
        .waitFor({ timeout: 5000 });
      await shot('planned-station');
      // Back returns to the plan it was opened from.
      await page.getByRole('button', { name: /Back to plan/ }).click();
      await page.locator('.fleet-fuel-plan').waitFor({ timeout: 5000 });
      steps.push({ step: 'planned-station-back', ok: true });
    });
    await attempt('ordinary-station', async () => {
      // A station off the plan, opened from the day's list as a
      // dispatcher would, whether or not the station layer is on.
      await select(us);
      await toggleDetails(true);
      await page
        .locator('.fleet-map-inspector__actions')
        .getByRole('button', { name: 'Fuel', exact: true })
        .click();
      await page.getByRole('button', { name: 'Stations', exact: true }).click();
      await page
        .getByRole('button', { name: `View ${stations.pilot218.name}` })
        .click({ timeout: 5000 });
      await page
        .locator('.fleet-map-inspector[data-inspector-mode="fuel"]')
        .waitFor({ timeout: 5000 });
      await shot('ordinary-station');
      await page.getByRole('button', { name: /Back to stations/ }).click();
      await page
        .locator('section.fleet-fuel-stations')
        .waitFor({ timeout: 5000 });
      steps.push({ step: 'ordinary-station-back-to-list', ok: true });
    });
    report.cases.push({ name, width, height, theme, text, steps });
    await context.close();
  }
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
}
console.log(
  JSON.stringify(
    {
      output,
      cases: report.cases.map(c => ({
        name: c.name,
        failed: c.steps
          .filter(s => s.failed)
          .map(s => `${s.step}: ${s.failed}`),
        outside: c.steps.filter(s => s.layout?.outside).map(s => s.step),
      })),
      errors: report.errors.slice(0, 10),
      unexpected: [...new Set(report.unexpectedRequests)],
    },
    null,
    2,
  ),
);
