import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'Set MAP_TEST_ARTIFACT_DIR to staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('route-editor');
const origin = 'http://localhost:5079';
const id = n => `11111111-1111-1111-1111-${String(n).padStart(12, '0')}`;
const truckId = id(1),
  dispatchId = id(2),
  userId = id(3);
const point = { latitude: 40, longitude: -80 };
const stops = [
  {
    id: id(4),
    sequence: 1,
    name: 'Webster, NY',
    address: '1886 Tebor Road',
    city: 'Webster',
    job: 'Pick Up',
    point,
  },
  {
    id: id(5),
    sequence: 2,
    name: 'Port St. Lucie, FL',
    address: '13077 SW Anthony F. Sansone Sr. Blvd',
    city: 'Port St. Lucie',
    job: 'Drop Off',
    point: { latitude: 28, longitude: -80 },
  },
];
const road = miles => ({
  miles,
  seconds: miles * 60,
  calculatedAt: new Date().toISOString(),
  warnings: [],
  points: [],
  legs: [{ miles, seconds: miles * 60, points: [point, stops[1].point] }],
});
const truck = {
  truckId,
  unitNumber: '11007',
  driverName: 'Fixture Driver',
  trailerNumber: '9P1571',
  ...point,
  fuelPercent: 52,
  speed: 45,
  heading: 90,
  engineState: 'On',
  updatedAt: new Date().toISOString(),
};
const plan = {
  id: id(6),
  dispatchId,
  truckId,
  version: 1,
  calculatedAt: truck.updatedAt,
  fromCurrentPosition: false,
  profile: {},
  route: road(1490),
  stops,
  tracking: {
    nextStopId: stops[0].id,
    passedStopIds: [],
    visitedStops: {},
    allStopsPassed: false,
  },
};
const planning = {
  truckId,
  dispatchId,
  loadNumber: 1383,
  state: {
    profile: {},
    plan,
    apiConfigured: true,
    fuelPercent: 52,
    progress: {
      position: point,
      remainingMiles: 1490,
      remainingSeconds: 89000,
      progressMiles: 0,
    },
  },
};
const success = response => ({ success: true, response, errors: [] });
const stub = `export async function createFleetMap(element, key, callbacks) {
  element.style.background = 'var(--ui-surface-soft)';
  const state = window.routeFixture = {payload: null, updates: [], select(number) {return callbacks.invokeMethodAsync('OnRouteOptionSelected', this.payload.session, number);},
    drag() {return callbacks.invokeMethodAsync('OnRouteViaChanged', this.payload.session, null, 0, 35, -81);}};
  return new Proxy({setRouteEditor(bytes) {const update = bytes ? JSON.parse(new TextDecoder().decode(bytes)) : null;
    if (update) {state.updates.push({bytes: bytes.length, geometry: !!update.preview}); state.payload = {...update, preview: update.preview ?? state.payload?.preview};}
    else state.payload = null;
    requestAnimationFrame(() => {const node = document.querySelector('.route-editor'); if (node) {
      const map = element.getBoundingClientRect(), box = node.getBoundingClientRect();
      node.style.setProperty('--map-inspector-side-gap', Math.max(0, Math.min(box.left - map.left, map.right - box.right)) + 'px');}});},
    setRouteBytes() {return true;}, focusTruck() {return true;}, dispose() {delete window.routeFixture;}},
    {get(target, key) {return key === 'then' ? undefined : target[key] ?? (() => {});}});
}`;
const integrity = `sha256-${createHash('sha256').update(stub).digest('base64')}`;
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g,
  (_, open, json, close) => {
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
    'Actual Blazor route editor with deterministic intercepted APIs and explicit provider stub. No live writes, authentication, Google GPU or real provider drag checks.',
  cases: [],
  errors: [],
  unexpected: [],
};
async function assertTruckInformation(page, name) {
  assert.equal(
    await page.locator('.fleet-map-mobile-summary__toggle').count(),
    0,
    `${name}: truck information has no disclosure control`,
  );
  assert.equal(
    await page.locator('#fleet-map-telemetry-details').isVisible(),
    true,
    `${name}: truck readings remain visible`,
  );
  assert.equal(
    await page.locator('#fleet-map-route-details').isVisible(),
    true,
    `${name}: load information remains visible below the readings`,
  );
  assert.equal(
    await page.locator('#fleet-map-details').evaluate(element => {
      const panels = [...element.children].map(child => child.id);
      return (
        panels[0] === 'fleet-map-telemetry-details' &&
        panels[1] === 'fleet-map-route-details'
      );
    }),
    true,
    `${name}: telemetry and HOS precede the load panel`,
  );
}
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
try {
  for (const width of [1440, 768, 390, 320])
    for (const theme of ['light', 'dark']) {
      const name = `${width}-${theme}`,
        writes = [],
        previews = [],
        loadReads = [];
      if (
        process.env.ROUTE_EDITOR_CASE &&
        process.env.ROUTE_EDITOR_CASE !== name
      )
        continue;
      const context = await browser.newContext({
        viewport: { width, height: width < 768 ? 844 : 1000 },
        colorScheme: theme,
        hasTouch: width < 768,
        serviceWorkers: 'block',
        locale: 'en-US',
        timezoneId: 'America/Toronto',
      });
      await context.addInitScript(
        ({ userId, theme }) => {
          localStorage.setItem(
            'auth_session',
            JSON.stringify({
              Id: userId,
              AccessToken: 'fixture',
              RefreshToken: 'fixture',
            }),
          );
          document.addEventListener('DOMContentLoaded', () => {
            document.documentElement.dataset.theme = theme;
          });
        },
        { userId, theme },
      );
      await installReleaseArtifact(context, artifact, origin);
      await context.route('**/*', async route => {
        const request = route.request(),
          url = new URL(request.url());
        if (url.origin !== origin) {
          report.unexpected.push(url.origin + url.pathname);
          return route.abort();
        }
        if (url.pathname.startsWith('/api/')) {
          if (
            url.pathname === `/api/dispatch/${dispatchId}` ||
            url.pathname.startsWith(`/api/fleet/trucks/${truckId}/planning`)
          )
            loadReads.push(url.pathname);
          let value;
          if (url.pathname === '/api/auth/me')
            value = {
              id: userId,
              name: 'Fixture',
              email: 'fixture@example.invalid',
              isAdmin: true,
            };
          else if (url.pathname === '/api/settings/appearance')
            value = success({
              theme,
              distanceUnit: theme === 'dark' ? 'kilometers' : 'both',
              temperatureUnit: 'celsius',
            });
          else if (url.pathname === '/api/settings/dispatch')
            value = success({
              loadNumberPrefix: 'AMF',
              revision: 1,
            });
          else if (url.pathname === '/api/fuel/price-overview')
            value = success([]);
          else if (url.pathname === '/api/fleet/locations')
            value = success({ trucks: [truck], points: [truck] });
          else if (url.pathname === '/api/fleet/planning/previews')
            value = success([]);
          else if (url.pathname === '/api/fleet/hos') value = success({});
          else if (
            url.pathname === `/api/fleet/trucks/${truckId}/weather` &&
            request.method() === 'GET'
          )
            value = success({
              celsius: 22.5,
              condition: 'CLEAR',
              description: 'Clear',
              isDaytime: true,
              updatedAt: truck.updatedAt,
            });
          else if (
            url.pathname.startsWith(`/api/fleet/trucks/${truckId}/planning`)
          )
            value = success(planning);
          else if (url.pathname === `/api/dispatch/${dispatchId}`)
            value = success({
              id: dispatchId,
              truckId,
              loadNumber: 1383,
              status: 'in_transit',
              stops,
            });
          else if (
            url.pathname.endsWith('/route/options') &&
            request.method() === 'POST'
          ) {
            const body = request.postDataJSON();
            previews.push(body);
            value = success({
              id: id(6 + previews.length),
              dispatchId,
              truckId,
              loadNumber: 1383,
              revision: 2,
              expiresAt: new Date(Date.now() + 600000).toISOString(),
              stops: [
                {
                  ...stops[0],
                  id: '00000000-0000-0000-0000-000000000000',
                  name: 'Current truck location',
                  job: 'GPS start',
                },
                stops[1],
              ],
              originUpdatedAt: truck.updatedAt,
              viaPoints: body.viaPoints,
              savedRoute: road(1490),
              options: (body.alternatives ? [1490, 1518] : [1520]).map(
                (miles, index) => ({
                  number: index + 1,
                  route: road(miles),
                  differenceMiles: miles - 1490,
                  differenceSeconds: (miles - 1490) * 60,
                }),
              ),
            });
          } else if (
            url.pathname.endsWith('/route/via-location') &&
            request.method() === 'POST'
          )
            value = success({ latitude: 35, longitude: -81 });
          else if (
            url.pathname.endsWith('/route/choice') &&
            request.method() === 'PUT'
          ) {
            writes.push(request.postDataJSON());
            value = success(3);
          }
          if (value === undefined) {
            report.unexpected.push(request.method() + ' ' + url.pathname);
            return route.abort();
          }
          return route.fulfill({ status: 200, json: value });
        }
        if (
          /\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(
            url.pathname,
          )
        )
          return route.fulfill({ contentType: 'text/javascript', body: stub });
        if (request.isNavigationRequest())
          return route.fulfill({ contentType: 'text/html', body: html });
        return route.fallback();
      });
      const page = await context.newPage();
      page.on('pageerror', error =>
        report.errors.push(name + ': ' + error.message),
      );
      page.on('console', message => {
        if (message.type() === 'error')
          report.errors.push(name + ': ' + message.text());
      });
      await page.goto(`${origin}/fleet/map?truckId=${truckId}`);
      const opener = page.getByRole('button', {
        name: 'Route options',
        exact: true,
        includeHidden: true,
      });
      await opener.waitFor({ state: 'attached' });
      await assertTruckInformation(page, name);
      await page.locator('#fleet-map-details').evaluate(element => {
        window.routeFixture.truckPanels = [...element.children];
      });
      const before = await page.locator('#fleet-map').boundingBox();
      await opener.click();
      const editor = page.locator('.route-editor');
      await editor.locator('.route-editor__option').nth(1).waitFor();
      assert.equal(
        await page.locator('.fleet-map-inspector').isVisible(),
        false,
        name + ': route editor replaces the inspector',
      );
      assert.equal(await editor.locator('.route-editor__option').count(), 2);
      assert.equal(
        (
          await editor
            .locator('.route-editor__option strong')
            .first()
            .innerText()
        )
          .replace(/\s+/g, ' ')
          .trim(),
        theme === 'dark' ? '2,398 km' : '1,490 mi · 2,398 km',
      );
      assert.ok(
        (await editor.innerText()).includes('From current truck location'),
      );
      assert.ok((await editor.innerText()).includes('Saved remaining route'));
      await editor.screenshot({ path: resolve(output, `${name}-options.png`) });
      const readsBeforeCancel = [...loadReads];
      await editor.getByRole('button', { name: 'Cancel', exact: true }).click();
      await editor.waitFor({ state: 'detached' });
      await page.locator('.fleet-map-inspector').waitFor({ state: 'visible' });
      assert.equal(
        writes.length,
        0,
        'cancelling unselected route options makes no write',
      );
      assert.deepEqual(await page.locator('#fleet-map').boundingBox(), before);
      await assertTruckInformation(page, `${name}-cancelled`);
      assert.equal(
        await page
          .locator('#fleet-map-details')
          .evaluate(element =>
            window.routeFixture.truckPanels.every(
              (panel, index) => panel === element.children[index],
            ),
          ),
        true,
        `${name}: cancelling retains the mounted truck information panels`,
      );
      assert.deepEqual(
        loadReads,
        readsBeforeCancel,
        `${name}: restoring truck information does not reload its data`,
      );
      await opener.click();
      await editor.locator('.route-editor__option').nth(1).waitFor();
      await page.evaluate(() => window.routeFixture.select(2));
      assert.equal(
        await editor
          .locator('.route-editor__option')
          .nth(1)
          .getAttribute('aria-pressed'),
        'true',
      );
      assert.equal(writes.length, 0);
      const selectionUpdates = await page.evaluate(
        () => window.routeFixture.updates,
      );
      assert.equal(selectionUpdates[0].geometry, true);
      assert.equal(
        selectionUpdates.at(-1).geometry,
        false,
        'selection sends no geometry through Blazor interop',
      );
      assert.ok(selectionUpdates.at(-1).bytes < 512);
      await editor
        .getByRole('button', { name: 'Edit route', exact: true })
        .click();
      await editor
        .getByLabel('City or address', { exact: true })
        .fill('Columbia, SC');
      await editor.getByRole('button', { name: 'Add', exact: true }).click();
      await editor.locator('.route-editor__via').waitFor();
      await page.evaluate(() => window.routeFixture.drag());
      await editor.locator('.route-editor__via').nth(1).waitFor();
      await editor.screenshot({ path: resolve(output, `${name}-edit.png`) });
      const bounds = await editor.evaluate(node => {
        const box = node.getBoundingClientRect(),
          stage = document.querySelector('#fleet-map').getBoundingClientRect();
        return {
          x: box.x,
          y: box.y,
          right: box.right,
          width: box.width,
          stage: {
            x: stage.x,
            y: stage.y,
            right: stage.right,
            width: stage.width,
          },
          overflow: node.scrollWidth > node.clientWidth + 1,
        };
      });
      assert.equal(
        bounds.overflow,
        false,
        name + ': no horizontal editor overflow',
      );
      assert.ok(
        bounds.x >= bounds.stage.x - 1 &&
          bounds.right <= bounds.stage.right + 1,
      );
      if (width < 768) {
        assert.ok(Math.abs(bounds.width - bounds.stage.width) <= 1);
        assert.ok(Math.abs(bounds.y - bounds.stage.y) <= 1);
      }
      await page.screenshot({
        path: resolve(output, `${name}-page.png`),
        fullPage: true,
      });
      assert.deepEqual(
        await page.locator('#fleet-map').boundingBox(),
        before,
        name + ': same mounted map bounds',
      );
      await editor
        .getByRole('button', { name: 'Use this route', exact: true })
        .click();
      await editor.waitFor({ state: 'detached' });
      await page.locator('.fleet-map-inspector').waitFor({ state: 'visible' });
      await assertTruckInformation(page, `${name}-saved`);
      assert.equal(writes.length, 1);
      assert.deepEqual(writes[0], {
        previewId: id(6 + previews.length),
        option: 1,
        revision: 2,
        executionLegId: null,
      });
      assert.equal(previews.at(-1).viaPoints.length, 2);
      report.cases.push({
        name,
        bounds,
        previews: previews.length,
        simulatedSaves: writes.length,
        selectionUpdates,
      });
      await context.close();
    }
  assert.deepEqual(report.errors, []);
  assert.deepEqual(report.unexpected, []);
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  console.log(output);
}
