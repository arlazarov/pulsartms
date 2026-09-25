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

// Every call the page makes on the map is recorded; focusing a truck
// succeeds. The page's callbacks are kept so the test can report the camera
// coming to rest, as the real map does on 'idle'.
const mapStub = `export async function createFleetMap(element, _key, callbacks) {
  element.dataset.returnFixture = 'offline-map';
  window.mapCalls = [];
  window.mapCallbacks = callbacks;
  return new Proxy({}, {
    get(_target, name) {
      if (name === 'then') return undefined;
      return (...args) => {
        window.mapCalls.push([String(name), JSON.parse(JSON.stringify(args ?? []))]);
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

  // The truck's card stays hidden here (no route is served), so the link
  // is followed by a script click, which Blazor routes like a pointer one.
  const open = tab.locator("a[aria-label='Route & load details']");
  await open.waitFor({ state: 'attached' });
  await tab.waitForFunction(() =>
    document
      .querySelector("a[aria-label='Route & load details']")
      ?.getAttribute('href')
      ?.includes('44.1%252C-78.2%252C9'),
  );
  const openHref = await open.getAttribute('href');
  check(
    openHref.startsWith(`/dispatch/${current}?from=`) &&
      new URL(openHref, origin).searchParams
        .get('from')
        .includes('view=44.1%2C-78.2%2C9'),
    'Open load carries the map address',
    openHref,
  );
  await open.evaluate(element => element.click());
  link = await back('Back to map');
  await link.click();
  await tab.locator('[data-return-fixture]').waitFor();
  await tab.waitForFunction(() =>
    (window.mapCalls ?? []).some(([name]) => name === 'selectNextStop'),
  );
  const again = await tab.evaluate(() => window.mapCalls);
  check(
    JSON.stringify(
      again.find(([name]) => name === 'setOptions')?.[1][0]?.initialView,
    ) === JSON.stringify({ latitude: 44.1, longitude: -78.2, zoom: 9 }),
    'Back to map restores the moved camera',
    again.find(([name]) => name === 'setOptions')?.[1][0]?.initialView,
  );
  check(
    JSON.stringify(again.find(([name]) => name === 'selectNextStop')?.[1]) ===
      JSON.stringify([next, 1, null]),
    'Back to map reopens the next load stop',
    again.find(([name]) => name === 'selectNextStop')?.[1],
  );
  check(
    (await tab.locator('#fleet-truck-search').inputValue()) === '110',
    'Back to map keeps the search',
    await tab.locator('#fleet-truck-search').inputValue(),
  );
  await tab.screenshot({ path: resolve(output, 'map-returned.png') });

  // Browser Back from the load to the map.
  await tab
    .locator("a[aria-label='Route & load details']")
    .evaluate(element => element.click());
  await back('Back to map');
  await tab.goBack();
  await tab.locator('[data-return-fixture]').waitFor();
  await tab.waitForFunction(() =>
    (window.mapCalls ?? []).some(([name]) => name === 'setOptions'),
  );
  const viaBack = await tab.evaluate(() => window.mapCalls);
  check(
    JSON.stringify(
      viaBack.find(([name]) => name === 'setOptions')?.[1][0]?.initialView,
    ) === JSON.stringify({ latitude: 44.1, longitude: -78.2, zoom: 9 }),
    'browser Back restores the map camera',
    await here(),
  );

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
