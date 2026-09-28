// The Fleet workspace and Dispatch in the compiled Client: both themes at
// desktop and phone
// widths, drawn on the provider's real basemap with the browser key from
// wwwroot/appsettings.json (never written to the report). It also checks
// that the Fleet Map keeps its behaviour: list and chain
// selection reach the inspector, Follow holds through position updates, the
// map turns to satellite at close zoom and back to the road map when
// zoomed out, and Dispatch keeps Cards, Table, Papers, search and scope.
// APIs are synthetic and read-only; no account, database or write is used.
//
// MAP_TEST_ARTIFACT_DIR=/abs/publish/wwwroot \
//   node tests/browser/workspaceSmoke.mjs
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { inflateSync } from 'node:zlib';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui', process.env.WORKSPACE_TEST_OUTPUT_DIR);
const settingsFile =
  process.env.MAP_TEST_APPSETTINGS ?? resolve('wwwroot/appsettings.json');
const appsettings = await readFile(settingsFile, 'utf8');
const key = JSON.parse(appsettings).GoogleMaps?.ApiKey;
assert.ok(key, 'appsettings.json has no browser key');
const redact = text => String(text).split(key).join('[key]');
const origin = 'http://localhost:5079';
const provider = /(^|\.)(googleapis|gstatic|google)\.com$/;
const user = '11111111-1111-1111-1111-111111111111';
const guid = n => `5a0e5c1e-7d5b-4a61-9d7e-${String(n).padStart(12, '0')}`;
const success = response => ({ success: true, response, errors: [] });
const day = offset => {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  return d.toISOString().slice(0, 10);
};

const places = {
  nashville: ['Nashville', 'TN', 36.16, -86.78],
  knoxville: ['Knoxville', 'TN', 35.96, -83.92],
  richmond: ['Richmond', 'VA', 37.54, -77.44],
  baltimore: ['Baltimore', 'MD', 39.29, -76.61],
  chicago: ['Chicago', 'IL', 41.88, -87.63],
  detroit: ['Detroit', 'MI', 42.33, -83.05],
  atlanta: ['Atlanta', 'GA', 33.75, -84.39],
  charlotte: ['Charlotte', 'NC', 35.23, -80.84],
  columbus: ['Columbus', 'OH', 39.96, -83.0],
  pittsburgh: ['Pittsburgh', 'PA', 40.44, -79.99],
  albany: ['Albany', 'NY', 42.65, -73.76],
  boston: ['Boston', 'MA', 42.36, -71.06],
};
const cycle = Object.keys(places);
const plans = [
  ['54777', ['nashville', 'knoxville', 'richmond', 'baltimore'], 52],
  ['11006', ['chicago', 'detroit', 'columbus'], 61],
  ['11014', ['atlanta', 'charlotte', 'richmond'], 58],
  ['11018', ['pittsburgh', 'columbus'], 0],
  ['11022', ['albany', 'boston', 'albany'], 55],
  ['11027', ['charlotte', 'atlanta'], 0],
  // One trip, four trips and many trips, for the chain's rows.
  ['11031', ['detroit', 'chicago'], 48],
  ['11044', ['boston', 'albany', 'pittsburgh', 'columbus', 'chicago'], 57],
  ['11052', cycle.slice(0, 10), 60],
  // The rest of a twenty-truck fleet.
  ...Array.from({ length: 11 }, (_, i) => [
    String(12000 + i * 7),
    [
      cycle[i % cycle.length],
      cycle[(i + 3) % cycle.length],
      cycle[(i + 5) % cycle.length],
    ],
    i % 3 === 0 ? 0 : 50 + i,
  ]),
];
let stopNumber = 0;
const stopOf = (place, sequence, job, date, done) => {
  const [city, province, latitude, longitude] = places[place];
  return {
    id: guid(3000 + ++stopNumber),
    sequence,
    job,
    name: `${city} ${job === 'Pickup' ? 'shipper' : 'receiver'}`,
    city,
    province,
    country: 'USA',
    address: `${100 + stopNumber} Fixture Road, ${city}, ${province}`,
    scheduledDate: date,
    scheduledTime: '10:00:00',
    latitude,
    longitude,
    isCompleted: done,
  };
};
const trucks = [];
const loads = new Map();
const rows = [];
plans.forEach(([unit, route, speed], t) => {
  const [, , lat0, lng0] = places[route[0]];
  const [, , lat1, lng1] = places[route[1]];
  const truckId = guid(100 + t);
  trucks.push({
    truckId,
    truckExternalId: `fixture-${t + 1}`,
    unitNumber: unit,
    driverName: `Fixture Driver ${t + 1}`,
    trailerNumber: `TR-${540 + t}`,
    latitude: lat0 + (lat1 - lat0) * 0.4,
    longitude: lng0 + (lng1 - lng0) * 0.4,
    speed,
    heading: 90,
    updatedAt: new Date().toISOString(),
    formattedLocation: `Near ${places[route[0]][0]}, ${places[route[0]][1]}`,
    engineState: speed ? 'On' : 'Off',
    fuelPercent: 60 + t * 5,
  });
  const dispatches = [];
  for (let i = 0; i + 1 < route.length; i++) {
    const id = guid(200 + t * 10 + i);
    const load = {
      id,
      truckId,
      loadNumber: 1409 + t * 10 + i,
      orderNumber: `ORD-${t}${i}`,
      status: i === 0 ? 'in_transit' : 'planned',
      completed: false,
      // The server's placement of the load on its truck. One load of the
      // second truck is read at another revision than its inputs.
      workPhase:
        t === 1 && i === 1
          ? 'stale'
          : ['current', 'next', 'upcoming'][Math.min(i, 2)],
      customerName: `Fixture Customer ${t + 1}`,
      truckNumber: unit,
      driverName: `Fixture Driver ${t + 1}`,
      trailerNumber: `TR-${540 + t}`,
      shipDate: day(i),
      deliveryDate: day(i + 1),
      price: 1800 + i * 250,
      currency: 'USD',
      stops: [
        stopOf(route[i], 1, 'Pickup', day(i), i === 0),
        stopOf(route[i + 1], 2, 'Delivery', day(i + 1), false),
        // The first truck's current load drops at two receivers: D1, D2.
        ...(t === 0 && i === 0
          ? [stopOf(route[i + 1], 3, 'Delivery', day(i + 1), false)]
          : []),
      ],
      eta: null,
    };
    loads.set(id, load);
    dispatches.push(load);
  }
  rows.push({
    key: truckId,
    truckId,
    truckNumber: unit,
    driverName: `Fixture Driver ${t + 1}`,
    trailerNumber: `TR-${540 + t}`,
    speed,
    engineState: speed ? 'On' : 'Off',
    dispatches,
  });
});
const first = trucks[0];
// Positions are a function of time, so every read agrees with the last.
const started = Date.now();
const gps = { stoppedAt: null };
const point = (latitude, longitude) => ({ latitude, longitude });
// A saved fuel plan with one purchase, so the plan card, its editor and the
// send window have something to show. Values are synthetic.
const fuelPlanOf = (truck, load, destination) => ({
  truckId: truck.truckId,
  calculatedAt: new Date().toISOString(),
  pricingDate: day(0),
  needsRefresh: false,
  manuallyEdited: false,
  dispatchIds: [load.id],
  purchaseGallons: 90,
  purchaseCostUsd: 330.48,
  arrivalGallons: 60,
  startingGallons: 120,
  remainingMiles: 180,
  stops: [
    {
      number: 1,
      stationId: guid(700),
      beforeStopId: destination.id,
      dispatchId: load.id,
      name: 'Fixture Travel Center #1',
      point: point(
        (truck.latitude + destination.latitude) / 2,
        (truck.longitude + destination.longitude) / 2,
      ),
      address: '1 Fixture Exit, Fixture, TN',
      arrivalGallons: 40,
      departureGallons: 130,
      buyGallons: 90,
      fillToTarget: false,
      milesAhead: 80,
      purchaseCostUsd: 330.48,
      yourPrice: 3.672,
      cashUsdPerGallon: 3.672,
      currency: 'USD',
      unit: 'US gal',
    },
  ],
});
const planning = truck => {
  const row = rows.find(r => r.truckId === truck.truckId);
  const current = row.dispatches[0];
  const [, destination] = current.stops;
  const now = new Date().toISOString();
  return success({
    truckId: truck.truckId,
    dispatchId: current.id,
    loadNumber: current.loadNumber,
    state: {
      profile: {},
      apiConfigured: false,
      fuelPercent: truck.fuelPercent,
      fuelUpdatedAt: now,
      eta: null,
      plan: {
        id: guid(400 + trucks.indexOf(truck)),
        dispatchId: current.id,
        truckId: truck.truckId,
        version: 1,
        calculatedAt: now,
        originalPlannedMiles: 320,
        fromCurrentPosition: true,
        profile: {},
        fuelPlan: fuelPlanOf(truck, current, destination),
        stops: [
          {
            ...destination,
            point: point(destination.latitude, destination.longitude),
          },
        ],
        tracking: {
          nextStopId: destination.id,
          passedStopIds: [],
          visitedStops: {},
        },
        route: {
          miles: 180,
          seconds: 12_000,
          warnings: [],
          points: [],
          legs: [
            {
              miles: 180,
              seconds: 12_000,
              points: [
                point(truck.latitude, truck.longitude),
                point(destination.latitude, destination.longitude),
              ],
            },
          ],
        },
      },
      progress: {
        progressMiles: 140,
        remainingMiles: 180,
        remainingSeconds: 12_000,
        distanceFromRouteMiles: 0,
        offRoute: false,
        locationStale: false,
        locationTime: now,
        position: point(truck.latitude, truck.longitude),
      },
    },
  });
};
const nextRoutes = truckId => {
  const row = rows.find(r => r.truckId === truckId);
  return success({
    revision: `fixture-${truckId}`,
    unchanged: false,
    routes: row.dispatches.slice(1).map(load => ({
      id: load.id,
      loadNumber: load.loadNumber,
      status: 'planned',
      stopCount: load.stops.length,
      stops: load.stops.map(({ id, latitude, longitude, job, name }) => ({
        id,
        latitude,
        longitude,
        job,
        name,
      })),
      deadhead: null,
      legs: [
        {
          miles: 300,
          seconds: 18_000,
          points: load.stops.map(s => point(s.latitude, s.longitude)),
        },
      ],
    })),
  });
};
const boardReads = [];
const board = (number, search, truckId) => {
  const q = (search ?? '').toLowerCase();
  const items = rows.filter(
    r =>
      (!truckId || r.truckId === truckId) &&
      (!q ||
        [r.truckNumber, r.driverName, ...r.dispatches.map(d => d.loadNumber)]
          .join(' ')
          .toLowerCase()
          .includes(q)),
  );
  return success({
    items,
    page: number,
    pageSize: 12,
    totalCount: items.length,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  });
};
const fixtures = new Map([
  [
    '/api/auth/me',
    {
      id: user,
      name: 'Fixture Administrator',
      email: 'fixture@example.invalid',
      isAdmin: true,
    },
  ],
  ['/api/settings/dispatch', success({ loadNumberPrefix: 'AMF', revision: 1 })],
  [
    '/api/settings/planning',
    success({ preferences: { useIfta: true }, revision: 1, updatedAt: null }),
  ],
  ['/api/driver-groups', success({ selected: null, groups: [] })],
  ['/api/fleet/planning/previews', success([])],
  ['/api/fuel/price-overview', success([])],
  ['/api/dispatch/board/enrichment', success([])],
  ['/api/messaging/unread', success({ count: 0 })],
]);

const report = {
  artifact,
  scope:
    'Compiled Client with synthetic read-only APIs on the real basemap. ' +
    'No live account, database or writes.',
  checks: [],
  screenshots: [],
  errors: [],
  unexpectedRequests: [],
};
const check = (condition, message, detail) => {
  report.checks.push({ ok: !!condition, message, detail });
  if (!condition) report.errors.push(`${message} ${JSON.stringify(detail)}`);
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${message}`);
};

const html = await readFile(resolve(artifact, 'index.html'), 'utf8');
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
  args: [
    '--ignore-gpu-blocklist',
    '--use-angle=metal',
    '--enable-gpu-rasterization',
  ],
});

// Records the provider map's type changes and camera moves, so the checks
// read what the page asked of the real map.
const recorder = () => {
  window.mapTypes = [];
  window.cameraMoves = 0;
  const hook = setInterval(() => {
    const Map = window.google?.maps?.Map;
    if (!Map || Map.prototype.__recorded) return;
    Map.prototype.__recorded = true;
    const setType = Map.prototype.setMapTypeId;
    Map.prototype.setMapTypeId = function (type) {
      window.mapTypes.push(type);
      return setType.call(this, type);
    };
    const move = Map.prototype.moveCamera;
    Map.prototype.moveCamera = function (...args) {
      window.cameraMoves += 1;
      window.mapRef = this;
      return move.apply(this, args);
    };
    window.camera = () => {
      const map = window.mapRef;
      const center = map?.getCenter();
      return center
        ? { lat: center.lat(), lng: center.lng(), zoom: map.getZoom() }
        : null;
    };
    clearInterval(hook);
  }, 20);
};

async function open({ theme, width, height }) {
  const context = await browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: 1,
    serviceWorkers: 'block',
  });
  await context.addInitScript(
    ({ user }) => {
      localStorage.setItem(
        'auth_session',
        JSON.stringify({ Id: user, AccessToken: 'fixture', RefreshToken: 'x' }),
      );
      localStorage.setItem(
        `pulsartms.fleet-map.preferences.${user}`,
        JSON.stringify({ showNextLoads: true, showTraffic: false }),
      );
    },
    { user },
  );
  await context.addInitScript(recorder);
  await installReleaseArtifact(context, artifact, origin);
  await context.route('**/*', async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (provider.test(url.hostname)) return route.continue();
    const readPost =
      request.method() === 'POST' &&
      (url.pathname === '/api/dispatch/board/planning' ||
        /^\/api\/fleet\/trucks\/[^/]+\/planning$/.test(url.pathname) ||
        /^\/api\/dispatch\/[^/]+\/planning\/automatic$/.test(url.pathname) ||
        // The editor's preview computes a draft; it saves nothing.
        /^\/api\/dispatch\/[^/]+\/planning\/fuel\/edit\/preview$/.test(
          url.pathname,
        ));
    if (
      url.origin !== origin ||
      (!['GET', 'HEAD'].includes(request.method()) && !readPost)
    ) {
      report.unexpectedRequests.push(`${request.method()} ${redact(url)}`);
      return route.abort('blockedbyclient');
    }
    if (url.pathname === '/appsettings.json')
      return route.fulfill({
        contentType: 'application/json',
        body: appsettings,
      });
    if (url.pathname.startsWith('/api/')) {
      const path = url.pathname;
      let m;
      if (path === '/api/settings/appearance')
        return route.fulfill({ json: success({ theme }) });
      if (path === '/api/fleet/locations') {
        // The first truck is moving: its recent positions, ten seconds
        // apart, advance east. The map plays positions back 90 s behind the
        // reports, so the history must cover that window.
        // Once GPS stops (gps.stoppedAt) no newer report is ever sent.
        const now = gps.stoppedAt ?? Date.now();
        const history = Array.from({ length: 19 }, (_, i) => {
          const at = now - (18 - i) * 10_000;
          return {
            ...first,
            longitude: first.longitude + ((at - started) / 10_000) * 0.003,
            updatedAt: new Date(at).toISOString(),
          };
        });
        const list = [history.at(-1), ...trucks.slice(1)];
        return route.fulfill({
          json: success({
            trucks: list,
            points: [...history, ...trucks.slice(1)],
          }),
        });
      }
      if (path === '/api/fleet/hos')
        return route.fulfill({ json: success({}) });
      if (path === '/api/dispatch/board') {
        boardReads.push(url.searchParams.get('truckId'));
        return route.fulfill({
          json: board(
            Number(url.searchParams.get('page') ?? 1),
            url.searchParams.get('search'),
            url.searchParams.get('truckId'),
          ),
        });
      }
      if (path === '/api/dispatch')
        return route.fulfill({
          json: success({
            items: [],
            page: 1,
            pageSize: 12,
            totalCount: 0,
            totalPages: 1,
          }),
        });
      if (path === '/api/dispatch/board/telemetry')
        return route.fulfill({
          json: success(
            trucks.map(t => ({
              truckId: t.truckId,
              speed: t.speed,
              engineState: t.engineState,
              trailerNumber: t.trailerNumber,
            })),
          ),
        });
      if (path === '/api/dispatch/board/planning')
        return route.fulfill({ json: success([]) });
      if ((m = path.match(/^\/api\/fleet\/trucks\/([^/]+)\/planning$/))) {
        const truck = trucks.find(t => t.truckId === m[1]);
        return route.fulfill({ json: planning(truck) });
      }
      if ((m = path.match(/^\/api\/dispatch\/([^/]+)\/planning\/automatic$/))) {
        const load = loads.get(m[1]);
        const truck = trucks.find(t => t.truckId === load?.truckId);
        return route.fulfill({ json: planning(truck) });
      }
      if (
        (m = path.match(
          /^\/api\/dispatch\/([^/]+)\/planning\/fuel\/edit\/preview$/,
        ))
      ) {
        const load = loads.get(m[1]);
        const truck = trucks.find(t => t.truckId === load?.truckId);
        const plan = planning(truck).response.state.plan.fuelPlan;
        return route.fulfill({
          json: success({
            plan,
            stops: plan.stops.map(stop => ({
              stationId: stop.stationId,
              beforeStopId: stop.beforeStopId,
              buyGallons: stop.buyGallons,
              fillToTarget: false,
              purchaseLimitGallons: 150,
            })),
            expectedCalculatedAt: plan.calculatedAt,
            tankGallons: 200,
            fillLimitGallons: 190,
            errors: [],
            valuesAvailable: true,
          }),
        });
      }
      if (/^\/api\/fleet\/trucks\/[^/]+\/camera$/.test(path))
        return route.fulfill({
          json: success({ status: 'unavailable', url: null, capturedAt: null }),
        });
      if ((m = path.match(/^\/api\/dispatch\/truck\/([^/]+)\/next-routes$/)))
        return route.fulfill({ json: nextRoutes(m[1]) });
      if ((m = path.match(/^\/api\/dispatch\/truck\/([^/]+)$/)))
        return route.fulfill({
          json: success(rows.find(r => r.truckId === m[1])?.dispatches ?? []),
        });
      if ((m = path.match(/^\/api\/dispatch\/([^/]+)$/)) && loads.has(m[1]))
        return route.fulfill({ json: success(loads.get(m[1])) });
      if ((m = path.match(/^\/api\/dispatch\/([^/]+)\/planning\/map$/)))
        return route.fulfill({
          json: success({ dispatchId: m[1], segments: [], missingSections: 0 }),
        });
      if (/^\/api\/fleet\/trucks\/[^/]+\/weather$/.test(path))
        return route.fulfill({
          json: success({
            celsius: 21,
            condition: 'CLEAR',
            description: 'Clear',
            isDaytime: true,
            updatedAt: new Date().toISOString(),
          }),
        });
      const fixture = fixtures.get(path);
      if (fixture) return route.fulfill({ json: fixture });
      report.unexpectedRequests.push(`${request.method()} ${path}`);
      return route.fulfill({ status: 404, json: success(null) });
    }
    if (request.isNavigationRequest())
      return route.fulfill({ contentType: 'text/html', body: html });
    return route.fallback();
  });
  const tab = await context.newPage();
  tab.on('pageerror', error => report.errors.push(redact(error.message)));
  tab.on('console', message => {
    // Missing reads are listed as unexpected requests instead.
    if (
      message.type() === 'error' &&
      // The route preview is refused on purpose (it would ask the
      // provider); the editor logs that and shows its retry.
      !/favicon|Failed to load resource|Route preview failed/.test(
        message.text(),
      )
    )
      report.errors.push(`console: ${redact(message.text()).slice(0, 300)}`);
  });
  return { context, tab };
}

// How many distinct colours (to 5 bits a channel) a PNG screenshot holds:
// imagery has thousands, a road map a few hundred. Reads 8-bit RGB(A).
function colours(png) {
  const ihdr = png.indexOf('IHDR');
  const width = png.readUInt32BE(ihdr + 4);
  const height = png.readUInt32BE(ihdr + 8);
  const channels = png[ihdr + 13] === 6 ? 4 : 3;
  const chunks = [];
  for (let at = 8; at < png.length; ) {
    const length = png.readUInt32BE(at);
    const type = png.toString('ascii', at + 4, at + 8);
    if (type === 'IDAT') chunks.push(png.subarray(at + 8, at + 8 + length));
    at += length + 12;
  }
  const raw = inflateSync(Buffer.concat(chunks));
  const stride = width * channels;
  const rows = [];
  let previous = new Uint8Array(stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    const out = new Uint8Array(stride);
    for (let x = 0; x < stride; x++) {
      const a = x >= channels ? out[x - channels] : 0;
      const b = previous[x];
      const c = x >= channels ? previous[x - channels] : 0;
      const p = a + b - c;
      const pa = Math.abs(p - a),
        pb = Math.abs(p - b),
        pc = Math.abs(p - c);
      const predictor = [
        0,
        a,
        b,
        (a + b) >> 1,
        pa <= pb && pa <= pc ? a : pb <= pc ? b : c,
      ][filter];
      out[x] = (line[x] + predictor) & 255;
    }
    rows.push(out);
    previous = out;
  }
  const seen = new Set();
  for (let y = 0; y < height; y += 2)
    for (let x = 0; x < width; x += 2) {
      const i = x * channels;
      seen.add(
        ((rows[y][i] >> 3) << 10) |
          ((rows[y][i + 1] >> 3) << 5) |
          (rows[y][i + 2] >> 3),
      );
    }
  return seen.size;
}

const shot = async (tab, name) => {
  const path = resolve(output, `${name}.png`);
  await tab.screenshot({ path });
  report.screenshots.push(path);
};
const noOverflow = async (tab, name) => {
  const over = await tab.evaluate(
    () =>
      document.documentElement.scrollWidth -
      document.documentElement.clientWidth,
  );
  check(over <= 0, `${name}: no page horizontal overflow`, { over });
};
try {
  // Screens: both themes, desktop and phone.
  // WORKSPACE_BEHAVIOUR_ONLY=1 skips them for a quick behaviour pass.
  if (!process.env.WORKSPACE_BEHAVIOUR_ONLY)
    for (const theme of ['light', 'dark'])
      for (const [size, width, height] of [
        ['desktop', 1440, 900],
        ['phone', 390, 844],
        ['narrow', 360, 780],
      ]) {
        const { context, tab } = await open({ theme, width, height });
        const name = `${theme}-${size}`;
        await tab.goto(`${origin}/fleet/map?truckId=${first.truckId}`);
        await tab.locator('#fleet-map').waitFor();
        await tab.waitForTimeout(6000);
        await noOverflow(tab, `${name} fleet`);
        await shot(tab, `${name}-fleet`);
        if (name.endsWith('-desktop')) {
          // The chosen truck's sonar moves on the real map: frames a sweep's
          // fraction apart differ, and each is kept for a look.
          const frames = [];
          for (let i = 0; i < 4; i++) {
            const png = await tab
              .locator('#fleet-map')
              .screenshot({ type: 'png' });
            await writeFile(
              resolve(output, `${name}-sonar-frame-${i}.png`),
              png,
            );
            frames.push(png.toString('base64'));
            await tab.waitForTimeout(150);
          }
          check(
            new Set(frames).size >= 3,
            "the selected truck's sonar animates on the map",
            { distinct: new Set(frames).size },
          );
          // The chain's markers open the stops' cards in the panel: the
          // current trip's pickup, then a later trip's first stop.
          const markers = tab.locator('button.fleet-trip-chain__stop');
          if ((await markers.count()) > 3) {
            await markers.nth(1).click();
            await tab.waitForTimeout(1500);
            await shot(tab, `${name}-stop-current`);
            await markers.nth(3).click();
            await tab.waitForTimeout(2000);
            await shot(tab, `${name}-stop-next`);
          }
        }
        await tab.goto(`${origin}/dispatch`);
        await tab.locator('article.dispatch-truck').first().waitFor();
        await tab.waitForTimeout(800);
        await noOverflow(tab, `${name} dispatch`);
        await shot(tab, `${name}-dispatch-cards`);
        if (size === 'desktop') {
          for (const view of ['Table', 'Papers']) {
            await tab
              .locator('.dispatch-view button', { hasText: view })
              .click();
            await tab.waitForTimeout(700);
            await shot(tab, `${name}-dispatch-${view.toLowerCase()}`);
          }
        }
        await context.close();
      }

  // WORKSPACE_SCREENS_ONLY=1 stops after the screens, for a quick look.
  if (!process.env.WORKSPACE_SCREENS_ONLY) {
    // Behaviour in the Fleet Map.
    {
      const { context, tab } = await open({
        theme: 'dark',
        width: 1440,
        height: 900,
      });
      const readsBefore = boardReads.length;
      await tab.goto(`${origin}/fleet/map?truckId=${first.truckId}`);
      await tab.locator('.fleet-truck-list__row').first().waitFor();
      await tab.waitForTimeout(5000);
      const rowsShown = await tab.locator('.fleet-truck-list__row').count();
      check(rowsShown === trucks.length, 'the list shows all twenty trucks', {
        rowsShown,
      });
      const title = () =>
        tab.locator('.fleet-map-inspector__desktop-title').textContent();
      check(
        (await title()).trim() === first.unitNumber,
        'the addressed truck opens in the inspector',
        await title(),
      );
      const toggleShown = await tab
        .locator('.fleet-map-mobile-summary__toggle')
        .isVisible();
      const detailsShown = await tab.locator('#fleet-map-details').isVisible();
      check(
        !toggleShown && detailsShown,
        'the docked truck card shows its details whole',
        { toggleShown, detailsShown },
      );

      // The list selects through the page's own path; the chain follows.
      await tab.locator('.fleet-truck-list__row').nth(1).click();
      await tab.waitForTimeout(1500);
      check(
        (await title()).trim() === trucks[1].unitNumber,
        'choosing a truck in the list opens it in the inspector',
        await title(),
      );
      const chainTitle = await tab
        .locator('#fleet-trip-chain-title')
        .textContent();
      check(
        chainTitle.includes(trucks[1].unitNumber),
        'the chain follows the selection',
        chainTitle,
      );
      const staleChain = await tab
        .locator('.fleet-trip-chain__phase')
        .allTextContents();
      check(
        staleChain.includes('Needs refresh'),
        'a load the server marks stale says so in the chain',
        staleChain,
      );
      await tab.locator('.fleet-truck-list__row').nth(0).click();
      await tab.waitForTimeout(2500);
      const perTruck = boardReads.slice(readsBefore).filter(Boolean);
      check(
        JSON.stringify(perTruck) ===
          JSON.stringify([first.truckId, trucks[1].truckId, first.truckId]),
        'the chain reads the board once per truck chosen',
        perTruck,
      );

      // Trips and their stops: badges within each trip, never its place.
      const chips = await tab.evaluate(() =>
        [...document.querySelectorAll('.fleet-trip-chain__link')].map(link =>
          [...link.querySelectorAll('.fleet-trip-chain__stop')].map(x =>
            x.textContent.trim(),
          ),
        ),
      );
      check(
        JSON.stringify(chips) ===
          JSON.stringify([
            ['P', 'D1', 'D2'],
            ['P', 'D'],
            ['P', 'D'],
          ]),
        'each trip shows its own P and D stops',
        chips,
      );
      const chainPhases = await tab
        .locator('.fleet-trip-chain__phase')
        .allTextContents();
      check(
        JSON.stringify(chainPhases) ===
          JSON.stringify(['Current', 'Next', 'Upcoming']),
        'the chain names the server work phases',
        chainPhases,
      );
      const panel = async () => ({
        trip: (
          await tab.locator('#fleet-trip-detail-title').textContent()
        ).trim(),
        trips: await tab.locator('.fleet-trip-detail').count(),
        focused: await tab
          .locator(
            '.fleet-trip-detail__stop.is-focused .dispatch-load__stop-number',
          )
          .allTextContents(),
        mode: await tab
          .locator('.fleet-map-inspector')
          .getAttribute('data-inspector-mode'),
      });
      let now = await panel();
      check(
        now.trips === 1 && now.trip === 'AMF1409' && now.mode === 'truck',
        'the panel shows only the current trip at first',
        now,
      );

      // Follow: close zoom, satellite, and the moving truck kept in view.
      // Follow lives only in the map tool bar (the owner, 2026-09-28: the
      // head's quick Follow repeated it and was removed).
      const follow = tab.locator(
        '.fleet-map-controls button[aria-label="Follow"]',
      );
      await follow.click();
      await tab.waitForTimeout(3000);
      const c0 = await tab.evaluate(() => window.camera());
      check(
        (await follow.getAttribute('aria-pressed')) === 'true' &&
          c0?.zoom === 15,
        'Follow starts at close zoom',
        c0,
      );
      check(
        (await tab.evaluate(() => window.mapTypes.at(-1))) === 'hybrid',
        'Follow at close zoom switches the map to hybrid',
      );
      const mode = (await tab.locator('.fleet-map-mode').textContent()).trim();
      check(/Satellite/.test(mode), 'the map says it is in satellite', mode);
      const satellite = colours(
        await tab.locator('#fleet-map').screenshot({ type: 'png' }),
      );
      await shot(tab, 'behaviour-follow-satellite');
      await tab.waitForTimeout(21000);
      const c1 = await tab.evaluate(() => window.camera());
      check(
        (await follow.getAttribute('aria-pressed')) === 'true' &&
          c1.zoom === c0.zoom &&
          c1.lng - c0.lng > 0.0005 &&
          Math.abs(c1.lat - c0.lat) < 0.01,
        'successive GPS reports move the camera with the truck, zoom kept',
        { c0, c1 },
      );
      await shot(tab, 'behaviour-follow-after-updates');

      // Choosing another trip or stop keeps Follow and the camera's zoom.
      await tab.locator('.fleet-trip-chain__trip').nth(1).click();
      await tab.waitForTimeout(1200);
      now = await panel();
      const c2 = await tab.evaluate(() => window.camera());
      // Its stop's own facts (distance, fresh ETA) come with the trip; the
      // tool bar's Follow stays on, visible and enabled.
      const followBox = await follow.boundingBox();
      const viewport = tab.viewportSize();
      check(
        now.trips === 1 &&
          now.trip === 'AMF1410' &&
          now.mode === 'nextstop' &&
          (await follow.getAttribute('aria-pressed')) === 'true' &&
          (await follow.isVisible()) &&
          (await follow.isEnabled()) &&
          followBox !== null &&
          followBox.x >= 0 &&
          followBox.x + followBox.width <= viewport.width &&
          c2.zoom === c0.zoom,
        'choosing a later trip shows only it and keeps Follow',
        { now, c2 },
      );
      await tab
        .locator('.fleet-trip-chain__link')
        .nth(0)
        .locator('.fleet-trip-chain__stop')
        .nth(2)
        .click();
      await tab.waitForTimeout(1200);
      now = await panel();
      check(
        now.trip === 'AMF1409' &&
          JSON.stringify(now.focused) === JSON.stringify(['D2']) &&
          (await follow.getAttribute('aria-pressed')) === 'true',
        'a D2 chip opens that stop in the current trip, Follow kept',
        now,
      );
      await shot(tab, 'behaviour-trip-stop-focus');

      // A drag is the reader's camera: Follow ends; pressing it resumes.
      const box = await tab.locator('#fleet-map').boundingBox();
      await tab.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await tab.mouse.down();
      await tab.mouse.move(
        box.x + box.width / 2 + 120,
        box.y + box.height / 2,
        {
          steps: 8,
        },
      );
      await tab.mouse.up();
      await tab.waitForTimeout(800);
      const c3 = await tab.evaluate(() => window.camera());
      check(
        (await follow.getAttribute('aria-pressed')) === 'false' &&
          c3.zoom === c0.zoom,
        'dragging the map ends Follow without changing zoom',
        c3,
      );
      await follow.click();
      await tab.waitForTimeout(2000);
      check(
        (await follow.getAttribute('aria-pressed')) === 'true' &&
          (await tab.evaluate(() => window.camera().zoom)) === 15,
        'Follow resumes on the truck',
      );

      // GPS stops: the truck plays out what was reported, then stands; the
      // camera does not invent motion.
      gps.stoppedAt = Date.now();
      await tab.waitForTimeout(100000);
      const c4 = await tab.evaluate(() => window.camera());
      await tab.waitForTimeout(12000);
      const c5 = await tab.evaluate(() => window.camera());
      check(
        (await follow.getAttribute('aria-pressed')) === 'true' &&
          Math.abs(c5.lng - c4.lng) < 1e-7 &&
          Math.abs(c5.lat - c4.lat) < 1e-7,
        'without new GPS reports the followed camera stands still',
        { c4, c5 },
      );
      gps.stoppedAt = null;

      // Zooming out is the reader's camera: Follow ends, the road map returns.
      await tab.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      for (let i = 0; i < 8; i++) {
        await tab.mouse.wheel(0, 400);
        await tab.waitForTimeout(250);
      }
      await tab.waitForTimeout(3000);
      const ended = await follow.getAttribute('aria-pressed');
      const typeOut = await tab.evaluate(() => window.mapTypes.at(-1));
      const modeOut = (
        await tab.locator('.fleet-map-mode').textContent()
      ).trim();
      check(
        ended === 'false' && typeOut === 'roadmap' && /Road map/.test(modeOut),
        'zooming out ends Follow and returns the road map',
        {
          ended,
          typeOut,
          modeOut,
        },
      );
      const roadmap = colours(
        await tab.locator('#fleet-map').screenshot({ type: 'png' }),
      );
      check(
        // Imagery of uniform fields reads about 1.9x the road map's
        // colours; vector road maps stay well under that.
        satellite > roadmap * 1.5,
        'the satellite view is drawn as imagery, not as the road map',
        { satellite, roadmap },
      );
      await shot(tab, 'behaviour-zoomed-out-roadmap');
      await context.close();
    }

    // The chain's rows with one, four and many trips, desktop and phone.
    for (const [size, width, height] of [
      ['desktop', 1440, 900],
      ['narrow', 360, 780],
    ]) {
      const { context, tab } = await open({
        theme: 'light',
        width,
        height,
      });
      for (const [unit, count] of [
        ['11031', 1],
        ['11044', 4],
        ['11052', 9],
      ]) {
        const truck = trucks.find(t => t.unitNumber === unit);
        await tab.goto(`${origin}/fleet/map?truckId=${truck.truckId}`);
        await tab.locator('.fleet-trip-chain__link').first().waitFor();
        await tab.waitForTimeout(2500);
        const rows = await tab.evaluate(() => {
          const list = document.querySelector('.fleet-trip-chain__links');
          const shown = [...list.children].filter(
            li => getComputedStyle(li).display !== 'none',
          );
          const tops = new Set(
            shown.map(li => Math.round(li.getBoundingClientRect().top)),
          );
          return {
            total: list.children.length,
            shown: shown.length,
            rows: tops.size,
            selected: shown.some(li => li.classList.contains('is-selected')),
            across: list.scrollWidth - list.clientWidth,
            page:
              document.documentElement.scrollWidth -
              document.documentElement.clientWidth,
          };
        });
        const limit = size === 'narrow' ? 1 : 2;
        check(
          rows.total === count &&
            rows.rows <= limit &&
            rows.selected &&
            rows.across <= 0 &&
            rows.page <= 0,
          `${size}: ${count} trip(s) wrap without sideways scrolling`,
          rows,
        );
        await shot(tab, `chain-${size}-${count}-trips`);
        if (count > 1) {
          await tab.locator('.fleet-trip-chain__all').click();
          await tab.waitForTimeout(500);
          const all = await tab.evaluate(() => {
            const list = document.querySelector('.fleet-trip-chain__links');
            return {
              shown: [...list.children].filter(
                li => getComputedStyle(li).display !== 'none',
              ).length,
              across: list.scrollWidth - list.clientWidth,
            };
          });
          check(
            all.shown === count && all.across <= 0,
            `${size}: All trips (${count}) shows every trip`,
            all,
          );
          await shot(tab, `chain-${size}-${count}-trips-all`);
          await tab.locator('.fleet-trip-chain__all').click();
        }
      }
      if (size === 'narrow') {
        // The phone card opens closed with its summary and Details; Follow
        // is the map bar's.
        const card = await tab.evaluate(() => ({
          follow: !!document.querySelector(
            '.fleet-map-controls .fleet-map-inspector__follow',
          )?.offsetParent,
          details: !!document.querySelector('.fleet-map-mobile-summary__toggle')
            ?.offsetParent,
          hidden:
            getComputedStyle(document.querySelector('#fleet-map-details'))
              .display === 'none',
        }));
        check(
          card.follow && card.details && card.hidden,
          'narrow: the card opens closed with Details and Follow in reach',
          card,
        );
        await shot(tab, 'narrow-card-closed');
        await tab.locator('.fleet-map-mobile-summary__toggle').click();
        await tab.waitForTimeout(400);
        await shot(tab, 'narrow-card-open');

        // The map's own buttons that a phone keeps, and the layers in the
        // Filters drawer.
        const inView = async selector => {
          const box = await tab.locator(selector).first().boundingBox();
          return (
            !!box &&
            box.width > 0 &&
            box.x >= 0 &&
            box.x + box.width <= width &&
            box.y >= 0 &&
            box.y + box.height <= height
          );
        };
        const kept = {
          fleet: await inView('button[aria-label="Show the whole fleet"]'),
          route: await inView('button[aria-label="Fit the selected route"]'),
        };
        await tab.locator('.fleet-map-mobile-filters').click();
        await tab.waitForTimeout(300);
        kept.layers = await inView('.fleet-map-toggle[title="Fuel Stations"]');
        await shot(tab, 'narrow-filters-layers');
        await tab.locator('.fleet-map-mobile-filters').click();
        check(
          kept.fleet && kept.route && kept.layers,
          'narrow: Whole fleet, Fit route and the layers stay in reach',
          kept,
        );
        const before = await tab.evaluate(() => window.camera());
        await tab.locator('button[aria-label="Show the whole fleet"]').click();
        await tab.waitForTimeout(1500);
        const fleetView = await tab.evaluate(() => window.camera());
        check(
          fleetView.zoom < before.zoom || fleetView.lat !== before.lat,
          'narrow: Whole fleet moves the camera',
          { before, fleetView },
        );

        // The Route / Fuel tabs on the phone's card.
        const tabs = tab.locator('.fleet-map-inspector__tabs [role="tab"]');
        await tabs.nth(1).click();
        await tab.waitForTimeout(800);
        const fuelMode = await tab
          .locator('.fleet-map-inspector')
          .getAttribute('data-inspector-mode');
        await shot(tab, 'narrow-fuel-tab');
        await tab
          .locator('.fleet-map-inspector__tabs [role="tab"]')
          .nth(0)
          .click();
        await tab.waitForTimeout(800);
        const routeMode = await tab
          .locator('.fleet-map-inspector')
          .getAttribute('data-inspector-mode');
        check(
          fuelMode === 'fuelplan' &&
            routeMode === 'truck' &&
            (await inView('.fleet-map-inspector__tabs')),
          'narrow: the Fuel and Route tabs switch the card',
          { fuelMode, routeMode },
        );

        // Follow, a drag that ends it, and Follow again, at 360 px.
        const quick = tab.locator(
          '.fleet-map-controls .fleet-map-inspector__follow',
        );
        await quick.click();
        await tab.waitForTimeout(2500);
        const followed = await quick.getAttribute('aria-pressed');
        const map = await tab.locator('#fleet-map').boundingBox();
        const x = map.x + map.width / 2;
        const y = map.y + map.height - 60;
        await tab.mouse.move(x, y);
        await tab.mouse.down();
        await tab.mouse.move(x + 80, y, { steps: 8 });
        await tab.mouse.up();
        await tab.waitForTimeout(600);
        const dragged = await quick.getAttribute('aria-pressed');
        await quick.click();
        await tab.waitForTimeout(2000);
        const resumed = await quick.getAttribute('aria-pressed');
        check(
          followed === 'true' && dragged === 'false' && resumed === 'true',
          'narrow: Follow, a drag ends it, Follow resumes',
          { followed, dragged, resumed },
        );
        await shot(tab, 'narrow-follow-resumed');
      }
      await context.close();
    }

    // The fuel plan, its editor, the send window, the camera and the route
    // options in the workspace layout. Nothing is saved or sent: writes are
    // blocked, the camera start is refused, and no send or save is pressed.
    for (const theme of ['light', 'dark'])
      for (const [size, width, height] of [
        ['desktop', 1440, 900],
        ['phone', 390, 844],
        ['narrow', 360, 780],
      ]) {
        const { context, tab } = await open({
          theme,
          width,
          height,
        });
        const name = `editors-${theme}-${size}`;
        const fresh = async () => {
          await tab.goto(`${origin}/fleet/map?truckId=${first.truckId}`);
          await tab.locator('.fleet-map-inspector__desktop-title').waitFor({
            state: 'attached',
          });
          await tab.waitForTimeout(3500);
          // A phone's card opens closed; its actions are behind Details.
          const details = tab.locator('.fleet-map-mobile-summary__toggle');
          if (await details.isVisible()) await details.click();
          await tab.locator('button[aria-label="Fuel"]').waitFor();
        };
        const inView = async (selector, label) => {
          const box = await tab.locator(selector).first().boundingBox();
          const over = await tab.evaluate(
            () =>
              document.documentElement.scrollWidth -
              document.documentElement.clientWidth,
          );
          check(
            box &&
              box.width > 0 &&
              box.x >= -1 &&
              box.x + box.width <= width + 1 &&
              over <= 0,
            `${name}: ${label} is shown within the screen`,
            { box, over },
          );
          return box;
        };
        await fresh();
        await tab.locator('button[aria-label="Fuel"]').click();
        await tab.waitForTimeout(800);
        await inView('.fleet-map-inspector', 'the fuel plan card');
        await shot(tab, `${name}-fuel-plan`);
        const edit = tab.locator('.fleet-map-fuel-panel__view');
        check(await edit.isEnabled(), `${name}: Edit plan is offered`);
        await edit.click();
        await tab.locator('.fuel-plan-editor').waitFor({ timeout: 10000 });
        await tab.waitForTimeout(800);
        const editor = await inView('.fuel-plan-editor', 'the fuel editor');
        if (size === 'desktop') {
          const map = await tab.locator('#fleet-map').boundingBox();
          check(
            editor.x >= map.x + map.width - 1,
            `${name}: the fuel editor stands in the card's column`,
            { editor, map },
          );
        }
        await shot(tab, `${name}-fuel-editor`);

        await fresh();
        await tab.locator('button[aria-label="Fuel"]').click();
        await tab.waitForTimeout(800);
        await tab.getByRole('button', { name: 'Send fuel plan' }).click();
        await tab.locator('.fuel-send-plan').waitFor({ timeout: 10000 });
        await tab.waitForTimeout(800);
        await inView('.fuel-send-plan', 'the send window');
        await shot(tab, `${name}-send-plan`);

        await fresh();
        await tab.getByRole('button', { name: 'Camera', exact: true }).click();
        await tab.locator('dialog.truck-camera[open]').waitFor({
          timeout: 10000,
        });
        await tab.waitForTimeout(800);
        await inView('dialog.truck-camera[open]', 'the camera');
        await shot(tab, `${name}-camera`);

        await fresh();
        await tab.locator('button[aria-label="Route options"]').click();
        await tab.locator('.route-editor').waitFor({ timeout: 10000 });
        await tab.waitForTimeout(800);
        await inView('.route-editor', 'the route options');
        await shot(tab, `${name}-route-options`);
        await context.close();
      }

    // Dispatch keeps its views, search and scope.
    {
      const { context, tab } = await open({
        theme: 'light',
        width: 1440,
        height: 900,
      });
      await tab.goto(`${origin}/dispatch?q=11006`);
      await tab.locator('article.dispatch-truck').first().waitFor();
      await tab.waitForTimeout(600);
      const boardPhases = (
        await tab.locator('.dispatch-load__phase').allTextContents()
      ).map(x => x.trim());
      check(
        JSON.stringify(boardPhases) ===
          JSON.stringify(['Current', 'Needs refresh']),
        'Dispatch names the same server phases as the Fleet chain',
        boardPhases,
      );
      const shown = async () => ({
        search: await tab.locator('#dispatch-search').inputValue(),
        active: await tab
          .locator('#dispatch-active')
          .getAttribute('aria-pressed'),
      });
      const start = await shown();
      for (const view of ['Table', 'Papers', 'Cards']) {
        await tab.locator('.dispatch-view button', { hasText: view }).click();
        await tab.waitForTimeout(600);
        const now = await shown();
        const pressed = await tab
          .locator('.dispatch-view button', { hasText: view })
          .getAttribute('aria-pressed');
        check(
          pressed === 'true' &&
            now.search === start.search &&
            now.active === start.active,
          `Dispatch ${view} keeps the search and scope`,
          { start, now },
        );
      }
      // Completed is read in the Table alone (owner, September 27).
      await tab.locator('.dispatch-view button', { hasText: 'Table' }).click();
      await tab.waitForTimeout(400);
      await tab.locator('#dispatch-completed').click();
      await tab.waitForTimeout(600);
      check(
        (await tab
          .locator('#dispatch-completed')
          .getAttribute('aria-pressed')) === 'true',
        'Dispatch Completed scope still opens',
      );
      await context.close();
    }
  }
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'workspace-report.json'),
    JSON.stringify(report, null, 2),
  );
}
console.log(
  JSON.stringify(
    {
      output,
      errors: report.errors,
      unexpected: [...new Set(report.unexpectedRequests)],
    },
    null,
    2,
  ),
);
if (report.errors.length) process.exitCode = 1;
