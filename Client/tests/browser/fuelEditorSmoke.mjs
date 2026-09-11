import assert from 'node:assert/strict';
import {browserOutput} from '../../../scripts/artifacts.mjs';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {chromium} from 'playwright';
import {installReleaseArtifact} from './releaseArtifact.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR, 'MAP_TEST_ARTIFACT_DIR must identify staged wwwroot');
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('fuel-editor', process.env.FUEL_EDITOR_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const uuid = number => `11111111-1111-1111-1111-${String(number).padStart(12, '0')}`;
const userId = uuid(1), truckId = uuid(2), dispatchId = uuid(3), stopId = uuid(4), routeId = uuid(5);
const stations = [6, 7, 8].map(uuid), before = [9, 10, 11].map(uuid);
const names = ['LOVES #706', 'LOVES #306', 'LOVES #333'];
const originalToken = '2026-09-10T12:00:00Z';
const point = {latitude: 39.2, longitude: -79.1};
const truck = {truckId, unitNumber: '54777', driverName: 'Fixture Driver', trailerNumber: 'GG1030',
  ...point, speed: 0, heading: 90, engineState: 'Off', fuelPercent: 46, updatedAt: originalToken};
const stop = {id: stopId, sequence: 1, job: 'Delivery', name: 'Fixture receiving facility', city: 'Fort Mill',
  address: '308 Springhill Farm Rd, Fort Mill, SC 29715, US', province: 'SC', country: 'US',
  scheduledDate: '2026-09-10', scheduledTime: '16:00:00', ...point};
const initialEdits = [{stationId: stations[0], beforeStopId: before[0], buyGallons: 0, fillToTarget: true},
  {stationId: stations[1], beforeStopId: before[1], buyGallons: 70, fillToTarget: false}];
const routeStops = before.map((id, index) => ({id, sequence: index + 1, point,
  name: ['Fort Mill, SC', 'Greensboro, NC', 'Amsterdam, NY'][index],
  address: ['308 Springhill Farm Rd, Fort Mill, SC', '100 Main St, Greensboro, NC', '1730 NY-5S, Amsterdam, NY'][index],
  job: index === 1 ? 'Pickup' : 'Delivery'}));
const segments = routeStops.map((beforeStop, index) => ({beforeStopId: beforeStop.id,
  afterStop: routeStops[index - 1] ?? null, beforeStop, dispatchId}));
const success = response => ({success: true, response, errors: []});
const luminance = color => color.match(/[\d.]+/g).slice(0, 3).map(Number)
  .map(value => value / 255).map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4)
  .reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
async function dragStop(page, context, editor, stationName, target, touch, screenshot) {
  const handle = editor.getByRole('button', {name: `Move ${stationName}`, exact: true});
  const list = editor.locator('.fuel-plan-editor__stops');
  await list.scrollIntoViewIfNeeded();
  const sourceElement = await handle.elementHandle();
  const targetElement = await target.elementHandle();
  await list.evaluate((surface, {source, target}) => {
    const start = source.closest('.fuel-plan-editor__stop').getBoundingClientRect();
    const end = target.getBoundingClientRect();
    const bounds = surface.getBoundingClientRect();
    surface.scrollTop += Math.min(start.top, end.top) - bounds.top - 4;
  }, {source: sourceElement, target: targetElement});
  const sourceBox = await handle.boundingBox(), targetBox = await target.boundingBox(), listBox = await list.boundingBox();
  const start = {x: sourceBox.x + sourceBox.width / 2, y: sourceBox.y + sourceBox.height / 2};
  const end = {x: targetBox.x + targetBox.width / 2, y: targetBox.y + targetBox.height * .75};
  for (const point of [start, end]) assert.ok(point.y > listBox.y && point.y < listBox.y + listBox.height,
    'drag endpoints must be in the visible timeline');
  await list.evaluate(surface => surface.addEventListener('pointerdown', event => {
    window.fuelFixture.lastDragInput = {input: event.pointerType, trusted: event.isTrusted};
  }, {once: true, capture: true}));
  let cdp;
  if (touch) {
    cdp = await context.newCDPSession(page);
    await cdp.send('Input.dispatchTouchEvent', {type: 'touchStart', touchPoints: [{...start, id: 1}]});
  } else {
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
  }
  for (let step = 1; step <= 12; step++) {
    const point = {x: start.x + (end.x - start.x) * step / 12, y: start.y + (end.y - start.y) * step / 12};
    if (touch) await cdp.send('Input.dispatchTouchEvent', {type: 'touchMove', touchPoints: [{...point, id: 1}]});
    else await page.mouse.move(point.x, point.y);
  }
  assert.equal(await editor.locator('.fuel-plan-editor__stop.is-dragging').count(), 1, 'real pointer event starts a drag');
  assert.equal(await target.evaluate(element => element.classList.contains('drop-after')), true, 'drop preview follows the fixed anchor or row');
  if (screenshot) await editor.screenshot({path: screenshot});
  if (touch) {
    await cdp.send('Input.dispatchTouchEvent', {type: 'touchEnd', touchPoints: []});
    await cdp.detach();
  } else await page.mouse.up();
  await sourceElement.dispose();
  await targetElement.dispose();
  const actualInput = await page.evaluate(() => window.fuelFixture.lastDragInput);
  assert.deepEqual(actualInput, {input: touch ? 'touch' : 'mouse', trusted: true});
  return actualInput;
}
const mapStub = `export async function createFleetMap(element, _key, callbacks) {
  element.style.background = 'var(--ui-surface-muted)';
  const fixture = window.fuelFixture = { selectStation(stationId, name, beforeStopId = null, addNew = false, dispatch = '${dispatchId}') {
    return callbacks.invokeMethodAsync('OnFuelStationEdit', '${truckId}', dispatch, stationId, name, beforeStopId, addNew);
  }};
  return {setOptions(){},setTrucks(){},setTrucksVisible(){},setStationsVisible(){},setTrafficVisible(){},setIfta(){},
    clearSelection(){},clearNextLoads(){},setNextLoadsVisible(){},clearNextLoadSelection(){},closeStationPopup(){},
    setStopEtas(){},setLoadReference(){},setFollow(){},finishInitialView(){},setInspectorMode(){},clearMapInspection(){},
    setFuelEditorTruck(id){fixture.editingTruck = id;},
    focusFuelStation(station){fixture.focusedStation = station;},
    clearFuelStationFocus(){fixture.focusedStation = null;},
    setRouteBytes(bytes){fixture.plan = JSON.parse(new TextDecoder().decode(bytes));return true;},
    setNextLoadsBytes(){},focusTruck(){return true;},dispose(){delete window.fuelFixture;}};
}`;
const stubIntegrity = `sha256-${createHash('sha256').update(mapStub).digest('base64')}`;
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g, (_all, open, json, close) => {
    const map = JSON.parse(json);
    for (const name of Object.keys(map.integrity ?? {}))
      if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(name)) map.integrity[name] = stubIntegrity;
    return open + JSON.stringify(map) + close;
  });
const report = {artifact, scope: 'Actual staged Blazor fuel editor, intercepted deterministic API and map-selection callbacks. No database, live writes, provider, GPU or calculation accuracy checks.',
  cases: [], errors: [], unexpectedRequests: []};
const browser = await chromium.launch({headless: true, channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome'});
await mkdir(output, {recursive: true});

try {
  for (const {width, height} of [{width: 1440, height: 1000}, {width: 390, height: 844},
    {width: 390, height: 667}, {width: 320, height: 667}]) for (const theme of ['light', 'dark']) {
    const mobile = width < 768;
    const name = `${width}-${height}-${theme}`;
    if (process.env.FUEL_EDITOR_CASE && process.env.FUEL_EDITOR_CASE !== name) continue;
    let savedEdits = structuredClone(initialEdits), savedToken = originalToken, manuallyEdited = false;
    const editsSeen = [], writes = [];
    function preview(edits) {
      const canonical = edits.map(edit => ({...edit, beforeStopId: edit.beforeStopId ?? before[stations.indexOf(edit.stationId)],
        purchaseLimitGallons: 177.3}));
      const errors = canonical.some(edit => !edit.fillToTarget && edit.buyGallons === 10)
        ? ['Not enough fuel to reach the following stop. Increase the purchase or add a station.'] : [];
      const plan = {truckId, calculatedAt: savedToken, pricingDate: '2026-09-10', manuallyEdited,
        dispatchIds: [dispatchId], needsRefresh: false, purchaseGallons: 247, purchaseCostUsd: 765.43,
        arrivalGallons: 51, startingGallons: 97, remainingMiles: 645,
        stops: canonical.map((edit, index) => ({...edit, number: index + 1, dispatchId,
          name: names[stations.indexOf(edit.stationId)], point,
          address: '3499 Lee Jackson Hwy, Staunton, VA 24401, USA',
          arrivalGallons: 34, departureGallons: edit.fillToTarget ? 211.3 : 34 + edit.buyGallons,
          buyGallons: edit.fillToTarget ? 177.3 : edit.buyGallons, milesAhead: (index + 1) * 100,
          purchaseCostUsd: 123.45,
          yourPrice: 5.613, cashUsdPerGallon: 5.613, economicUsdPerGallon: 5.286, currency: 'USD', unit: 'US gal'}))};
      return {plan, stops: canonical, expectedCalculatedAt: savedToken, tankGallons: 211.3,
        fillLimitGallons: 211.3, errors, valuesAvailable: true, segments};
    }
    function planning() {
      const fuel = preview(savedEdits).plan;
      const plan = {id: routeId, dispatchId, truckId, version: 1, calculatedAt: originalToken,
        originalPlannedMiles: 871, fromCurrentPosition: true, profile: {}, fuelPlan: fuel,
        stops: [{...stop, point}], tracking: {nextStopId: stopId, passedStopIds: [], visitedStops: {}, allStopsPassed: false},
        route: {miles: 871, seconds: 30000, warnings: [], points: [], legs: [{miles: 871, seconds: 30000, points: [point, {latitude: 35.1, longitude: -80.9}]}]}};
      return {truckId, dispatchId, loadNumber: 1375,
        hos: {breakMs: 214 * 60000, driveMs: 151 * 60000, shiftMs: 151 * 60000,
          cycleMs: 3659 * 60000, updatedAt: originalToken, currentDutyStatus: 'driving'},
        state: {profile: {tankGallons: 211.3}, plan,
        apiConfigured: false, fuelPercent: 46, fuelUpdatedAt: originalToken,
        progress: {progressMiles: 226, remainingMiles: 645, remainingSeconds: 7200, position: point}}};
    }
    const context = await browser.newContext({viewport: {width, height}, colorScheme: theme, hasTouch: mobile,
      locale: 'en-US', timezoneId: 'America/Toronto', reducedMotion: 'reduce', serviceWorkers: 'block'});
    await context.addInitScript(({userId, theme}) => {
      localStorage.setItem('auth_session', JSON.stringify({Id: userId, AccessToken: 'fixture', RefreshToken: 'fixture'}));
      document.addEventListener('DOMContentLoaded', () => {document.documentElement.dataset.theme = theme;});
    }, {userId, theme});
    await installReleaseArtifact(context, artifact, origin);
    await context.route('**/*', async route => {
      const request = route.request(), url = new URL(request.url());
      if (url.origin !== origin) { report.unexpectedRequests.push(`${request.method()} ${url.origin}${url.pathname}`); return route.abort('blockedbyclient'); }
      if (url.pathname.startsWith('/api/')) {
        let value;
        if (url.pathname === '/api/auth/me') value = {id: userId, name: 'Fixture Administrator', email: 'fixture@example.invalid', isAdmin: true};
        else if (url.pathname === '/api/settings/dispatch') value = success({loadNumberPrefix: 'AMF', revision: 1});
        else if (url.pathname === '/api/fleet/locations') value = success({trucks: [truck], points: [truck]});
        else if (url.pathname === '/api/fleet/planning/previews') value = success([]);
        else if (url.pathname === `/api/fleet/trucks/${truckId}/planning/preview`
          || url.pathname === `/api/fleet/trucks/${truckId}/planning`) value = success(planning());
        else if (url.pathname === `/api/dispatch/${dispatchId}`) value = success({id: dispatchId, truckId,
          loadNumber: 1375, orderNumber: '567086821', status: 'in_transit', stops: [stop]});
        else if (url.pathname === `/api/dispatch/${dispatchId}/planning/fuel/edit/preview` && request.method() === 'POST') {
          const body = request.postDataJSON(); editsSeen.push(body);
          value = success(preview(body.stops ?? savedEdits));
        } else if (url.pathname === `/api/dispatch/${dispatchId}/planning/fuel/edit` && request.method() === 'PUT') {
          const body = request.postDataJSON(); writes.push(body);
          assert.equal(body.expectedCalculatedAt, savedToken, `${name}: changed draft concurrency token`);
          savedEdits = structuredClone(body.stops); savedToken = '2026-09-10T12:01:00Z'; manuallyEdited = true;
          value = success(preview(savedEdits));
        } else if (url.pathname === `/api/dispatch/${dispatchId}/planning/fuel/reset` && request.method() === 'POST') {
          writes.push({reset: true, ...request.postDataJSON()});
          savedEdits = structuredClone(initialEdits); manuallyEdited = false; value = success(planning());
        }
        if (value === undefined) {report.unexpectedRequests.push(`${request.method()} ${url.pathname}`); return route.abort('blockedbyclient');}
        return route.fulfill({status: 200, json: value});
      }
      if (!['GET', 'HEAD'].includes(request.method())) {report.unexpectedRequests.push(`${request.method()} ${url.pathname}`); return route.abort('blockedbyclient');}
      if (/\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(url.pathname))
        return route.fulfill({status: 200, contentType: 'text/javascript', body: mapStub});
      if (request.isNavigationRequest()) return route.fulfill({status: 200, contentType: 'text/html', body: html});
      return route.fallback();
    });
    const page = await context.newPage();
    page.on('pageerror', error => report.errors.push(`${name}: ${error.message}`));
    page.on('console', message => {if (message.type() === 'error') report.errors.push(`${name}: ${message.text()}`);});
    await page.goto(`${origin}/fleet/map?truckId=${truckId}`);
    await page.evaluate(() => {
      window.fuelHorizontalEvents = [];
      document.addEventListener('scroll', () => {
        if (document.documentElement.scrollLeft) window.fuelHorizontalEvents.push({
          left: document.documentElement.scrollLeft, active: document.activeElement?.className,
          phase: window.fuelTestPhase,
        });
      }, true);
    });
    await page.locator('.fleet-map-route-info [title="Copy full address"]').waitFor({state: 'attached'});
    if (!mobile) {
      await page.locator('.driver-hours__clock[aria-label="Cycle: 60:59 remaining"]').waitFor();
      const mapBeforeProbes = await page.locator('#fleet-map').boundingBox();
      for (const probeWidth of [320, 390, 768, 1200, 1440, 2000]) for (const scale of [100, 200]) {
        await page.setViewportSize({width: probeWidth, height});
        await page.evaluate(scale => {document.documentElement.style.fontSize = scale + '%';}, scale);
        const detailsToggle = page.locator('.fleet-map-mobile-summary__toggle');
        if (await detailsToggle.isVisible() && await detailsToggle.getAttribute('aria-expanded') === 'false') await detailsToggle.click();
        const dials = await page.locator('.fleet-map-truck-info .driver-hours__clock .driver-hours__dial')
          .evaluateAll(elements => elements.map(element => {
            const dial = element.getBoundingClientRect(), text = element.querySelector('strong').getBoundingClientRect();
            return {text: element.querySelector('strong').textContent, diameter: dial.width,
              corner: Math.hypot(text.width / 2, text.height / 2),
              centered: Math.abs((text.left + text.right - dial.left - dial.right) / 2)};
          }));
        assert.equal(dials.length, 4);
        for (const dial of dials) assert.ok(dial.centered <= 1 && dial.corner < dial.diameter * 25.5 / 64 - 1,
          `${name}/${probeWidth}/${scale}: HOS value stays clear of the ring: ${JSON.stringify(dial)}`);
        await page.locator('.fleet-map-truck-info__hours').evaluate(node => node.style.setProperty('--hos-dial-size', '72px'));
        await page.waitForFunction(() => {
          const dials = [...document.querySelectorAll('.fleet-map-truck-info__hours .driver-hours__dial')];
          return dials.length === 4 && dials.every(dial => Math.abs(dial.getBoundingClientRect().width - 72) <= 1);
        }, null, {timeout: 5000});
        await page.locator('.fleet-map-truck-info__hours').evaluate(node => node.style.removeProperty('--hos-dial-size'));
      }
      await page.setViewportSize({width, height});
      await page.evaluate(() => {document.documentElement.style.fontSize = '100%';});
      await page.waitForFunction(expected => {
        const actual = document.querySelector('#fleet-map').getBoundingClientRect();
        return ['x', 'y', 'width', 'height'].every(key => Math.abs(actual[key] - expected[key]) <= 1);
      }, mapBeforeProbes, {timeout: 5000});
      const metrics = await page.locator('.fleet-map-truck-info__reading').evaluateAll(readings => readings.map(reading => {
        const value = reading.querySelector('.fuel-reading__value') ?? reading.querySelector(':scope > strong');
        const icon = reading.querySelector('svg');
        return {top: value.getBoundingClientRect().top, font: getComputedStyle(value).fontSize,
          lineHeight: getComputedStyle(value).lineHeight, iconHeight: icon.getBoundingClientRect().height};
      }));
      assert.equal(metrics.length, 3);
      for (const metric of metrics) {
        assert.ok(Math.abs(metric.top - metrics[0].top) <= 1, `${name}: telemetry values share one baseline`);
        assert.equal(metric.font, metrics[0].font);
        assert.equal(metric.lineHeight, metrics[0].lineHeight);
        assert.equal(metric.iconHeight, metrics[0].iconHeight);
      }
    }
    const mapBefore = await page.locator('#fleet-map').boundingBox();
    const scrollBefore = await page.evaluate(() => ({x: scrollX, y: scrollY}));
    await page.locator('#fleet-map').evaluate(element => {window.fuelFixture.mapElement = element;});
    await page.evaluate(({station, name}) => window.fuelFixture.selectStation(station, name), {station: stations[0], name: names[0]});
    const editor = page.locator('.fuel-plan-editor');
    const view = label => editor.getByRole('group', {name: 'Fuel editor view'}).getByRole('button', {name: label, exact: true});
    await editor.locator('.fuel-plan-editor__stop').nth(1).waitFor({state: 'attached'});
    assert.equal(await page.locator('.fleet-map-page__background').evaluate(element => element.inert), false);
    assert.equal(await page.locator('.fleet-map-page__background').getAttribute('aria-hidden'), null);
    assert.equal(await page.locator('#fleet-map').evaluate(element => element.closest('[inert]')), null);
    await page.locator('.fleet-map-toolbar input').first().focus();
    assert.deepEqual(await page.locator('#fleet-map').boundingBox(), mapBefore, `${name}: toolbar focus must not move the map`);
    assert.equal(await page.evaluate(() => Boolean(document.activeElement?.closest('.fleet-map-page__background'))), true,
      `${name}: visible background controls remain keyboard accessible beside a nonmodal card`);
    await editor.getByRole('button', {name: 'Close fuel plan editor'}).focus();
    assert.equal(await editor.locator('input[type="range"]').getAttribute('step'), '10');
    assert.equal(await editor.locator('.fleet-fuel-visit__percent').last().innerText(), '100%');
    assert.equal(await editor.getByRole('slider', {name: 'Gallons to buy'}).isEnabled(), true);
    assert.equal(await editor.getByRole('slider', {name: 'Gallons to buy'}).getAttribute('max'), '180');
    assert.equal((await editor.locator('label[for="fuel-edit-quantity"]').innerText()).trim(), 'Quantity');
    assert.equal(await editor.locator('.fuel-plan-editor__anchor').count(), 3);
    assert.equal(await editor.getByRole('combobox', {name: 'Fuel stop order'}).count(), 0);
    assert.ok((await editor.innerText()).includes('$123.45'), 'server-provided cost is displayed');
    assert.equal(await editor.locator('.fuel-plan-editor__price strong').innerText(), '5.613 USD / US gal');
    const badgeColors = await editor.locator('.fuel-plan-editor__stop-heading .fleet-fuel-visit__number').first()
      .evaluate(element => ({color: getComputedStyle(element).color, background: getComputedStyle(element).backgroundColor}));
    const badgeLuminance = [luminance(badgeColors.color), luminance(badgeColors.background)].sort((a, b) => a - b);
    assert.ok((badgeLuminance[1] + .05) / (badgeLuminance[0] + .05) >= 4.5, `${name}: fuel number contrast`);
    await page.waitForFunction(station => window.fuelFixture.focusedStation?.stationId === station, stations[0]);
    assert.equal(writes.length, 0);
    if (mobile) {
      assert.equal(await editor.locator('.fuel-plan-editor__timeline').isVisible(), false);
      await view('Route').click();
      assert.equal(await editor.locator('.fuel-plan-editor__content').isVisible(), false);
    }
    const initialTimeline = await editor.locator('.fuel-plan-editor__stops').evaluate(list => {
      const bounds = list.getBoundingClientRect();
      const entries = [...list.querySelectorAll('[data-reorder-key]')];
      return {height: bounds.height, visible: entries.filter(entry => {
        const box = entry.getBoundingClientRect();
        return box.top >= bounds.top - 1 && box.bottom <= bounds.bottom + 1;
      }).map(entry => entry.dataset.reorderKey), total: entries.length};
    });
    await editor.screenshot({path: resolve(output, `${name}-initial.png`)});
    if (width === 1440) {
      assert.equal(initialTimeline.visible.length, initialTimeline.total,
        `${name}: the whole three-stop route and both fuel rows must be visible together without scrolling`);
      assert.ok(initialTimeline.height >= 480, `${name}: route timeline uses the full editor body height`);
    } else assert.ok(initialTimeline.visible.length >= 2,
      `${name}: mobile route pane must show multiple complete draggable/anchor rows: ${JSON.stringify(initialTimeline)}`);
    if (mobile) await view('Fuel details').click();
    const purchaseSlider = editor.getByRole('slider', {name: 'Gallons to buy'});
    await page.evaluate(() => {window.fuelTestPhase = 'slider';});
    await purchaseSlider.focus();
    await purchaseSlider.press('ArrowLeft');
    await page.waitForFunction(() => document.querySelector('.fuel-plan-editor__full input')?.checked === false
      && !document.querySelector('.fuel-plan-editor button.fuel-plan-editor__save')?.disabled);
    assert.equal(editsSeen.at(-1).stops[0].buyGallons, 170, `${name}: one tick before Full tank fits the server's 177.3-gallon purchase limit`);
    assert.equal(editsSeen.at(-1).stops[0].fillToTarget, false);
    await purchaseSlider.press('End');
    await page.waitForFunction(() => document.querySelector('.fuel-plan-editor__full input')?.checked === true
      && !document.querySelector('.fuel-plan-editor button.fuel-plan-editor__save')?.disabled);
    assert.equal(editsSeen.at(-1).stops[0].fillToTarget, true);
    const gestures = [];
    await page.evaluate(() => {window.fuelTestPhase = 'drag';});
    if (mobile) await view('Route').click();
    const anchorInput = await dragStop(page, context, editor, names[0], editor.locator(`[data-reorder-key="stop:${before[0]}"]`), mobile,
      resolve(output, `${name}-drag-anchor.png`));
    await page.waitForFunction(() => document.querySelector('.fuel-plan-editor__position')?.textContent.includes('After Delivery'));
    assert.deepEqual(editsSeen.at(-1).stops.map(edit => edit.stationId), stations.slice(0, 2));
    assert.deepEqual(editsSeen.at(-1).stops.map(edit => edit.beforeStopId), [before[1], before[1]]);
    gestures.push({...anchorInput, target: 'fixed delivery anchor', beforeStopId: before[1]});
    const rowInput = await dragStop(page, context, editor, names[0], editor.locator('.fuel-plan-editor__stop').filter({hasText: names[1]}), mobile);
    await page.waitForFunction(() => document.querySelector('.fuel-plan-editor__stop strong')?.textContent === 'LOVES #306');
    assert.deepEqual(editsSeen.at(-1).stops.map(edit => edit.stationId), [stations[1], stations[0]]);
    assert.deepEqual(editsSeen.at(-1).stops.map(edit => edit.beforeStopId), [before[1], before[1]]);
    assert.equal(editsSeen.at(-1).stops[0].buyGallons, 70);
    assert.equal(editsSeen.at(-1).stops[1].fillToTarget, true);
    assert.equal(await editor.locator('.is-dragging, .drop-after, .drop-before').count(), 0);
    gestures.push({...rowInput, target: 'fuel row', order: [stations[1], stations[0]]});
    if (mobile) {
      await editor.locator('.fuel-plan-editor__stop-select').first().click();
      assert.equal(await view('Fuel details').getAttribute('aria-pressed'), 'true');
    }
    await editor.locator('.fuel-plan-editor__cost').scrollIntoViewIfNeeded();
    await page.evaluate(() => {window.fuelTestPhase = 'controls';});
    await editor.screenshot({path: resolve(output, `${name}-selected-controls.png`)});
    assert.equal(await editor.getByRole('slider', {name: 'Gallons to buy'}).isVisible(), true);
    assert.equal(await editor.getByRole('checkbox', {name: 'Full tank'}).isVisible(), true);
    for (const selector of ['.fleet-fuel-visit__levels', 'input[type="range"]', '.fuel-plan-editor__cost']) {
      const control = editor.locator(selector);
      await control.scrollIntoViewIfNeeded();
      assert.equal(await control.evaluate(element => {
        const content = element.closest('.fuel-plan-editor__content').getBoundingClientRect();
        const bounds = element.getBoundingClientRect();
        return bounds.top >= content.top - 1 && bounds.bottom <= content.bottom + 1;
      }), true, `${name}: ${selector} is fully reachable inside the compact details scroller`);
    }
    assert.equal(writes.length, 0, `${name}: dragging only previews the draft`);
    await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await page.evaluate(() => {window.fuelTestPhase = 'reopen';});
    await editor.waitFor({state: 'detached'});
    assert.equal(await page.locator('.fleet-map-page__background').evaluate(element => element.inert), false);
    await page.evaluate(({station, name}) => window.fuelFixture.selectStation(station, name), {station: stations[0], name: names[0]});
    await editor.locator('.fuel-plan-editor__stop').nth(1).waitFor({state: 'attached'});
    assert.deepEqual(savedEdits, initialEdits, `${name}: cancelling drag restores the saved plan`);
    await page.evaluate(({station, name}) => window.fuelFixture.selectStation(station, name), {station: stations[2], name: names[2]});
    await editor.locator('.fuel-plan-editor__stop').nth(2).waitFor({state: 'attached'});
    if (mobile) await view('Route').click();
    const handle = editor.getByRole('button', {name: `Move ${names[2]}`, exact: true});
    await handle.focus();
    await handle.press('ArrowUp');
    await page.waitForFunction(() => document.querySelector('.fuel-plan-editor__stop strong')?.textContent === 'LOVES #333');
    assert.equal(editsSeen.at(-1).stops[0].beforeStopId, before[0]);
    await page.waitForFunction(station => window.fuelFixture.focusedStation?.stationId === station, stations[2]);
    if (mobile) await view('Fuel details').click();
    const slider = editor.getByRole('slider', {name: 'Gallons to buy'});
    await slider.evaluate(element => {element.value = '10'; element.dispatchEvent(new Event('input', {bubbles: true}));});
    assert.equal(await editor.getByRole('checkbox', {name: 'Full tank'}).isChecked(), false);
    await editor.getByRole('alert').waitFor();
    assert.equal(await editor.getByRole('button', {name: 'Save plan', exact: true}).isEnabled(), false);
    assert.equal(await editor.locator('.fleet-fuel-visit__percent').first().innerText(), '16%');
    await editor.screenshot({path: resolve(output, `${name}-validation.png`)});
    await slider.evaluate(element => {element.value = '30'; element.dispatchEvent(new Event('input', {bubbles: true}));});
    await page.waitForFunction(() => !document.querySelector('.fuel-plan-editor button.fuel-plan-editor__save')?.disabled);
    assert.equal(editsSeen.at(-1).stops[0].buyGallons, 30);
    assert.equal(editsSeen.at(-1).expectedCalculatedAt, originalToken);
    await slider.evaluate(element => {element.value = element.max; element.dispatchEvent(new Event('input', {bubbles: true}));});
    await page.waitForFunction(() => document.querySelector('.fuel-plan-editor__full input')?.checked === true);
    await page.waitForFunction(() => !document.querySelector('.fuel-plan-editor button.fuel-plan-editor__save')?.disabled);
    assert.equal(editsSeen.at(-1).stops[0].fillToTarget, true);
    await slider.evaluate(element => {element.value = '30'; element.dispatchEvent(new Event('input', {bubbles: true}));});
    await page.waitForFunction(() => !document.querySelector('.fuel-plan-editor button.fuel-plan-editor__save')?.disabled);
    const geometry = await editor.evaluate(element => {
      const rect = element.getBoundingClientRect();
      const map = document.querySelector('#fleet-map').getBoundingClientRect();
      const timeline = element.querySelector('.fuel-plan-editor__timeline').getBoundingClientRect();
      const content = element.querySelector('.fuel-plan-editor__content').getBoundingClientRect();
      const footer = element.querySelector('.fuel-plan-editor__footer').getBoundingClientRect();
      const mapReceivesPointer = [.1, .35, .65, .9].some(x => [.1, .35, .65, .9].some(y =>
        document.elementFromPoint(map.left + map.width * x, map.top + map.height * y)?.closest('#fleet-map')));
      return {left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width,
        map: {x: map.x, y: map.y, width: map.width, height: map.height},
        mapRetained: window.fuelFixture.mapElement === document.querySelector('#fleet-map'),
        mapReceivesPointer,
        timeline: {left: timeline.left, right: timeline.right, top: timeline.top, bottom: timeline.bottom, height: timeline.height},
        content: {left: content.left, top: content.top, right: content.right, bottom: content.bottom}, footerTop: footer.top,
        mapCount: document.querySelectorAll('#fleet-map').length,
        documentWidth: document.documentElement.scrollWidth, viewport: innerWidth,
        contentWidth: element.querySelector('.fuel-plan-editor__content').clientWidth,
        horizontalScrollers: [...document.querySelectorAll('*')].filter(node => node.scrollLeft).map(node =>
          ({tag: node.tagName, className: node.className, left: node.scrollLeft, client: node.clientWidth, scroll: node.scrollWidth})),
        horizontalEvents: window.fuelHorizontalEvents,
        pageScroll: {x: scrollX, y: scrollY},
        scrollWidth: element.querySelector('.fuel-plan-editor__content').scrollWidth};
    });
    assert.ok(geometry.left >= 0 && geometry.right <= width + 1 && geometry.bottom <= height + 1, `${name}: editor is not viewport bounded`);
    assert.ok(geometry.top >= geometry.map.y && geometry.bottom <= geometry.map.y + geometry.map.height + 1,
      `${name}: editor must remain inside the map without covering the page heading or filters`);
    assert.ok(geometry.documentWidth <= width + 1 && geometry.scrollWidth <= geometry.contentWidth + 1, `${name}: horizontal overflow`);
    assert.equal(geometry.mapCount, 1, `${name}: editor does not create another map`);
    assert.equal(geometry.mapRetained, true, `${name}: editor retains the mounted main map`);
    for (const key of ['x', 'y', 'width', 'height']) {
      const beforeCoordinate = mapBefore[key] + (scrollBefore[key] ?? 0);
      const afterCoordinate = geometry.map[key] + (geometry.pageScroll[key] ?? 0);
      assert.ok(Math.abs(afterCoordinate - beforeCoordinate) <= 1,
        `${name}: editing must preserve map document bounds while allowing page scrolling: ${key}`);
    }
    if (width === 1440) {
      assert.ok(Math.abs((geometry.left + geometry.right) / 2 - (geometry.map.x + geometry.map.width / 2)) <= 1,
      `${name}: desktop editor is horizontally centered inside the map`);
      assert.ok(Math.abs(geometry.map.y + geometry.map.height - geometry.bottom - 12) <= 1,
      `${name}: desktop editor retains the bottom map inset`);
      assert.equal(geometry.mapReceivesPointer, true, `${name}: an uncovered part of the main map stays interactive`);
      assert.ok(geometry.width >= 800 && geometry.width <= 866, `${name}: desktop editing surface must be wide enough for two usable columns`);
      assert.ok(geometry.timeline.right <= geometry.content.left + 1
        && Math.abs(geometry.timeline.top - geometry.content.top) <= 1
        && Math.abs(geometry.timeline.bottom - geometry.content.bottom) <= 1,
      `${name}: full-height timeline stays beside selected station controls`);
    } else {
      assert.ok(Math.abs(geometry.top - geometry.map.y) <= 1, `${name}: no mobile top gap`);
      assert.ok(Math.abs(geometry.width - geometry.map.width) <= 1, `${name}: mobile uses available map width`);
      assert.ok(geometry.content.bottom - geometry.content.top >= 160, `${name}: selected controls have usable working height`);
      await view('Map').click();
      assert.equal(await editor.locator('.fuel-plan-editor__content').isVisible(), false);
      assert.equal(await editor.getByRole('button', {name: 'Save plan', exact: true}).isVisible(), false);
      assert.equal(await page.locator('#fleet-map').evaluate(map => {
        const bounds = map.getBoundingClientRect();
        return Boolean(document.elementFromPoint(bounds.left + bounds.width / 2, bounds.bottom - 20)?.closest('#fleet-map'));
      }), true, `${name}: Map view exposes the same map without closing the draft`);
      const previewsBefore = editsSeen.length;
      await view('Fuel details').click();
      assert.equal(await editor.getByRole('slider', {name: 'Gallons to buy'}).inputValue(), '30');
      assert.equal(editsSeen.length, previewsBefore, `${name}: changing views does not recalculate or save`);
    }
    assert.ok(geometry.content.bottom <= geometry.footerTop + 1, `${name}: fixed footer remains outside the detail scroller`);
    await editor.getByRole('button', {name: 'Save plan', exact: true}).click();
    await editor.waitFor({state: 'detached'});
    assert.equal(await page.evaluate(() => window.fuelFixture.focusedStation), null);
    assert.equal(writes.length, 1);
    assert.equal(savedEdits[0].stationId, stations[2]);
    assert.equal(savedEdits[0].buyGallons, 30);
    await page.evaluate(({station, name}) => window.fuelFixture.selectStation(station, name), {station: stations[2], name: names[2]});
    await editor.getByRole('button', {name: 'Remove selected fuel stop'}).click();
    await page.waitForFunction(() => document.querySelectorAll('.fuel-plan-editor__stop').length === 2);
    await editor.getByRole('button', {name: 'Cancel', exact: true}).click();
    await editor.waitFor({state: 'detached'});
    assert.equal(writes.length, 1, `${name}: Cancel submitted a write`);
    assert.equal(savedEdits.length, 3);
    await page.evaluate(({station, name}) => window.fuelFixture.selectStation(station, name), {station: stations[2], name: names[2]});
    await editor.waitFor();
    await page.keyboard.press('Escape');
    await editor.waitFor({state: 'detached'});
    assert.equal(writes.length, 1, `${name}: Escape submitted a write`);
    await page.evaluate(({station, name, wrongDispatch}) => window.fuelFixture.selectStation(station, name, null, false, wrongDispatch),
      {station: stations[0], name: names[0], wrongDispatch: uuid(99)});
    assert.equal(await editor.count(), 0, `${name}: stale truck/dispatch callback opened editor`);
    report.cases.push({name, geometry, initialTimeline, gestures, previews: editsSeen.length, writes: writes.length, passed: true});
    await context.close();
  }
} catch (error) {
  report.errors.push(error.stack ?? String(error));
} finally {
  await browser.close();
  await writeFile(resolve(output, 'report.json'), JSON.stringify(report, null, 2));
}
console.log(JSON.stringify(report, null, 2));
assert.equal(report.errors.length + report.unexpectedRequests.length, 0, 'Fuel editor smoke failed');
assert.equal(report.cases.length, process.env.FUEL_EDITOR_CASE ? 1 : 8);
