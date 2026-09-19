import assert from 'node:assert/strict';
import {browserOutput} from '../../../scripts/artifacts.mjs';
import {mkdir, writeFile, readFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';
import {installReleaseArtifact} from './releaseArtifact.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR, 'MAP_TEST_ARTIFACT_DIR must identify a verified staged wwwroot');
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui', process.env.UI_TEST_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const id = '11111111-1111-1111-1111-111111111111';
const truckId = '22222222-2222-2222-2222-222222222222';
const currentLoadId = '33333333-3333-3333-3333-333333333333';
const futureLoadId = '44444444-4444-4444-4444-444444444444';
const stopIds = [1, 2, 3, 4, 5].map(number => `55555555-5555-5555-5555-${String(number).padStart(12, '0')}`);
const fixtureDay = new Date();
const dateOnly = days => new Date(Date.UTC(fixtureDay.getUTCFullYear(), fixtureDay.getUTCMonth(), fixtureDay.getUTCDate() + days)).toISOString().slice(0, 10);
const stop = (index, sequence, job, name, city, day, hour) => ({id: stopIds[index], sequence, job, name, city,
  province: 'ON', country: 'Canada', scheduledDate: dateOnly(day), scheduledTime: `${hour}:00:00`});
const stops = [
  stop(0, 1, 'Pickup', 'North distribution centre', 'Windsor', 0, '08'),
  stop(1, 2, 'Delivery', 'Intermediate consolidation warehouse', 'London', 0, '10'),
  stop(2, 3, 'Delivery', 'Lakeshore receiving facility', 'Toronto', 0, '16'),
  stop(3, 1, 'Pickup', 'East logistics terminal', 'Kingston', 1, '08'),
  stop(4, 2, 'Delivery', 'Capital distribution centre', 'Ottawa', 1, '18')
];
stops[0].pickedUpAt = `${dateOnly(0)}T08:15:00Z`;
stops[2].isWindow = true;
stops[2].scheduledTime2 = '18:00:00';
for (const [index, value] of stops.entries()) {
  const load = index < 3 ? 1441 : 1442;
  value.notes = `Shipper appointment confirmation number: PU${load}. Receiver appointment confirmation number: DL${load}.`;
  value.stopNo = `${value.job === 'Pickup' ? 'PU' : 'DEL'}-REF${load}`;
}
const stopForecast = (dispatchId, stop, time, lateMinutes) => ({dispatchId, stopId: stop.id,
  arrival: `${stop.scheduledDate}T${time}:00-04:00`, timeZoneId: 'America/Toronto',
  appointment: `${stop.scheduledDate}T${stop.scheduledTime}-04:00`, lateMinutes, drivingMinutes: 120, restMinutes: 0});
const cycleForecast = remainingMinutes => ({remainingMinutes, nextRecapAt: `${dateOnly(4)}T00:00:00-04:00`,
  nextRecapMinutes: 798, homeTimeZoneId: 'America/New_York', recapVerified: true});
const cycleAtCalculation = () => ({...cycleForecast(1400), nextRecapAt: `${dateOnly(2)}T00:00:00-04:00`, nextRecapMinutes: 185});
const recapLabel = new Date(dateOnly(2)).toLocaleDateString('en-US', {month: 'short', day: 'numeric', timeZone: 'UTC'});
const forecast = values => ({calculatedAt: new Date().toISOString(), validUntil: new Date(Date.now() + 120_000).toISOString(),
  stops: values, assumptions: [], unavailableReason: null, cycleAtCalculation: cycleAtCalculation()});
const dispatches = () => [
  {id: currentLoadId, truckId, loadNumber: 1441, orderNumber: 'CURRENT-ORD-1441', status: 'in_transit', customerName: 'Fixture Current Customer',
    truckNumber: '11006', driverName: 'Fixture Driver', trailerNumber: 'TR-100', loadedMiles: 450, emptyMiles: 50,
    totalMiles: 500, price: 2000, currency: 'CAD', loadedRatePerMile: 4.44, totalRatePerMile: 4, emptyMilesStatus: 'ready',
    stops: stops.slice(0, 3), eta: forecast([
      {...stopForecast(currentLoadId, stops[1], '10:25', 25), drivingMinutes: 135, restMinutes: 600,
        cycleAfterDeparture: cycleForecast(1255)},
      {...stopForecast(currentLoadId, stops[2], '16:00', 0), cycleAfterDeparture: cycleForecast(1100)}])},
  {id: futureLoadId, truckId, loadNumber: 1442, orderNumber: 'FUTURE-ORD-1442', status: 'planned', customerName: 'Fixture Future Customer',
    truckNumber: '11006', driverName: 'Fixture Driver', trailerNumber: 'TR-100', loadedMiles: 300, emptyMiles: 40,
    totalMiles: 340, price: 1500, currency: 'CAD', loadedRatePerMile: 5, totalRatePerMile: 4.41, emptyMilesStatus: 'ready',
    stops: stops.slice(3), eta: forecast([
      {...stopForecast(futureLoadId, stops[3], '08:00', 0), cycleAfterDeparture: cycleForecast(1005)},
      {...stopForecast(futureLoadId, stops[4], '19:05', 65), drivingMinutes: 780, restMinutes: 1800,
        cycleAfterDeparture: cycleForecast(750)}])}
];
const completedDispatches = () => dispatches().map(load => ({...load, status: 'completed', eta: null,
  stops: load.stops.map(value => ({...value, departedAt: `${dateOnly(-1)}T18:00:00Z`}))}));
const planning = () => ({truckId, dispatchId: currentLoadId, loadNumber: 1441, message: null,
  state: {profile: {}, apiConfigured: false, fuelPercent: 28, fuelUpdatedAt: new Date().toISOString(),
    plan: {id: '66666666-6666-6666-6666-666666666666', truckId, dispatchId: currentLoadId, version: 1,
      originalPlannedMiles: 2509, fromCurrentPosition: false,
      stops: stops.slice(0, 3).map(value => ({...value, address: `${value.city}, ON`, point: {latitude: 42, longitude: -80}})),
      route: {legs: [{miles: 2509, seconds: 140000, points: []}, {miles: 0, seconds: 0, points: []}], warnings: []},
      tracking: {nextStopId: stopIds[1], nextStopLabel: 'London, ON', passedStopIds: [stopIds[0]], allStopsPassed: false}},
    progress: {progressMiles: 2465, remainingMiles: 44, remainingSeconds: 2600, offRoute: false, locationStale: false},
    eta: forecast(dispatches().flatMap(load => load.eta.stops))}});
const success = response => ({success: true, response, errors: []});
const paginated = items => ({items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1,
  hasPreviousPage: false, hasNextPage: false});
const truck = {truckId, unitNumber: '11006', driverName: 'Fixture Driver', trailerNumber: 'TR-100',
  latitude: 41.8, longitude: -87.6, speed: 65, heading: 0, updatedAt: new Date().toISOString(), engineState: 'Running'};
const fixtures = new Map([
  ['/api/auth/me', {id, name: 'Fixture Administrator', email: 'fixture@example.invalid', isAdmin: true}],
  ['/api/users', success(paginated([{id, name: 'Fixture Dispatcher', email: 'fixture@example.invalid', isActive: true, role: 'Dispatch'}]))],
  ['/api/settings/planning', success({preferences: {useIfta: true, maxDetourMinutes: 15,
    reserveGallons: 25, fillPercent: 100, stopCostUsd: 20, driverHourlyCostUsd: 35}, revision: 1, updatedAt: null})],
  ['/api/settings/dispatch', success({loadNumberPrefix: 'AMF', revision: 1, updatedAt: null})],
  ['/api/settings/integrations', success(['torqueai', 'samsara', 'google-email'].map(provider => ({
    provider, configured: true, usesSavedSettings: false, canRestoreDeployment: false, revision: 0, updatedAt: null,
    fields: (provider === 'google-email' ? ['clientId', 'clientSecret', 'refreshToken'] : ['apiKey'])
      .map(name => ({name, configured: true}))
  })))],
  ['/api/dispatch/board', () => success(paginated([{key: truckId, truckId, truckNumber: '11006',
    driverName: truck.driverName, trailerNumber: truck.trailerNumber, speed: 65, engineState: 'Running',
    currentCycle: {calculatedAt: new Date().toISOString(), validUntil: new Date(Date.now() + 120_000).toISOString(), cycle: cycleAtCalculation()},
    dispatches: dispatches()}]))],
  ['/api/dispatch', () => success(paginated(completedDispatches()))],
  ['/api/fleet/locations', success({trucks: [truck], points: [truck]})],
  ['/api/fleet/planning/previews', success([])]
]);
const mapStub = `export async function createFleetMap(element) {
  element.dataset.uiFixture = 'map-provider-not-tested';
  return {setOptions(){},setTrucks(){},setTrucksVisible(){},setStationsVisible(){},setTrafficVisible(){},
    setIfta(){},clearSelection(){},clearNextLoads(){},setNextLoads(){},setNextLoadsVisible(){},
    focusTruck(){return false;},dispose(){delete element.dataset.uiFixture;}};
}`;
const stubIntegrity = `sha256-${createHash('sha256').update(mapStub).digest('base64')}`;
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g, (_all, open, json, close) => {
    const map = JSON.parse(json);
    for (const name of Object.keys(map.integrity ?? {})) {
      if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(name)) map.integrity[name] = stubIntegrity;
    }
    return open + JSON.stringify(map) + close;
  });
const browser = await chromium.launch({headless: true,
  ...(process.env.UI_TEST_BROWSER_CHANNEL ? {channel: process.env.UI_TEST_BROWSER_CHANNEL} : {})});
const report = {artifact, scope: 'Actual staged Blazor UI with deterministic APIs; map provider/GPU module stubbed; no network or business writes.',
  cases: [], failures: [], unexpectedRequests: [], browserErrors: []};
const check = (condition, message) => { if (!condition) report.failures.push(message); };
async function dispatchTop(page) {
  return page.evaluate(() => ['.dispatch-page > .page-header', '.dispatch-board__filters', '#dispatch-search', '.dispatch-board__body']
    .map(selector => {
      const element = document.querySelector(selector);
      if (!element) return null;
      const box = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return {x: box.x + window.scrollX, y: box.y + window.scrollY, width: box.width, height: box.height,
        frame: ['paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'borderTopWidth', 'borderRightWidth',
          'borderBottomWidth', 'borderLeftWidth', 'borderTopLeftRadius', 'backgroundColor'].map(key => style[key])};
    }));
}
async function checkDispatchTop(page, baseline, name) {
  const current = await dispatchTop(page);
  check(current.every((box, index) => box && baseline[index]
    && (index === 3 ? ['x', 'y', 'width'] : ['x', 'y', 'width', 'height'])
      .every(key => Math.abs(box[key] - baseline[index][key]) <= 1)
    && JSON.stringify(box.frame) === JSON.stringify(baseline[index].frame)),
    name + ' keeps the shared heading, toolbar, search and body frame in the same place');
}
async function checkDispatchLoading(page, baseline, name) {
  const status = page.locator('.dispatch-board__body > [role="status"]');
  await status.waitFor();
  if (baseline) await checkDispatchTop(page, baseline, name + ' while loading');
  const placement = await status.evaluate(element => {
    const body = element.parentElement, parentBox = body.getBoundingClientRect(), box = element.getBoundingClientRect();
    const style = getComputedStyle(body);
    return {left: box.left - parentBox.left, top: box.top - parentBox.top,
      expectedLeft: parseFloat(style.paddingLeft) + parseFloat(style.borderLeftWidth),
      expectedTop: parseFloat(style.paddingTop) + parseFloat(style.borderTopWidth)};
  });
  check(Math.abs(placement.left - placement.expectedLeft) <= 1 && Math.abs(placement.top - placement.expectedTop) <= 1,
    name + ' loading status shares the body origin without view-specific inner spacing');
  return dispatchTop(page);
}
async function checkCardAlignment(page, name) {
  const lanes = await page.locator('.dispatch-truck__loads').evaluateAll(elements => elements.map(lane => ({
    horizontal: getComputedStyle(lane).gridAutoFlow === 'column',
    cards: [...lane.querySelectorAll(':scope > .dispatch-load')].map(card => ({
      height: card.getBoundingClientRect().height,
      bottom: card.getBoundingClientRect().bottom,
      footerBottom: card.querySelector('.dispatch-load__footer').getBoundingClientRect().bottom
    }))
  })));
  for (const lane of lanes.filter(value => value.horizontal && value.cards.length > 1)) {
    for (const key of ['height', 'bottom', 'footerBottom']) {
      const values = lane.cards.map(card => card[key]);
      check(Math.max(...values) - Math.min(...values) <= 1,
        name + ` horizontal cards align ${key} with different stop counts`);
    }
  }
}
async function checkLoadDialogs(page, name, screenshots) {
  const expectedCycles = [null, '~20h 55m', '~18h 20m', '~16h 45m', '~12h 30m'];
  for (const [index, button] of (await page.locator('.dispatch-load__footer .dispatch-load__details').all()).entries()) {
    await button.focus();
    const before = await page.locator('.dispatch-load').evaluateAll(cards => cards.map(card => card.getBoundingClientRect().height));
    const scrollBefore = await page.evaluate(() => window.scrollY);
    await button.press('Enter');
    const dialog = page.locator('.dispatch-load-dialog');
    await dialog.waitFor();
    check(await dialog.evaluate(element => element.matches(':modal') && element.contains(document.activeElement)),
      name + ' load details open as a keyboard-focused native modal');
    check(await page.evaluate(() => document.documentElement.classList.contains('popup-open')), name + ' popup locks background scrolling');
    check(await dialog.locator('.dispatch-paper__phase').innerText() === (index === 0 ? 'Current' : 'Next'),
      name + ' popup retains the selected load phase');
    const financials = await dialog.locator('.dispatch-paper__financials').innerText();
    const expectedFinancials = index === 0
      ? ['450 mi', 'Total 500 mi', 'Empty 50 mi', '2,000.00 CAD', '4.44 CAD', '4.00 CAD']
      : ['300 mi', 'Total 340 mi', 'Empty 40 mi', '1,500.00 CAD', '5.00 CAD', '4.41 CAD'];
    check(expectedFinancials.every(value => financials.includes(value)),
      name + ' Details retains all six server-provided mileage and financial values');
    const initialImage = resolve(output, `${name}-load-${index + 1}-popup-initial.png`);
    await page.screenshot({path: initialImage, fullPage: true});
    screenshots.push(initialImage);
    const items = dialog.locator('.dispatch-load__stop');
    check(await items.count() === (index === 0 ? 3 : 2), name + ' popup retains every load stop');
    const stopLayout = await items.evaluateAll(elements => elements.map(element => {
      const box = element.getBoundingClientRect();
      return {x: box.x, y: box.y, width: box.width, height: box.height};
    }));
    const dialogLayout = await dialog.evaluate(element => ({width: element.getBoundingClientRect().width,
      rootFont: parseFloat(getComputedStyle(document.documentElement).fontSize)}));
    if (dialogLayout.width >= dialogLayout.rootFont * 50) {
      check(stopLayout[1].x >= stopLayout[0].x + stopLayout[0].width - 1
        && Math.abs(stopLayout[1].y - stopLayout[0].y) <= 1,
      name + ' wide load popup places stops beside each other');
    }
    for (const [position, stop] of (await items.all()).entries()) {
      const globalIndex = index === 0 ? position : position + 3;
      const job = globalIndex === 0 || globalIndex === 3 ? 'PU' : 'DL';
      const loadNumber = index === 0 ? '1441' : '1442';
      check(await stop.getAttribute('data-stop-id') === stopIds[globalIndex], name + ' popup preserves stop identity and order');
      const details = stop.locator('.dispatch-load__stop-details');
      check(await details.count() === 1 && await details.getAttribute('open') === null,
        name + ' secondary stop fields start collapsed');
      await details.locator(':scope > summary').focus();
      await details.locator(':scope > summary').press('Enter');
      check(await details.getAttribute('open') !== null,
        name + ' secondary stop fields expand from the keyboard');
      check((await stop.locator('.dispatch-load__appointment-reference').innerText()).replace(/\s+/g, ' ') === `Appt # ${job}${loadNumber}`,
        name + ' popup shows job-specific appointment reference');
      check((await stop.locator('.dispatch-load__reference-number').innerText()).replace(/\s+/g, ' ') === `Ref # ${job === 'DL' ? 'DEL' : job}-REF${loadNumber}`,
        name + ' popup keeps stop reference distinct from appointment');
      if (globalIndex === 0) {
        check(await stop.locator('.dispatch-load__completed').count() === 1
          && !await stop.locator('.dispatch-load__stop-cycle, .arrival-estimate__timezone').count(), name + ' completed pickup has no stale forecast');
      } else {
        check((await stop.locator('.arrival-estimate__timezone').innerText()).includes('UTC-04:00'), name + ' popup retains local arrival time zone');
        check(await stop.locator('.dispatch-load__stop-cycle strong').innerText() === expectedCycles[globalIndex], name + ' popup retains the exact per-stop cycle');
      }
      await stop.scrollIntoViewIfNeeded();
      check(await stop.evaluate(element => {
        const box = element.getBoundingClientRect(), modal = element.closest('dialog').getBoundingClientRect();
        return box.x >= modal.x && box.right <= modal.right + 1 && element.scrollWidth <= element.clientWidth + 2;
      }), name + ' detailed stop remains horizontally contained in the popup');
    }
    if (index === 1) check((await dialog.locator('.dispatch-load__cycle').textContent()).includes('~12h 30m')
      && !(await dialog.innerText()).includes('Next recap'), name + ' future cycle summary does not duplicate driver recap');
    const image = resolve(output, `${name}-load-${index + 1}-popup.png`);
    await page.screenshot({path: image, fullPage: true});
    screenshots.push(image);
    if (index === 0) await page.keyboard.press('Escape');
    else await page.mouse.click(2, 2);
    await dialog.waitFor({state:'detached'});
    await page.waitForFunction(() => !document.documentElement.classList.contains('popup-open'));
    check(await button.evaluate(element => element === document.activeElement), name + ' popup dismissal returns focus to the original load button');
    check(Math.abs(await page.evaluate(() => window.scrollY) - scrollBefore) <= 1, name + ' popup restores the original page scroll position');
    const after = await page.locator('.dispatch-load').evaluateAll(cards => cards.map(card => card.getBoundingClientRect().height));
    check(before.every((height, i) => Math.abs(height - after[i]) <= 1), name + ' popup never expands the underlying cards');
    await checkCardAlignment(page, name + ' after popup');
  }
}
async function checkToolbar(page, name) {
  await page.mouse.move(0, 0);
  const metrics = await page.locator('.filter-toolbar').evaluate(toolbar => {
    const visible = element => element.getBoundingClientRect().width > 0 && element.getBoundingClientRect().height > 0;
    const controls = [...toolbar.querySelectorAll('input:not([type=checkbox]), select, .filter-toolbar__views button, .filter-toolbar__toggle, .btn')]
      .filter(visible).map(element => {
        const {x, y, width, height} = element.getBoundingClientRect();
        return {x, y, width, height, name: element.id || element.textContent.trim(),
          shadow: getComputedStyle(element).boxShadow};
      });
    const toggles = [...toolbar.querySelectorAll('.filter-toolbar__toggle')].filter(visible).map(label => {
      const input = label.querySelector('input');
      return {name: label.textContent.trim(), type: input.type, tabIndex: input.tabIndex,
        width: input.getBoundingClientRect().width, background: getComputedStyle(label).backgroundColor};
    });
    return {controls, toggles, rootFont: parseFloat(getComputedStyle(document.documentElement).fontSize),
      viewport: document.documentElement.clientWidth, gap: parseFloat(getComputedStyle(toolbar).gap)};
  });
  const expectedHeight = metrics.rootFont * (metrics.viewport < 800 ? 2.75 : 2.5);
  check(metrics.controls.length >= 2, name + ' toolbar controls are present');
  check(Math.abs(metrics.gap - metrics.rootFont / 2) <= 1, name + ' toolbar spacing uses the shared rhythm');
  for (const control of metrics.controls) {
    check(Math.abs(control.height - expectedHeight) <= 1, name + ` aligned toolbar height: ${control.name}`);
    check(control.x >= -1 && control.x + control.width <= metrics.viewport + 1,
      name + ` toolbar control stays inside the viewport: ${control.name}`);
  }
  for (const toggle of metrics.toggles) {
    check(toggle.type === 'checkbox' && toggle.tabIndex === 0, name + ` native keyboard checkbox: ${toggle.name}`);
    check(Math.abs(toggle.width - metrics.rootFont * 1.25) <= 1, name + ` shared checkbox size: ${toggle.name}`);
    check(toggle.background === 'rgba(0, 0, 0, 0)', name + ` unframed checkbox label: ${toggle.name}`);
  }
  return metrics;
}
await mkdir(output, {recursive: true});
try {
  for (const width of [1440, 390, 2344]) for (const theme of ['light', 'dark']) for (const scale of [100, 200]) {
    const context = await browser.newContext({viewport: {width, height: 1000}, colorScheme: theme,
      reducedMotion: 'reduce', serviceWorkers: 'block'});
    await context.addInitScript(({id, theme, scale}) => {
      localStorage.setItem('auth_session', JSON.stringify({Id: id, AccessToken: 'fixture', RefreshToken: 'fixture'}));
      window.uiFixtureCopies = [];
      Object.defineProperty(navigator, 'clipboard', {value: {writeText: async value => window.uiFixtureCopies.push(value)}});
      document.addEventListener('DOMContentLoaded', () => {
        document.documentElement.dataset.theme = theme;
        document.documentElement.style.fontSize = scale + '%';
      });
    }, {id, theme, scale});
    let boardHold;
    const holdBoard = () => {
      assert.ok(!boardHold, 'A board fixture response is already held');
      let release;
      boardHold = new Promise(resolve => {release = resolve;});
      return () => {boardHold = null; release();};
    };
    await installReleaseArtifact(context, artifact, origin);
    await context.route('**/*', async route => {
      const url = new URL(route.request().url());
      // Planning polls are conditional GETs (older Clients used POST); fulfill this exact fixture without forwarding it.
      if (url.origin === origin && ['GET', 'POST'].includes(route.request().method()) && url.pathname === `/api/fleet/trucks/${truckId}/planning`) {
        await route.fulfill({status: 200, json: success(planning())});
      } else if (url.origin !== origin || !['GET', 'HEAD'].includes(route.request().method())) {
        report.unexpectedRequests.push(`${route.request().method()} ${url.origin}${url.pathname}`);
        await route.abort('blockedbyclient');
      } else if (url.pathname.startsWith('/api/')) {
        if (url.pathname === '/api/dispatch/board' && boardHold) await boardHold;
        if (url.pathname === '/api/dispatch' && url.searchParams.get('status') !== 'completed')
          report.unexpectedRequests.push(`Unexpected dispatch scope ${url.search}`);
        const source = fixtures.get(url.pathname);
        const fixture = typeof source === 'function' ? source() : source;
        if (!fixture) report.unexpectedRequests.push(`Unmocked API ${url.pathname}`);
        await route.fulfill({status: fixture ? 200 : 500, json: fixture ?? {success: false, errors: ['Unmocked API']}});
      } else if (/\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(url.pathname)) {
        await route.fulfill({status: 200, contentType: 'text/javascript', body: mapStub});
      } else if (route.request().isNavigationRequest()) {
        await route.fulfill({status: 200, contentType: 'text/html', body: html});
      } else await route.fallback();
    });
    const page = await context.newPage();
    page.on('pageerror', error => report.browserErrors.push(error.message));
    await page.goto(origin + '/login');
    await page.waitForURL(origin + '/fleet/map');
    await page.getByRole('heading', {name: 'Fleet Map', exact: true, level: 1}).waitFor();
    check(await page.locator('.login-page').count() === 0, 'Authenticated Login redirects without showing credentials');
    if (width === 390) await page.getByRole('button', {name: 'Open menu', exact: true}).click();
    const accountButton = page.locator('.sidebar__account');
    check(await accountButton.getAttribute('aria-expanded') === 'false', 'Account actions start collapsed');
    check(await page.getByRole('button', {name: 'Logout', exact: true}).count() === 0, 'No standalone Logout row');
    await accountButton.click();
    await page.getByRole('button', {name: 'Logout', exact: true}).waitFor();
    await page.keyboard.press('Escape');
    check(await accountButton.getAttribute('aria-expanded') === 'false', 'Escape closes account actions');
    check(await accountButton.evaluate(element => document.activeElement === element), 'Escape restores account trigger focus');
    if (width === 390) await page.keyboard.press('Escape');
    const measurements = [];
    const pages = [
      ['/dispatch', 'Dispatch', '.dispatch-truck'], ['/users', 'Users', '.users-page__user'],
      ['/settings', 'Settings', '#settings-ifta'], ['/fleet/map', 'Fleet Map', '[data-ui-fixture]'],
      ['/users/add', 'Add User', '#name']
    ];
    for (const [path, title, ready] of width === 2344 ? pages.slice(0, 1) : pages) {
      const releaseInitial = path === '/dispatch' ? holdBoard() : null;
      await page.goto(origin + path);
      await page.getByRole('heading', {name: title, exact: true, level: 1}).waitFor();
      const initialDispatchTop = releaseInitial ? await checkDispatchLoading(page, null, `${width}/${theme}/${scale} initial Dispatch`) : null;
      releaseInitial?.();
      await page.locator(ready).waitFor();
      if (path === '/dispatch') {
        await page.locator('.dispatch-load__stop').nth(4).waitFor();
        await page.locator('.dispatch-planning__metric').nth(2).waitFor({state: 'attached'});
        await page.waitForFunction(() => document.querySelector('.dispatch-load__number')?.textContent.trim() === 'AMF1441');
        await checkDispatchTop(page, initialDispatchTop, `${width}/${theme}/${scale} initial Dispatch ready`);
      }
      if (path === '/settings') await page.locator('#settings-load-prefix').waitFor();
      const name = `${width}-${theme}-${scale}-${path.replaceAll('/', '-').slice(1)}`;
      const screenshot = resolve(output, name + '.png');
      await page.screenshot({path: screenshot, fullPage: true});
      if (path === '/dispatch') {
        await checkCardAlignment(page, name + ' collapsed');
        check(await page.locator('.dispatch-load details, .dispatch-planning details').count() === 0,
          name + ' default cards and driver header do not expand inline');
        check(await page.locator('.dispatch-load__summary, .dispatch-load__metrics, .dispatch-paper__financials').count() === 0,
          name + ' mileage and pricing details stay out of compact cards until opened');
        await page.locator('.dispatch-truck__loads').evaluateAll(lanes => lanes.forEach(lane => { lane.scrollLeft = 0; }));
        await page.evaluate(() => window.scrollTo(0, 0));
      }
      const metrics = await page.evaluate(() => {
        const rect = element => {
          const {x, y, width, height} = element.getBoundingClientRect();
          return {x, y, width, height};
        };
        const visible = element => element.getBoundingClientRect().width > 0 && element.getBoundingClientRect().height > 0;
        const controls = [...document.querySelectorAll('main button, main input:not([type=checkbox]), main select, main .btn')]
          .filter(visible).map(element => ({name: element.getAttribute('aria-label') || element.id || element.textContent.trim(),
            ...rect(element), scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
            scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
            scrollableCard: element.closest('.dispatch-truck__loads') ? rect(element.closest('.dispatch-load')) : null}));
        const textBlock = element => element ? {text: element.textContent.trim(), ...rect(element),
          fontSize: parseFloat(getComputedStyle(element).fontSize)} : null;
        const inkRect = element => {
          if (!element) return null;
          const range = document.createRange();
          range.selectNodeContents(element);
          const {x, y, width, height} = range.getBoundingClientRect();
          return {x, y, width, height};
        };
        const cycleBlock = element => element ? {...textBlock(element),
          scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
          scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
          fields: [...element.querySelectorAll('dl > div')].map(field => ({...rect(field),
            label: textBlock(field.querySelector('dt')), value: textBlock(field.querySelector('dd')),
            scrollWidth: field.scrollWidth, clientWidth: field.clientWidth})),
          recapTime: element.querySelector('time')?.getAttribute('datetime'),
          recap: textBlock(element.querySelector('time')),
          recapAmount: textBlock(element.querySelector('dd > span')),
          home: textBlock(element.querySelector('small')),
          homeZone: element.querySelector('small')?.getAttribute('title')
        } : null;
        const dispatchCards = [...document.querySelectorAll('.dispatch-load')].map(card => ({...rect(card),
          current: card.classList.contains('dispatch-load--current'),
          loadNumber: textBlock(card.querySelector('.dispatch-load__number')),
          order: textBlock(card.querySelector('.dispatch-load__order')),
          cycle: cycleBlock(card.querySelector('.dispatch-load__cycle')),
          overview: rect(card.querySelector('.dispatch-load__overview')),
          footer: rect(card.querySelector('.dispatch-load__footer')),
          stopGrid: rect(card.querySelector('.dispatch-load__route')),
          stops: [...card.querySelectorAll('.dispatch-load__stop')].map(stop => ({...rect(stop),
            id: stop.dataset.stopId, scrollWidth: stop.scrollWidth, clientWidth: stop.clientWidth,
            text: stop.textContent.trim(),
            number: stop.querySelector('.dispatch-load__stop-number')?.textContent.trim(),
            location: stop.querySelector('.dispatch-load__location')?.textContent.trim(),
            locationText: textBlock(stop.querySelector('.dispatch-load__location')),
            facility: stop.querySelector('.dispatch-load__facility')?.textContent.trim(),
            facilityText: textBlock(stop.querySelector('.dispatch-load__facility')),
            appointmentReference: textBlock(stop.querySelector('.dispatch-load__appointment-reference')),
            referenceNumber: textBlock(stop.querySelector('.dispatch-load__reference-number')),
            appointment: textBlock(stop.querySelector('.arrival-estimate__appointment')),
            estimate: textBlock([...stop.querySelectorAll('.arrival-estimate > span')].find(span => /^ETA\b/.test(span.textContent.trim()))),
            cycle: textBlock(stop.querySelector('.dispatch-load__stop-cycle')),
            cycleValue: textBlock(stop.querySelector('.dispatch-load__stop-cycle strong')),
            timing: textBlock(stop.querySelector('.dispatch-load__timing')),
            late: stop.querySelector('.arrival-estimate__late')?.textContent.trim() ?? null,
            actual: stop.querySelector('.dispatch-load__actual')?.textContent.trim() ?? null,
            completed: !!stop.querySelector('.dispatch-load__completed')
          }))}));
        const summary = document.querySelector('.dispatch-planning--compact');
        const routeSummary = summary ? {...rect(summary), heading: textBlock(summary.querySelector('.dispatch-planning__heading')),
          identity: textBlock(summary.querySelector('.dispatch-planning__identity')),
          fuel: textBlock(summary.querySelector('.fuel-reading')),
          recap: textBlock(summary.querySelector('.driver-next-recap')),
          recapTime: summary.querySelector('.driver-next-recap time')?.getAttribute('datetime'),
          nextStop: textBlock(summary.querySelector('.dispatch-planning__next')),
          nextStopInk: inkRect(summary.querySelector('.dispatch-planning__next > strong')),
          appointment: textBlock(summary.querySelector('.dispatch-planning__details .arrival-estimate__appointment')),
          metrics: [...summary.querySelectorAll('.dispatch-planning__metric')].map(metric => ({...rect(metric),
            label: textBlock(metric.querySelector(':scope > span')), miles: textBlock(metric.querySelector('.dispatch-planning__miles')),
            kilometres: textBlock(metric.querySelector('small')), scrollWidth: metric.scrollWidth, clientWidth: metric.clientWidth}))} : null;
        const truck = document.querySelector('.dispatch-truck');
        const headerZone = selector => {
          const element = truck?.querySelector(selector);
          return element ? {...rect(element), scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
            scrollHeight: element.scrollHeight, clientHeight: element.clientHeight} : null;
        };
        const truckHeader = truck ? {frame: rect(truck),
          identity: headerZone(':scope > .dispatch-truck__header'),
          content: headerZone('.dispatch-planning__content'),
          hos: headerZone('.driver-hours-panel'),
          driver: headerZone('.dispatch-planning__driver'),
          identityGroup: headerZone('.dispatch-truck__identity'),
          mapAction: headerZone('.dispatch-truck__map'),
          identityGap: parseFloat(getComputedStyle(truck.querySelector('.dispatch-truck__header')).columnGap)} : null;
        const prefixInput = document.querySelector('#settings-load-prefix');
        const prefixForm = prefixInput?.closest('form');
        const fuelForm = document.querySelector('#settings-ifta')?.closest('form');
        const prefixSettings = prefixInput ? {...rect(prefixInput), value: prefixInput.value,
          label: textBlock(document.querySelector('label[for="settings-load-prefix"]')),
          independentForm: !!prefixForm && !!fuelForm && prefixForm !== fuelForm
            && !fuelForm.contains(prefixInput) && !prefixForm.contains(document.querySelector('#settings-ifta'))} : null;
        return {heading: rect(document.querySelector('main h1')), controls, dispatchCards, routeSummary, truckHeader, prefixSettings,
          truckWidth: document.querySelector('.dispatch-truck')?.clientWidth,
          viewport: document.documentElement.clientWidth, documentWidth: document.documentElement.scrollWidth,
          rootFont: getComputedStyle(document.documentElement).fontSize,
          background: getComputedStyle(document.body).backgroundColor};
      });
      const detailScreenshots = [];
      if (path === '/dispatch') {
        const expanded = resolve(output, `${name}-expanded.png`);
        await page.screenshot({path: expanded, fullPage: true});
        detailScreenshots.push(expanded);
      }
      if (path === '/dispatch' || path === '/fleet/map') {
        metrics.toolbar = await checkToolbar(page, name);
        const detail = resolve(output, `${name}-toolbar.png`);
        await page.locator('.filter-toolbar').screenshot({path: detail});
        detailScreenshots.push(detail);
      }
      if (path === '/dispatch') {
        const future = page.locator('.dispatch-load').last();
        const panels = [['late-stop', future.locator('.dispatch-load__stop').last()]];
        if (width === 390) panels.push(['stop', future.locator('.dispatch-load__stop').first()],
          ['footer', future.locator('.dispatch-load__footer')]);
        for (const [panel, locator] of panels) {
          const detail = resolve(output, `${name}-${panel}.png`);
          await locator.screenshot({path: detail});
          detailScreenshots.push(detail);
        }
      }
      measurements.push({path, title, screenshot, detailScreenshots, ...metrics});
      if (path === '/settings') {
        assert.deepEqual(await page.locator('.integration-settings [data-provider]').evaluateAll(cards => cards.map(card => card.dataset.provider)),
          ['torqueai', 'samsara', 'google-email'], name + ' only the three approved integrations');
        assert.equal(await page.locator('.integration-settings input').count(), 0, name + ' credentials are not read back');
        const google = page.locator('[data-provider="google-email"]');
        await google.getByRole('button', {name: 'Edit credentials', exact: true}).click();
        assert.equal(await google.locator('input[type=password][autocomplete=new-password]').count(), 3,
          name + ' Google email uses three empty OAuth credential inputs');
        assert.deepEqual(await google.locator('input').evaluateAll(fields => fields.map(field => field.value)), ['', '', ''],
          name + ' replacement inputs contain no saved credentials');
        const integrationLayout = await page.locator('.integration-settings').evaluate(section => {
          const bounds = element => {
            const rect = element.getBoundingClientRect();
            return {left: rect.left, right: rect.right, width: rect.width, height: rect.height,
              scrollWidth: element.scrollWidth, clientWidth: element.clientWidth};
          };
          return {viewport: document.documentElement.clientWidth,
            cards: [...section.querySelectorAll('[data-provider]')].map(bounds),
            fields: [...section.querySelectorAll('input')].map(bounds),
            controls: [...section.querySelectorAll('button')].map(bounds)};
        });
        for (const [index, field] of integrationLayout.fields.entries()) {
          check(field.left >= 0 && field.right <= integrationLayout.viewport + 1,
            `${name} integration replacement field ${index} remains inside the viewport`);
          check(field.height >= (width <= 799 ? 44 : 40) - 1,
            `${name} integration replacement field ${index} retains shared control height`);
        }
        for (const [index, card] of integrationLayout.cards.entries())
          check(card.scrollWidth <= card.clientWidth + 1 && card.left >= 0 && card.right <= integrationLayout.viewport + 1,
            `${name} integration card ${index} contains its editable content`);
        const integrationScreenshot = resolve(output, `${name}-integrations-editing.png`);
        await page.locator('.integration-settings').screenshot({path: integrationScreenshot});
        detailScreenshots.push(integrationScreenshot);
        await google.locator('#integration-google-email-refreshToken').fill('offline-discarded-draft');
        await google.getByRole('button', {name: 'Cancel', exact: true}).click();
        await google.getByRole('button', {name: 'Edit credentials', exact: true}).click();
        assert.equal(await google.locator('#integration-google-email-refreshToken').inputValue(), '', name + ' cancel clears entered credentials');
        await google.getByRole('button', {name: 'Cancel', exact: true}).click();
        check(metrics.prefixSettings?.value === 'AMF' && metrics.prefixSettings.independentForm,
          name + ' saved load prefix is editable independently from fuel preferences');
        check(metrics.prefixSettings?.label?.text.includes('prefix'), name + ' load prefix has a visible field label');
        await page.locator('#settings-load-prefix').fill('TMS-');
        assert.equal(await page.locator('#settings-detour').count(), 0, name + ' no hard detour limit control');
        assert.equal(await page.locator('#settings-driving-cost, #settings-reserve, #settings-fill').count(), 0,
          name + ' retired fleet defaults have no editable controls');
        assert.equal(await page.getByRole('heading', {name: 'Fuel stops', exact: true}).count(), 0,
          name + ' retired fuel stops section is absent');
        assert.equal(await page.getByRole('button', {name: 'Restore defaults', exact: true}).count(), 0,
          name + ' retired restore defaults action is absent');
        const iftaBefore = await page.locator('#settings-ifta').isChecked();
        await page.locator('#settings-ifta').setChecked(!iftaBefore);
        assert.equal(await page.locator('#settings-load-prefix').inputValue(), 'TMS-', name + ' price basis does not reset the dispatch prefix draft');
        check(await page.getByRole('button', {name: 'Save settings', exact: true}).isEnabled(),
          name + ' price basis remains an explicit save, not an automatic write');
        const prefixExample = page.locator('.dispatch-number-settings__form .settings-page__hint strong');
        await page.waitForFunction(() => document.querySelector('.dispatch-number-settings__form .settings-page__hint strong')?.textContent === 'TMS-1373');
        assert.equal(await prefixExample.textContent(), 'TMS-1373', name + ' custom prefix preview');
        await page.locator('#settings-load-prefix').fill('');
        await page.locator('#settings-ifta').focus();
        await page.waitForFunction(() => document.querySelector('.dispatch-number-settings__form .settings-page__hint strong')?.textContent === '1373');
        assert.equal(await prefixExample.textContent(), '1373', name + ' blank prefix preview keeps only the number');
        assert.equal(await page.locator('#settings-ifta').isChecked(), !iftaBefore,
          name + ' prefix changes preserve the independent price basis draft');
      }
      if (path === '/dispatch') {
        const cards = metrics.dispatchCards;
        check(cards.length === 2 && cards[0].current && !cards[1].current, name + ' current and upcoming cards');
        if (width > 550) {
          check(cards[1].x >= cards[0].x + cards[0].width - 1 && Math.abs(cards[1].y - cards[0].y) <= 1,
            name + ' assigned loads occupy a horizontal lane in dispatch order');
        } else {
          check(cards[1].y >= cards[0].y + cards[0].height - 1,
            name + ' mobile load cards stack without shrinking the route timeline');
        }
        check(cards[0]?.loadNumber?.text === 'AMF1441' && cards[1]?.loadNumber?.text === 'AMF1442',
          name + ' current and future loads use the same configured prefix');
        check(cards.every(card => card.cycle === null), name + ' detailed cycle forecasts belong to the load popup');
        const expectedOrders = ['CURRENT-ORD-1441', 'FUTURE-ORD-1442'];
        check(cards.every((card, index) => card.order?.text === `Order ${expectedOrders[index]}`), name + ' visible current and future order numbers');
        for (const [index, button] of (await page.getByRole('button', {name: 'Copy order number', exact: true}).all()).entries()) {
          await button.click();
          check(await page.evaluate(value => window.uiFixtureCopies.at(-1) === value, expectedOrders[index]), name + ` exact order ${index + 1} copied`);
          check(await button.locator('.is-copied').count() === 1 && !await page.getByText('Order number copied.', {exact:true}).count(),
            name + ' copy confirmation stays inside the existing button');
        }
        const rootFont = parseFloat(metrics.rootFont);
        const header = metrics.truckHeader;
        const zones = ['identity', 'content', 'hos', 'driver'].map(key => [key, header?.[key]]);
        for (const [key, zone] of zones) {
          check(zone && zone.width > 0 && zone.height > 0
            && zone.x >= header.frame.x - 1 && zone.x + zone.width <= header.frame.x + header.frame.width + 1
            && zone.scrollWidth <= zone.clientWidth + 2 && zone.scrollHeight <= zone.clientHeight + 2,
          name + ` truck header ${key} zone stays visible and unclipped`);
        }
        for (const [index, [firstKey, first]] of zones.entries()) {
          for (const [secondKey, second] of zones.slice(index + 1)) {
            check(first && second && (Math.min(first.x + first.width, second.x + second.width) - Math.max(first.x, second.x) <= 1
              || Math.min(first.y + first.height, second.y + second.height) - Math.max(first.y, second.y) <= 1),
            name + ` truck header ${firstKey} and ${secondKey} zones do not overlap`);
          }
        }
        const hosDials = await page.locator('.dispatch-truck .driver-hours__clock .driver-hours__dial').evaluateAll(nodes => nodes.map(node => {
          const dial = node.getBoundingClientRect(), text = node.querySelector('strong').getBoundingClientRect();
          return {diameter: dial.width, height: dial.height, corner: Math.hypot(text.width / 2, text.height / 2),
            centered: Math.abs((text.left + text.right - dial.left - dial.right) / 2),
            fontSize: parseFloat(getComputedStyle(node.querySelector('strong')).fontSize)};
        }));
        check(hosDials.length >= 4 && hosDials.every(dial => Math.abs(dial.height - dial.diameter) <= 1
          && dial.centered <= 1 && dial.corner < dial.diameter * 26 / 64 - 1
          && dial.fontSize <= dial.diameter * .24 + .02),
          name + ' Dispatch HOS values share the responsive diameter and stay inside their rings');
        if (width === 2344 && scale === 100) {
          check(header?.identity && header?.content && header?.hos
            && Math.abs(header.content.x - header.identity.x - header.identity.width) <= 1
            && Math.abs(header.hos.x - header.content.x - header.content.width) <= 1
            && Math.max(header.identity.y, header.content.y, header.hos.y)
              < Math.min(header.identity.y + header.identity.height, header.content.y + header.content.height, header.hos.y + header.hos.height),
          name + ' wide truck header left-packs adjacent content-sized identity, telemetry and HOS groups');
          check(header?.hos && header.hos.x + header.hos.width < header.frame.x + header.frame.width - rootFont,
            name + ' unused wide-header space remains after HOS, not between header groups');
          check(header?.identityGroup && header?.mapAction
            && Math.abs(header.mapAction.x - header.identityGroup.x - header.identityGroup.width - header.identityGap) <= 1,
            name + ' truck map action stays beside identity with its named control gap');
        }
        for (const card of cards) {
          const {overview, stopGrid, footer} = card;
          check(overview.x >= card.x - 1 && overview.x + overview.width <= card.x + card.width + 1
            && footer.x >= overview.x - 1 && footer.x + footer.width <= overview.x + overview.width + 1,
            name + ' load overview and footer fit the card');
          check(footer.y >= stopGrid.y + stopGrid.height - 1
            && Math.abs(footer.x - stopGrid.x) <= 1,
            name + ' compact operational footer follows its stop timeline');
          check(card.stops.every((stop, index) => index === 0
            || stop.y >= card.stops[index - 1].y + card.stops[index - 1].height - 1),
            name + ' pickups and deliveries form a vertical timeline');
          for (const stop of card.stops) {
            check(stop.timing === null,
              name + ` stop ${stop.id} does not present cumulative driving/rest as a delay explanation`);
            check(stop.cycle === null, name + ' summary stops leave cycle details in the popup');
            check(stop.locationText?.fontSize >= rootFont - .01
              && stop.facilityText?.fontSize >= rootFont * 14 / 16 - .01
              && stop.appointment?.fontSize >= rootFont * 14 / 16 - .01
              && (!stop.estimate || stop.estimate.fontSize >= rootFont * 14 / 16 - .01),
              name + ` readable location, facility, appointment and ETA for stop ${stop.id}`);
          }
        }
        const summary = metrics.routeSummary;
        check(summary?.recap?.text.replace(/\s+/g, ' ') === `Next recap ${recapLabel} +3h 05m`
          && Date.parse(summary.recapTime) === Date.parse(cycleAtCalculation().nextRecapAt),
          name + ' truck header shows the current recap date and credited hours without delivery-derived data');
        check(summary?.heading?.text.includes('Route') && summary.heading.text.includes('Load AMF1441')
          && summary.fuel?.text === 'Fuel 28%', name + ' route/load/fuel summary identity');
        check(JSON.stringify(summary?.metrics.map(metric => metric.label.text)) === JSON.stringify(['Total Distance', 'Remaining', 'Next Stop']),
          name + ' separate route distance labels');
        check(JSON.stringify(summary?.metrics.map(metric => metric.miles.text)) === JSON.stringify(['2,509 mi', '44 mi', '44 mi'])
          && JSON.stringify(summary?.metrics.map(metric => metric.kilometres.text)) === JSON.stringify(['4,038 km', '71 km', '71 km']),
          name + ' unchanged total/remaining/next-stop distances');
        for (const metric of summary?.metrics ?? []) {
          check(metric.x >= summary.x - 1 && metric.x + metric.width <= summary.x + summary.width + 1
            && metric.scrollWidth <= metric.clientWidth + 2, name + ` route metric ${metric.label.text} is not clipped`);
          if (width > 390) check(metric.label.fontSize >= rootFont * 11 / 16 - .01
            && metric.miles.fontSize >= rootFont * 14 / 16 - .01,
            name + ` readable desktop route metric ${metric.label.text}`);
          if (width === 390) check(metric.miles.y >= metric.label.y + metric.label.height - 1
            && metric.kilometres.y >= metric.miles.y + metric.miles.height - 1, name + ` mobile ${metric.label.text} label/miles/km hierarchy`);
        }
        if (summary?.nextStop && summary.appointment
          && summary.appointment.x >= summary.nextStop.x + summary.nextStop.width - 1) {
          check(summary.appointment.x - summary.nextStop.x - summary.nextStop.width <= 2 * rootFont + 1
            && summary.appointment.x - summary.nextStopInk.x - summary.nextStopInk.width <= 4 * rootFont + 1,
            name + ' next-stop appointment stays close to its destination');
        }
        if (width === 390 && scale === 100) {
          check(summary.metrics.every(metric => Math.abs(metric.miles.y - summary.metrics[0].miles.y) <= 1), name + ' aligned mobile route values');
          check(summary.metrics[0].x + summary.metrics[0].width < summary.metrics[1].x
            && summary.metrics[1].x + summary.metrics[1].width < summary.metrics[2].x, name + ' three distinct compact mobile distance columns');
        }
        check(cards[0]?.stops.length === 3 && cards[1]?.stops.length === 2, name + ' every stop is visible');
        const renderedStops = cards.flatMap(card => card.stops);
        check(JSON.stringify(renderedStops.map(stop => stop.id)) === JSON.stringify(stopIds), name + ' stop order and identity');
        check(JSON.stringify(renderedStops.map(stop => stop.number)) === JSON.stringify(['1', '2', '3', '1', '2']),
          name + ' pickup and delivery stop numbers remain visible');
        check(renderedStops.every(stop => !stop.actual && !/\bActual (?:pickup|delivery)\b/i.test(stop.text)),
          name + ' actual pickup and delivery date rows are omitted');
        check(renderedStops.every(stop => stop.appointment && stop.location && stop.facility), name + ' stop locations, facilities and appointments');
        const completed = renderedStops.find(stop => stop.id === stopIds[0]);
        check(completed?.completed && !completed.estimate && !completed.late,
          name + ' completed pickup retains status without a stale forecast');
        const estimates = renderedStops.filter(stop => stop.estimate);
        check(estimates.length === 4, name + ' four local per-stop ETAs remain in the summary');
        check(estimates.every(stop => stop.appointment && stop.estimate.y >= stop.appointment.y + stop.appointment.height - 1),
          name + ' appointments and forecasts occupy separate rows');
        const late = renderedStops.filter(stop => stop.late);
        check(late.length === 2 && late[0].id === stopIds[1] && late[0].late === 'Late by 25m'
          && late[0].appointment?.text.includes('10:00') && late[0].estimate?.text.includes('10:25')
          && late[1].id === stopIds[4] && late[1].late === 'Late by 1h 05m'
          && late[1].appointment?.text.includes('06:00 PM') && late[1].estimate?.text.includes('07:05 PM'),
          name + ' lateness belongs to its current or future stop');
        for (const card of cards) for (const stop of card.stops) {
          check(stop.width > 0 && stop.x >= card.x - 1 && stop.x + stop.width <= card.x + card.width + 1
            && stop.scrollWidth <= stop.clientWidth + 2, name + ` stop ${stop.id} fits inside its card`);
          for (const other of card.stops) if (other.id !== stop.id)
            check(stop.x + stop.width <= other.x + 1 || other.x + other.width <= stop.x + 1
              || stop.y + stop.height <= other.y + 1 || other.y + other.height <= stop.y + 1,
              name + ` stops ${stop.id} and ${other.id} do not overlap`);
        }
        await checkLoadDialogs(page, name, detailScreenshots);
        const dispatchTopBaseline = await dispatchTop(page);
        const papers = page.getByRole('button', {name: 'Papers', exact: true});
        const table = page.getByRole('button', {name: 'Table', exact: true});
        if (await table.isVisible()) {
          const releaseTable = holdBoard();
          await table.click();
          await checkDispatchLoading(page, dispatchTopBaseline, name + ' Table');
          releaseTable();
          const firstRow = page.locator('.dispatch-table tbody tr').first();
          await firstRow.waitFor();
          await checkDispatchTop(page, dispatchTopBaseline, name + ' Table');
          await table.evaluate(button => Promise.all(button.getAnimations().map(animation => animation.finished)));
          check(await table.evaluate(button => getComputedStyle(button).color === 'rgb(255, 255, 255)'),
            name + ' selected Table remains filled with white text under the pointer');
          check((await firstRow.innerText()).includes('2,000.00 CAD')
            && (await firstRow.innerText()).includes('4.44 CAD')
            && (await firstRow.innerText()).includes('4.00 CAD'), name + ' table keeps all server financial values');
          check((await firstRow.innerText()).includes('04:00 PM – 06:00 PM'), name + ' table retains the complete delivery appointment window');
          check(await firstRow.locator('.dispatch-table__stop-entry').count() === 3, name + ' table includes intermediate delivery stops');
          const tableImage = resolve(output, `${name}-table.png`);
          await page.screenshot({path: tableImage, fullPage: true});
          detailScreenshots.push(tableImage);
          const mapLink = firstRow.locator('.dispatch-table__map');
          await mapLink.evaluate(link => link.addEventListener('click', event => event.preventDefault(), {once: true}));
          await mapLink.click();
          check(!await page.locator('.dispatch-load-dialog').count(), name + ' Map link does not also open the row popup');
          await firstRow.locator('.dispatch-table__open').focus();
          await firstRow.locator('.dispatch-table__open').press('Enter');
          await page.locator('.dispatch-load-dialog').waitFor();
          check((await page.locator('.dispatch-load-dialog .dispatch-paper__title').innerText()).includes('AMF1441'), name + ' Table opens the same load with the keyboard');
          await page.keyboard.press('Escape');
          await page.locator('.dispatch-load-dialog').waitFor({state: 'detached'});
        }
        const releasePapers = holdBoard();
        await papers.click();
        await checkDispatchLoading(page, dispatchTopBaseline, name + ' Papers');
        releasePapers();
        assert.equal(await papers.getAttribute('aria-pressed'), 'true', name + ' view interaction');
        await checkDispatchTop(page, dispatchTopBaseline, name + ' Papers');
        await page.locator('.dispatch-paper__tab').filter({hasText: 'AMF1441'}).click();
        const sheet = page.locator('.dispatch-load-dialog');
        await sheet.waitFor();
        check((await sheet.innerText()).includes('2,000.00 CAD')
          && (await sheet.innerText()).includes('4.44 CAD')
          && (await sheet.innerText()).includes('4.00 CAD'), name + ' papers keep all server financial values');
        check((await sheet.innerText()).includes('Windsor') && (await sheet.innerText()).includes('Completed'),
          name + ' papers retain the completed pickup');
        check((await sheet.innerText()).includes('04:00 PM – 06:00 PM'), name + ' papers retain the complete delivery appointment window');
        const papersImage = resolve(output, `${name}-papers.png`);
        await page.screenshot({path: papersImage, fullPage: true});
        detailScreenshots.push(papersImage);
        await page.getByRole('button', {name: 'Close load details', exact: true}).click();
        await sheet.waitFor({state: 'detached'});
        await page.locator('#dispatch-completed').click();
        await page.locator('.dispatch-papers--completed').waitFor();
        check((await page.locator('.dispatch-paper-column__heading').innerText()).includes('Completed'),
          name + ' completed papers have no active-phase folders');
        await page.getByRole('button', {name: 'Cards', exact: true}).click();
        await page.locator('.dispatch-load').nth(1).waitFor();
        await checkDispatchTop(page, dispatchTopBaseline, name + ' completed Cards');
        check(await page.locator('.dispatch-planning, .dispatch-truck__equipment').count() === 0,
          name + ' archive does not show live telemetry or route planning');
        check(await page.locator('.dispatch-load__stop').count() === 5,
          name + ' archive preserves all pickup/delivery stops');
        check(await page.locator('.dispatch-load__metrics, .dispatch-paper__financials').count() === 0,
          name + ' archive cards keep financial details collapsed');
        await page.locator('.dispatch-load').first().locator('.dispatch-load__details').click();
        const archiveDialog = page.locator('.dispatch-load-dialog');
        await archiveDialog.waitFor();
        check((await archiveDialog.locator('.dispatch-paper__financials').innerText()).includes('2,000.00 CAD'),
          name + ' archive Details preserves server rate');
        await archiveDialog.getByRole('button', {name: 'Close load details', exact: true}).click();
        await archiveDialog.waitFor({state: 'detached'});
        check(!/CURRENT LOAD|NEXT LOAD/.test(await page.locator('.dispatch-board').innerText()),
          name + ' archive is not mislabeled as current or next work');
        check(await page.getByRole('button', {name: /New load|Assign next load/}).count() === 0,
          name + ' unsupported load creation is not offered');
        const completedImage = resolve(output, `${name}-completed.png`);
        await page.screenshot({path: completedImage, fullPage: true});
        detailScreenshots.push(completedImage);
      }
      if (path === '/fleet/map' && width === 390) {
        const filters = page.getByRole('button', {name: 'Filters', exact: true});
        await filters.click();
        assert.equal(await filters.getAttribute('aria-expanded'), 'true', name + ' mobile filters interaction');
        const expanded = await checkToolbar(page, name + ' expanded');
        check(expanded.toggles.length === 5, name + ' mobile filter disclosure keeps every native checkbox');
        const detail = resolve(output, `${name}-toolbar-filters.png`);
        await page.locator('#fleet-map-filters').screenshot({path: detail});
        detailScreenshots.push(detail);
      }
      if (path === '/fleet/map') {
        const ifta = page.getByRole('checkbox', {name: 'IFTA', exact: true});
        const before = await ifta.isChecked();
        await ifta.focus();
        await ifta.press('Space');
        check(await ifta.isChecked() !== before, name + ' native checkbox toggles with Space');
        await ifta.press('Space');
        check(await ifta.isChecked() === before, name + ' native checkbox restores with Space');
      }
    }
    report.cases.push({width, theme, scale, pages: measurements});
    await writeFile(resolve(output, 'report.json'), JSON.stringify(report, null, 2));
    const mainPages = measurements.slice(0, 4);
    for (const page of measurements) {
      check(page.documentWidth <= page.viewport + 1, `${width}/${theme}/${scale} ${page.path}: horizontal overflow ${page.documentWidth} > ${page.viewport}`);
      for (const control of page.controls) {
        const bounds = control.scrollableCard;
        check(bounds ? control.x >= bounds.x - 1 && control.x + control.width <= bounds.x + bounds.width + 1
          : control.x >= -1 && control.x + control.width <= page.viewport + 1,
          `${width}/${theme}/${scale} ${page.path}: control outside viewport (${control.name})`);
        check(control.height >= 24 && control.scrollWidth <= control.clientWidth + 2 && control.scrollHeight <= control.clientHeight + 2,
          `${width}/${theme}/${scale} ${page.path}: clipped or undersized control (${control.name})`);
      }
    }
    for (const page of mainPages.slice(1)) {
      check(Math.abs(page.heading.x - mainPages[0].heading.x) <= 1,
        `${width}/${theme}/${scale}: ${page.title} heading left edge differs from Dispatch`);
      check(Math.abs(page.heading.y - mainPages[0].heading.y) <= 1,
        `${width}/${theme}/${scale}: ${page.title} heading top differs from Dispatch`);
    }
    await context.close();
    console.log(`UI smoke ${width}px ${theme} ${scale}%: ${measurements.length} pages checked.`);
  }
  assert.deepEqual(report.unexpectedRequests, [], 'Offline smoke attempted unexpected network or writes');
  assert.deepEqual(report.browserErrors, [], 'Staged UI emitted browser errors');
  assert.deepEqual(report.failures, [], 'Staged UI geometry regressions');
} finally {
  await writeFile(resolve(output, 'report.json'), JSON.stringify(report, null, 2));
  await browser.close();
}
console.log(`Offline UI smoke report: ${resolve(output, 'report.json')}`);
