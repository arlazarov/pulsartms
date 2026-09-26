// Returning from a load, in the compiled Client: the Dispatch list comes
// back with its scope, search, page and scroll, and the Fleet Map with its
// truck, load, next load's stop, camera and search - through the load
// page's Back link and through browser Back. APIs are synthetic and
// read-only; the map provider is a stub that records what the page asks
// of it and reports one camera move. No live account, database, provider
// or write is involved.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui', process.env.RETURN_TEST_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const user = '11111111-1111-1111-1111-111111111111';
const guid = n => `5a0e5c1e-7d5b-4a61-9d7e-${String(n).padStart(12, '0')}`;
const success = response => ({ success: true, response, errors: [] });
const trucks = Array.from({ length: 12 }, (_, i) => ({
  truckId: guid(100 + i),
  unitNumber: String(11000 + i),
  driverName: `Fixture Driver ${i}`,
  trailerNumber: `TR-${i}`,
  latitude: 41 + i / 10,
  longitude: -87,
  speed: 0,
  heading: 0,
  updatedAt: new Date().toISOString(),
  engineState: 'Off',
}));
const load = (truck, i, completed) => ({
  id: guid(200 + i),
  truckId: truck.truckId,
  loadNumber: 1400 + i,
  orderNumber: `ORD-${i}`,
  status: completed ? 'completed' : 'planned',
  completed,
  customerName: `Fixture Customer ${i}`,
  truckNumber: truck.unitNumber,
  driverName: truck.driverName,
  trailerNumber: truck.trailerNumber,
  stops: [],
  eta: null,
});
const page = (items, number) => ({
  items,
  page: number,
  pageSize: items.length,
  totalCount: items.length * 3,
  totalPages: 3,
  hasPreviousPage: number > 1,
  hasNextPage: number < 3,
});
const board = number =>
  success(
    page(
      trucks.map((truck, i) => ({
        key: truck.truckId,
        truckId: truck.truckId,
        truckNumber: truck.unitNumber,
        driverName: truck.driverName,
        trailerNumber: truck.trailerNumber,
        speed: 0,
        engineState: 'Off',
        dispatches: [load(truck, i, false)],
      })),
      number,
    ),
  );
const completed = number =>
  success(
    page(
      trucks.map((truck, i) => load(truck, i, true)),
      number,
    ),
  );
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
  ['/api/fleet/locations', success({ trucks, points: trucks })],
  ['/api/fleet/hos', success({})],
  ['/api/fleet/planning/previews', success([])],
  ['/api/fuel/price-overview', success([])],
  ['/api/dispatch/board/telemetry', success([])],
  ['/api/dispatch/board/enrichment', success([])],
]);

// The first truck's work, so its card and its next load's stop draw: the
// current load on its road, and the next load with two stops.
const now = new Date().toISOString();
const point = (latitude, longitude) => ({ latitude, longitude });
const [first] = trucks;
const currentLoad = guid(200);
const nextLoad = guid(201);
const stop = (n, sequence, job, name, city, latitude, longitude) => ({
  id: guid(300 + n),
  sequence,
  job,
  name,
  city,
  province: 'ON',
  country: 'Canada',
  address: `${100 + n} Fixture Road, ${city}, ON, Canada`,
  scheduledDate: '2026-09-28',
  scheduledTime: '10:00:00',
  latitude,
  longitude,
});
const stops = [
  stop(
    0,
    1,
    'Delivery',
    'Current receiving facility',
    'Toronto',
    43.65,
    -79.38,
  ),
  stop(1, 1, 'Pickup', 'East logistics terminal', 'Kingston', 44.23, -76.48),
  stop(2, 2, 'Delivery', 'Capital distribution centre', 'Ottawa', 45.42, -75.7),
];
const loadOf = (id, number, status, of) => ({
  ...load(first, 0, false),
  id,
  loadNumber: number,
  status,
  orderNumber: `ORD-${number}`,
  stops: of,
});
const loads = [
  loadOf(currentLoad, 1400, 'in_transit', stops.slice(0, 1)),
  loadOf(nextLoad, 1401, 'planned', stops.slice(1)),
];
const routePlanning = success({
  truckId: first.truckId,
  dispatchId: currentLoad,
  loadNumber: 1400,
  state: {
    profile: {},
    apiConfigured: false,
    fuelPercent: 75,
    fuelUpdatedAt: now,
    eta: null,
    plan: {
      id: guid(400),
      dispatchId: currentLoad,
      truckId: first.truckId,
      version: 1,
      calculatedAt: now,
      originalPlannedMiles: 500,
      fromCurrentPosition: true,
      profile: {},
      fuelPlan: null,
      stops: [{ ...stops[0], point: point(43.65, -79.38) }],
      tracking: {
        nextStopId: stops[0].id,
        passedStopIds: [],
        visitedStops: {},
      },
      route: {
        miles: 500,
        seconds: 30_000,
        warnings: [],
        points: [],
        legs: [
          {
            miles: 500,
            seconds: 30_000,
            points: [point(41, -87), point(43.65, -79.38)],
          },
        ],
      },
    },
    progress: {
      progressMiles: 380,
      remainingMiles: 120,
      remainingSeconds: 7200,
      distanceFromRouteMiles: 0,
      offRoute: false,
      locationStale: false,
      locationTime: now,
      position: point(41, -87),
    },
  },
});
const nextRoutes = success({
  revision: 'fixture-v1',
  unchanged: false,
  routes: [
    {
      id: nextLoad,
      loadNumber: 1401,
      status: 'planned',
      stopCount: 2,
      stops: stops.slice(1).map(({ id, latitude, longitude, job, name }) => ({
        id,
        latitude,
        longitude,
        job,
        name,
      })),
      deadhead: {
        miles: 40,
        points: [point(43.65, -79.38), point(44.23, -76.48)],
      },
      legs: [
        {
          miles: 300,
          seconds: 18_000,
          points: [point(44.23, -76.48), point(45.42, -75.7)],
        },
      ],
    },
  ],
});
const truckWork = new Map([
  [`/api/dispatch/${currentLoad}`, success(loads[0])],
  [`/api/dispatch/${nextLoad}`, success(loads[1])],
  [`/api/dispatch/truck/${first.truckId}`, success(loads)],
  [`/api/dispatch/truck/${first.truckId}/next-routes`, nextRoutes],
  [
    `/api/fleet/trucks/${first.truckId}/weather`,
    success({
      celsius: 22.5,
      condition: 'CLEAR',
      description: 'Clear',
      isDaytime: true,
      updatedAt: now,
    }),
  ],
  ...[currentLoad, nextLoad].map(id => [
    `/api/dispatch/${id}/planning/map`,
    success({ dispatchId: id, segments: [], missingSections: 0 }),
  ]),
]);

// The map provider, without the Google SDK. Every call the page makes is
// recorded; focusing a truck succeeds. A next load's stop asked for by the
// page is reported back as selected once the page has sent that truck's
// next loads, as the real next-loads layer does when their stops are drawn
// - there is no marker to press. The page's callbacks are kept so the test
// can also report the camera coming to rest, as the real map does on
// 'idle'.
const mapStub = `export async function createFleetMap(element, _key, callbacks) {
  element.dataset.returnFixture = 'offline-map';
  window.mapCalls = [];
  window.mapCallbacks = callbacks;
  let pending = null;
  return new Proxy({}, {
    get(_target, name) {
      if (name === 'then') return undefined;
      return (...args) => {
        window.mapCalls.push([String(name), JSON.parse(JSON.stringify(args ?? []))]);
        if (name === 'selectNextStop') pending = args;
        if (name === 'setNextLoadsBytes' && pending) {
          const [load, index] = pending;
          pending = null;
          const address = new URL(location.href).searchParams;
          setTimeout(() => callbacks.invokeMethodAsync('OnNextLoadSelected',
            address.get('truckId'), address.get('dispatchId'), load, index));
        }
        return name === 'focusTruck' ? true : undefined;
      };
    },
  });
}`;
const integrity =
  'sha256-' + createHash('sha256').update(mapStub).digest('base64');
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g,
  (_all, open, json, close) => {
    const map = JSON.parse(json);
    for (const name of Object.keys(map.integrity ?? {}))
      if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(name))
        map.integrity[name] = integrity;
    return open + JSON.stringify(map) + close;
  },
);
const report = {
  artifact,
  scope:
    'Compiled Client with synthetic read-only APIs and a recording map ' +
    'stub. No live account, database, provider calls or writes.',
  checks: [],
  boardReads: [],
  errors: [],
  unexpectedRequests: [],
};
const check = (condition, message, detail) => {
  report.checks.push({ ok: !!condition, message, detail });
  if (!condition) report.errors.push(`${message} ${JSON.stringify(detail)}`);
};

const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 900 },
  serviceWorkers: 'block',
});
await context.addInitScript(user => {
  localStorage.setItem(
    'auth_session',
    JSON.stringify({ Id: user, AccessToken: 'fixture', RefreshToken: 'x' }),
  );
  // A reader who had a next load's stop open had Next loads on; the map
  // keeps that choice per user.
  localStorage.setItem(
    `pulsartms.fleet-map.preferences.${user}`,
    JSON.stringify({ showNextLoads: true, showTraffic: false }),
  );
}, user);
await installReleaseArtifact(context, artifact, origin);
await context.route('**/*', async route => {
  const request = route.request();
  const url = new URL(request.url());
  const planning =
    request.method() === 'POST' &&
    url.pathname === '/api/dispatch/board/planning';
  // Reading a truck's route is a POST that writes nothing.
  const readsRoute =
    request.method() === 'POST' &&
    (url.pathname === `/api/dispatch/${currentLoad}/planning/automatic` ||
      url.pathname === `/api/fleet/trucks/${first.truckId}/planning`);
  if (readsRoute) return route.fulfill({ json: routePlanning });
  if (
    url.origin !== origin ||
    (!['GET', 'HEAD'].includes(request.method()) && !planning)
  ) {
    report.unexpectedRequests.push(`${request.method()} ${url}`);
    return route.abort('blockedbyclient');
  }
  if (url.pathname.startsWith('/api/')) {
    const number = Number(url.searchParams.get('page') ?? 1);
    if (url.pathname === '/api/dispatch/board') {
      report.boardReads.push(url.search);
      return route.fulfill({ json: board(number) });
    }
    if (url.pathname === '/api/dispatch') {
      report.boardReads.push(url.search);
      return route.fulfill({ json: completed(number) });
    }
    if (planning) return route.fulfill({ json: success([]) });
    if (truckWork.has(url.pathname))
      return route.fulfill({ json: truckWork.get(url.pathname) });
    if (url.pathname === '/api/settings/appearance')
      return route.fulfill({ json: success({ theme: 'light' }) });
    const fixture = fixtures.get(url.pathname);
    if (fixture) return route.fulfill({ json: fixture });
    // Reads this check does not need (a load's workspace, a route plan):
    // answered as not found, and listed for the report.
    report.unexpectedRequests.push(`${request.method()} ${url.pathname}`);
    return route.fulfill({ status: 404, json: success(null) });
  }
  if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(url.pathname))
    return route.fulfill({
      contentType: 'text/javascript',
      body: mapStub,
    });
  if (request.isNavigationRequest())
    return route.fulfill({ contentType: 'text/html', body: html });
  return route.fallback();
});

const tab = await context.newPage();
tab.on('pageerror', error => report.errors.push(`page: ${error.message}`));
const here = () => tab.evaluate(() => location.pathname + location.search);
const back = async label => {
  const link = tab.locator('.dispatch-details__navigation > a');
  await link.waitFor();
  check(
    (await link.textContent()).trim() === `← ${label}`,
    'the load page names where it returns',
    await link.textContent(),
  );
  return link;
};

try {
  // Dispatch: arrive with a place in the address.
  const place = '/dispatch?scope=completed&q=AMF&page=2';
  await tab.goto(origin + place);
  await tab.locator('section.dispatch-load').first().waitFor();
  const listState = async () => ({
    address: await here(),
    completed: await tab
      .locator('#dispatch-completed')
      .getAttribute('aria-pressed'),
    search: await tab.locator('#dispatch-search').inputValue(),
    read: report.boardReads.at(-1),
  });
  let state = await listState();
  check(state.completed === 'true', 'Dispatch opens on Completed', state);
  check(state.search === 'AMF', 'Dispatch opens with its search', state);
  check(
    /page=2/.test(state.read) && /search=AMF/.test(state.read),
    'Dispatch reads the page and search it was opened with',
    state,
  );
  await tab.evaluate(() => window.scrollTo(0, 600));
  await tab.waitForTimeout(200);
  const scrolled = await tab.evaluate(() => window.scrollY);
  const details = tab.locator('section.dispatch-load a.dispatch-load__details');
  const href = await details.first().getAttribute('href');
  check(
    href.includes(`from=${encodeURIComponent(place)}`),
    'a card carries the list address',
    href,
  );

  // Back link.
  await details.first().click();
  let link = await back('Back to Dispatch');
  check(
    (await link.getAttribute('href')) === place,
    'Back to Dispatch goes to that address',
    await link.getAttribute('href'),
  );
  await link.click();
  await tab.locator('section.dispatch-load').first().waitFor();
  await tab.waitForFunction(y => Math.abs(window.scrollY - y) <= 2, scrolled);
  state = await listState();
  check(state.address === place, 'the list is back at its address', state);
  check(
    state.completed === 'true' && state.search === 'AMF',
    'the list is back on Completed with its search',
    state,
  );
  check(/page=2/.test(state.read), 'the list reads page 2 again', state.read);
  await tab.screenshot({ path: resolve(output, 'dispatch-returned.png') });

  // Browser Back.
  await tab
    .locator('section.dispatch-load a.dispatch-load__details')
    .first()
    .click();
  await back('Back to Dispatch');
  await tab.goBack();
  await tab.locator('section.dispatch-load').first().waitFor();
  await tab.waitForFunction(y => Math.abs(window.scrollY - y) <= 2, scrolled);
  state = await listState();
  check(
    state.address === place && state.completed === 'true',
    'browser Back returns to the same place and scroll',
    state,
  );

  // A change made on the list is written into its own address.
  await tab.locator('#dispatch-active').click();
  await tab.waitForFunction(() => !location.search.includes('scope='));
  check(
    !(await here()).includes('scope='),
    'choosing Active rewrites the list address',
    await here(),
  );

  // Fleet Map: arrive with truck, load, next stop, camera and search.
  const truck = trucks[0].truckId;
  const current = guid(200);
  const next = guid(201);
  const map =
    `/fleet/map?truckId=${truck}&dispatchId=${current}` +
    `&nextLoadId=${next}&nextStop=1&view=43.65,-79.38,11&q=110`;
  await tab.goto(origin + map);
  await tab.locator('[data-return-fixture]').waitFor();
  await tab.waitForFunction(() =>
    (window.mapCalls ?? []).some(([name]) => name === 'selectNextStop'),
  );
  const calls = await tab.evaluate(() => window.mapCalls);
  const options = calls.find(([name]) => name === 'setOptions')?.[1][0];
  check(
    JSON.stringify(options?.initialView) ===
      JSON.stringify({ latitude: 43.65, longitude: -79.38, zoom: 11 }),
    'the map is given the camera from the address',
    options?.initialView,
  );
  const selected = calls.find(([name]) => name === 'selectNextStop')?.[1];
  check(
    JSON.stringify(selected) === JSON.stringify([next, 1, null]),
    'the map is asked for the next load stop from the address',
    selected,
  );
  check(
    (await tab.locator('#fleet-truck-search').inputValue()) === '110',
    'the truck search comes back',
    await tab.locator('#fleet-truck-search').inputValue(),
  );

  // The camera comes to rest somewhere else: the address follows.
  await tab.evaluate(() =>
    window.mapCallbacks.invokeMethodAsync('OnMapViewChanged', 44.1, -78.2, 9),
  );
  await tab.waitForFunction(() =>
    location.search.includes('view=44.1%2C-78.2%2C9'),
  );
  const moved = await here();
  check(
    moved.includes(`truckId=${truck}`) &&
      moved.includes(`nextLoadId=${next}`) &&
      moved.includes('q=110'),
    'the map address keeps truck, next stop and search with the new camera',
    moved,
  );

  // The next load's stop is on screen as it was left: its card, titled,
  // with the stop the address names (the second of that load).
  const card = tab.locator('.fleet-map-next-load-card');
  const header = tab.locator('.fleet-map-inspector__header');
  const stopShown = async label => {
    await card.waitFor({ state: 'visible' });
    const text = await card.innerText();
    check(
      text.includes('Capital distribution centre') && text.includes('Delivery'),
      `${label}: the next load's stop is on screen`,
      text.slice(0, 160),
    );
    check(
      (await header.isVisible()) &&
        (await header.innerText()).includes('Next load stop'),
      `${label}: the card is the next load stop`,
      await header.innerText(),
    );
  };
  await stopShown('arrival');
  const stopLink = card.locator('a.fleet-route-popup__details-link');
  const detailsFrom = new URL(
    await stopLink.getAttribute('href'),
    origin,
  ).searchParams.get('from');
  check(
    (await stopLink.getAttribute('href')).startsWith(`/dispatch/${next}?`) &&
      detailsFrom.includes(`nextLoadId=${next}`) &&
      detailsFrom.includes('nextStop=1') &&
      detailsFrom.includes('view=44.1%2C-78.2%2C9'),
    "the stop's load link carries the map with that stop open",
    detailsFrom,
  );

  // Pressed as a reader would, on the visible link.
  await stopLink.click();
  link = await back('Back to map');
  check(await link.isVisible(), 'Back to map is visible', null);
  await link.click();
  await tab.locator('[data-return-fixture]').waitFor();
  await stopShown('Back to map');
  const again = await tab.evaluate(() => window.mapCalls);
  check(
    JSON.stringify(
      again.find(([name]) => name === 'setOptions')?.[1][0]?.initialView,
    ) === JSON.stringify({ latitude: 44.1, longitude: -78.2, zoom: 9 }),
    'Back to map restores the moved camera',
    again.find(([name]) => name === 'setOptions')?.[1][0]?.initialView,
  );
  check(
    (await tab.locator('#fleet-truck-search').inputValue()) === '110',
    'Back to map keeps the search',
    await tab.locator('#fleet-truck-search').inputValue(),
  );
  await tab.screenshot({ path: resolve(output, 'map-returned.png') });

  // Browser Back from the load to the map.
  await card.locator('a.fleet-route-popup__details-link').click();
  await back('Back to map');
  await tab.goBack();
  await tab.locator('[data-return-fixture]').waitFor();
  await stopShown('browser Back');
  const viaBack = await tab.evaluate(() => window.mapCalls);
  check(
    JSON.stringify(
      viaBack.find(([name]) => name === 'setOptions')?.[1][0]?.initialView,
    ) === JSON.stringify({ latitude: 44.1, longitude: -78.2, zoom: 9 }),
    'browser Back restores the map camera',
    await here(),
  );

  // Back to the truck: its own card, and Open load on it, visible.
  // The truck card opens collapsed; its actions are behind its own
  // chevron, which a reader presses first.
  const expand = async () => {
    const toggle = tab.locator('.fleet-map-mobile-summary__toggle');
    await toggle.waitFor({ state: 'visible' });
    if ((await toggle.getAttribute('aria-expanded')) !== 'true')
      await toggle.click();
  };
  await tab.locator('.fleet-map-inspector__back').click();
  await expand();
  const open = tab.locator('.fleet-map-inspector__load-link');
  await open.waitFor({ state: 'visible' });
  const openFrom = new URL(
    await open.getAttribute('href'),
    origin,
  ).searchParams.get('from');
  check(
    (await open.getAttribute('href')).startsWith(`/dispatch/${current}?`) &&
      !openFrom.includes('nextLoadId') &&
      openFrom.includes(`truckId=${truck}`),
    'Open load returns to the truck, the next stop closed',
    openFrom,
  );
  await open.click();
  link = await back('Back to map');
  await link.click();
  await expand();
  await open.waitFor({ state: 'visible' });
  check(
    !(await card.isVisible()),
    'Back to map shows the truck card, not the closed stop',
    null,
  );
  await tab.screenshot({ path: resolve(output, 'map-truck-returned.png') });

  // A crafted return address goes nowhere but Dispatch.
  await tab.goto(
    `${origin}/dispatch/${current}?from=${encodeURIComponent('https://example.com/')}`,
  );
  link = await back('Back to Dispatch');
  check(
    (await link.getAttribute('href')) === '/dispatch',
    'a crafted return address goes to Dispatch',
    await link.getAttribute('href'),
  );
} catch (error) {
  report.errors.push(error.stack ?? String(error));
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  if (report.errors.length) process.exitCode = 1;
  console.log(
    JSON.stringify(
      {
        checks: report.checks.length,
        failed: report.checks.filter(x => !x.ok).map(x => x.message),
        errors: report.errors,
        unexpectedRequests: [...new Set(report.unexpectedRequests)],
        output,
      },
      null,
      2,
    ),
  );
}
