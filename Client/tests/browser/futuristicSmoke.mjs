// The Futuristic interface in the compiled Client, against the current
// interface: Fleet Map and Dispatch in both themes at desktop and phone
// widths, drawn on the provider's real basemap with the browser key from
// wwwroot/appsettings.json (never written to the report). It also checks
// that the Futuristic Fleet Map keeps its behaviour: list and chain
// selection reach the inspector, Follow holds through position updates, the
// map turns to satellite at close zoom and back to the road map when
// zoomed out, and Dispatch keeps Cards, Table, Papers, search and scope.
// APIs are synthetic and read-only; no account, database or write is used.
//
// MAP_TEST_ARTIFACT_DIR=/abs/publish/wwwroot \
//   node tests/browser/futuristicSmoke.mjs
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui', process.env.FUTURISTIC_TEST_OUTPUT_DIR);
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
const plans = [
  ['54777', ['nashville', 'knoxville', 'richmond', 'baltimore'], 52],
  ['11006', ['chicago', 'detroit', 'columbus'], 61],
  ['11014', ['atlanta', 'charlotte', 'richmond'], 58],
  ['11018', ['pittsburgh', 'columbus'], 0],
  ['11022', ['albany', 'boston', 'albany'], 55],
  ['11027', ['charlotte', 'atlanta'], 0],
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
const point = (latitude, longitude) => ({ latitude, longitude });
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
        fuelPlan: null,
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
const board = (number, search) => {
  const q = (search ?? '').toLowerCase();
  const items = rows.filter(
    r =>
      !q ||
      [r.truckNumber, r.driverName, ...r.dispatches.map(d => d.loadNumber)]
        .join(' ')
        .toLowerCase()
        .includes(q),
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
      return move.apply(this, args);
    };
    clearInterval(hook);
  }, 20);
};

async function open({ theme, face, width, height }) {
  const context = await browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: 1,
    serviceWorkers: 'block',
  });
  await context.addInitScript(
    ({ user, face }) => {
      localStorage.setItem(
        'auth_session',
        JSON.stringify({ Id: user, AccessToken: 'fixture', RefreshToken: 'x' }),
      );
      localStorage.setItem(
        `pulsartms.fleet-map.preferences.${user}`,
        JSON.stringify({ showNextLoads: true, showTraffic: false }),
      );
      localStorage.setItem(`pulsr.interface.${user}`, face);
    },
    { user, face },
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
        /^\/api\/dispatch\/[^/]+\/planning\/automatic$/.test(url.pathname));
    if (
      url.origin !== origin ||
      (!['GET', 'HEAD'].includes(request.method()) && !readPost)
    ) {
      report.unexpectedRequests.push(`${request.method()} ${redact(url)}`);
      return route.abort('blockedbyclient');
    }
    if (url.pathname === '/appsettings.json')
      return route.fulfill({ contentType: 'application/json', body: appsettings });
    if (url.pathname.startsWith('/api/')) {
      const path = url.pathname;
      let m;
      if (path === '/api/settings/appearance')
        return route.fulfill({ json: success({ theme }) });
      if (path === '/api/fleet/locations') {
        // The first truck is moving: its recent positions, ten seconds
        // apart, advance east. The map plays positions back 90 s behind the
        // reports, so the history must cover that window.
        const now = Date.now();
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
          json: success({ trucks: list, points: [...history, ...trucks.slice(1)] }),
        });
      }
      if (path === '/api/fleet/hos') return route.fulfill({ json: success({}) });
      if (path === '/api/dispatch/board')
        return route.fulfill({
          json: board(
            Number(url.searchParams.get('page') ?? 1),
            url.searchParams.get('search'),
          ),
        });
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
      !/favicon|Failed to load resource/.test(message.text())
    )
      report.errors.push(`console: ${redact(message.text()).slice(0, 300)}`);
  });
  return { context, tab };
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
  // Screens: both interfaces, both themes, desktop and phone.
  for (const face of ['futuristic', 'current'])
    for (const theme of ['light', 'dark'])
      for (const [size, width, height] of [
        ['desktop', 1440, 900],
        ['phone', 390, 844],
      ]) {
        const { context, tab } = await open({ theme, face, width, height });
        const name = `${face}-${theme}-${size}`;
        await tab.goto(`${origin}/fleet/map?truckId=${first.truckId}`);
        await tab.locator('#fleet-map').waitFor();
        await tab.waitForTimeout(6000);
        const faceOn = await tab.evaluate(
          () => document.documentElement.dataset.interface ?? 'current',
        );
        check(faceOn === face, `${name}: interface applied`, { faceOn });
        await noOverflow(tab, `${name} fleet`);
        await shot(tab, `${name}-fleet`);
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

  // Behaviour in the Futuristic Fleet Map.
  {
    const { context, tab } = await open({
      theme: 'dark',
      face: 'futuristic',
      width: 1440,
      height: 900,
    });
    await tab.goto(`${origin}/fleet/map?truckId=${first.truckId}`);
    await tab.locator('.fleet-truck-list__row').first().waitFor();
    await tab.waitForTimeout(5000);
    const rowsShown = await tab.locator('.fleet-truck-list__row').count();
    check(rowsShown === trucks.length, 'the list shows every truck', {
      rowsShown,
    });
    const title = () =>
      tab.locator('.fleet-map-inspector__desktop-title').textContent();
    check((await title()).trim() === first.unitNumber,
      'the addressed truck opens in the inspector', await title());

    // The list selects through the page's own path.
    await tab.locator('.fleet-truck-list__row').nth(1).click();
    await tab.waitForTimeout(1500);
    check((await title()).trim() === trucks[1].unitNumber,
      'choosing a truck in the list opens it in the inspector',
      await title());
    const chainTitle = await tab
      .locator('#fleet-trip-chain-title')
      .textContent();
    check(chainTitle.includes(trucks[1].unitNumber),
      'the chain follows the selection', chainTitle);
    await tab.locator('.fleet-truck-list__row').nth(0).click();
    await tab.waitForTimeout(2500);

    // The chain opens the next load's stop card through the map.
    const links = tab.locator('.fleet-trip-chain__card');
    check((await links.count()) === 3,
      'the chain shows the current load and the next loads',
      await links.count());
    await links.nth(1).click();
    await tab.waitForTimeout(1500);
    const mode = await tab
      .locator('.fleet-map-inspector')
      .getAttribute('data-inspector-mode');
    check(mode === 'nextstop', 'a next load in the chain opens its stop card',
      { mode });
    await links.nth(0).click();
    await tab.waitForTimeout(800);
    const back = await tab
      .locator('.fleet-map-inspector')
      .getAttribute('data-inspector-mode');
    check(back === 'truck', 'the current load returns to the truck card',
      { back });
    await shot(tab, 'behaviour-chain-next-load');

    // Follow: close zoom turns to satellite, and holds through updates.
    const before = await tab.evaluate(() => window.mapTypes.slice());
    await tab.locator('button[aria-label="Follow"]').click();
    await tab.waitForTimeout(3000);
    const following = await tab
      .locator('button[aria-label="Follow"]')
      .getAttribute('aria-pressed');
    const afterFollow = await tab.evaluate(() => window.mapTypes.slice());
    check(following === 'true', 'Follow starts', { following });
    check(afterFollow.at(-1) === 'hybrid',
      'Follow at close zoom shows satellite', { before, afterFollow });
    await shot(tab, 'behaviour-follow-satellite');
    const moves = await tab.evaluate(() => window.cameraMoves);
    await tab.waitForTimeout(22000);
    const still = await tab
      .locator('button[aria-label="Follow"]')
      .getAttribute('aria-pressed');
    const movesAfter = await tab.evaluate(() => window.cameraMoves);
    const typeAfter = await tab.evaluate(() => window.mapTypes.at(-1));
    check(still === 'true' && movesAfter > moves && typeAfter === 'hybrid',
      'Follow keeps the moving truck in view on satellite',
      { still, moves, movesAfter, typeAfter });
    await shot(tab, 'behaviour-follow-after-updates');

    // Zooming out is the reader's camera: Follow ends, the road map returns.
    const box = await tab.locator('#fleet-map').boundingBox();
    await tab.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    for (let i = 0; i < 8; i++) {
      await tab.mouse.wheel(0, 400);
      await tab.waitForTimeout(250);
    }
    await tab.waitForTimeout(2500);
    const ended = await tab
      .locator('button[aria-label="Follow"]')
      .getAttribute('aria-pressed');
    const typeOut = await tab.evaluate(() => window.mapTypes.at(-1));
    check(ended === 'false' && typeOut === 'roadmap',
      'zooming out ends Follow and returns the road map', { ended, typeOut });
    await shot(tab, 'behaviour-zoomed-out-roadmap');

    // Following again returns to satellite.
    await tab.locator('button[aria-label="Follow"]').click();
    await tab.waitForTimeout(2500);
    const again = await tab.evaluate(() => window.mapTypes.at(-1));
    check(again === 'hybrid', 'following again shows satellite', { again });
    await context.close();
  }

  // Dispatch keeps its views, search and scope in the Futuristic interface.
  {
    const { context, tab } = await open({
      theme: 'light',
      face: 'futuristic',
      width: 1440,
      height: 900,
    });
    await tab.goto(`${origin}/dispatch?q=11006`);
    await tab.locator('article.dispatch-truck').first().waitFor();
    await tab.waitForTimeout(600);
    const shown = async () => ({
      search: await tab.locator('#dispatch-search').inputValue(),
      active: await tab.locator('#dispatch-active').getAttribute('aria-pressed'),
    });
    const start = await shown();
    for (const view of ['Table', 'Papers', 'Cards']) {
      await tab.locator('.dispatch-view button', { hasText: view }).click();
      await tab.waitForTimeout(600);
      const now = await shown();
      const pressed = await tab
        .locator('.dispatch-view button', { hasText: view })
        .getAttribute('aria-pressed');
      check(pressed === 'true' && now.search === start.search &&
          now.active === start.active,
        `Dispatch ${view} keeps the search and scope`, { start, now });
    }
    await tab.locator('#dispatch-completed').click();
    await tab.waitForTimeout(600);
    check((await tab.locator('#dispatch-completed').getAttribute(
      'aria-pressed')) === 'true', 'Dispatch Completed scope still opens');
    await context.close();
  }
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'futuristic-report.json'),
    JSON.stringify(report, null, 2),
  );
}
console.log(JSON.stringify({ output, errors: report.errors,
  unexpected: [...new Set(report.unexpectedRequests)] }, null, 2));
if (report.errors.length) process.exitCode = 1;
