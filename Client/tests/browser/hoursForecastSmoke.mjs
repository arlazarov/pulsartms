import assert from 'node:assert/strict';
import {browserOutput} from '../../../scripts/artifacts.mjs';
import {createHash} from 'node:crypto';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {chromium} from 'playwright';
import {installReleaseArtifact} from './releaseArtifact.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR, 'MAP_TEST_ARTIFACT_DIR must identify a verified staged wwwroot');
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const fleetOnly = process.env.HOURS_TEST_FLEET_ONLY === '1';
const output = browserOutput('hours-forecast', process.env.HOURS_TEST_OUTPUT_DIR);
const widths = [2344, 1920, 1440, 1200, 390];
const themes = ['light', 'dark'];
const origin = 'http://localhost:5079';
const userId = '11111111-1111-1111-1111-111111111111';
const truckId = '22222222-2222-2222-2222-222222222222';
const currentId = '33333333-3333-3333-3333-333333333333';
const futureId = '44444444-4444-4444-4444-444444444444';
const stopIds = [1, 2, 3].map(index => `55555555-5555-5555-5555-${String(index).padStart(12, '0')}`);
const now = '2026-09-08T12:00:00Z';
const point = (latitude, longitude) => ({latitude, longitude});
const stops = [
  {id: stopIds[0], sequence: 1, job: 'Delivery', name: 'Current receiving facility', city: 'Toronto', address: '100 Current Street, Toronto, ON, Canada',
    scheduledDate: '2026-09-08', scheduledTime: '16:00:00', latitude: 43.65, longitude: -79.38},
  {id: stopIds[1], sequence: 1, job: 'Pickup', name: 'East logistics terminal', city: 'Kingston', address: '200 Future Avenue',
    scheduledDate: '2026-09-11', scheduledTime: '08:00:00', latitude: 44.23, longitude: -76.48},
  {id: stopIds[2], sequence: 2, job: 'Delivery', name: 'Capital distribution centre', city: 'Ottawa', address: '300 Receiving Road',
    scheduledDate: '2026-09-11', scheduledTime: '18:00:00', latitude: 45.42, longitude: -75.70}
].map(stop => ({...stop, province: 'ON', country: 'Canada'}));
const recap = {remainingMinutes: 600, nextRecapAt: '2026-09-09T00:00:00-04:00', nextRecapMinutes: 185,
  homeTimeZoneId: 'America/Toronto', recapVerified: true};
const estimate = (stop, index) => {
  const arrival = `${stop.scheduledDate}T${index === 2 ? '19:05:00' : stop.scheduledTime}-04:00`;
  return {dispatchId: index === 0 ? currentId : futureId, stopId: stop.id, arrival,
    timeZoneId: 'America/Toronto', appointment: `${stop.scheduledDate}T${stop.scheduledTime}-04:00`,
    lateMinutes: index === 2 ? 65 : 0, drivingMinutes: 600, restMinutes: 120,
    cycleAfterDeparture: recap,
    hours: index === 2
      ? {cycleAtArrivalMinutes: null, cycleAfterStopMinutes: null, drivingShortfallMinutes: null,
        firstCycleShortageAt: null, cycleVerified: false, alternatives: [], unavailableReason: 'Unverified cycle history fixture.'}
      : {cycleAtArrivalMinutes: -180, cycleAfterStopMinutes: index === 0 ? -180 : -300, drivingShortfallMinutes: 180,
        firstCycleShortageAt: '2026-09-08T15:00:00-04:00', cycleVerified: true, unavailableReason: null,
        alternatives: index === 0 ? [] : [
          {kind: 'recap', arrival: '2026-09-11T10:00:00-04:00', departure: '2026-09-11T12:00:00-04:00',
            lateMinutes: 120, cycleAfterStopMinutes: 100, restStartedAt: now, resumeAt: '2026-09-09T00:00:00-04:00'},
          {kind: 'restart', arrival: '2026-09-11T07:00:00-04:00', departure: '2026-09-11T10:00:00-04:00',
            lateMinutes: 0, cycleAfterStopMinutes: 2000, restStartedAt: now, resumeAt: '2026-09-09T22:00:00Z'}
        ]}};
};
const estimates = stops.map(estimate);
const shiftTime = (value, minutes) => {
  const offset = value.match(/(?:Z|[+-]\d{2}:\d{2})$/)[0];
  return new Date(Date.parse(`${value.slice(0, -offset.length)}Z`) + minutes * 60_000).toISOString().slice(0, 19) + offset;
};
const forecast = (values, pending = false, timing = {}) => ({calculatedAt: timing.calculatedAt ?? (pending ? '2026-09-08T12:01:00Z' : now),
  validUntil: timing.validUntil ?? '2026-09-08T14:00:00Z', stops: pending ? [] : values.map(value => timing.shiftMinutes
    ? {...value, arrival: shiftTime(value.arrival, timing.shiftMinutes), lateMinutes: value.lateMinutes + timing.shiftMinutes} : value), assumptions: [],
  unavailableReason: pending ? 'Route refresh pending.' : null, routeUpdatePending: pending, cycleAtCalculation: recap,
  dutyStatus: {status: 'sleeperBerth', statusStartedAt: '2026-09-08T05:52:00Z', restStartedAt: '2026-09-08T05:52:00Z',
    observedAt: now, cycleResetHours: 34, cycleResetCountry: 'US', cycleResetRemainingMinutes: 1672}});
const loads = (pending, timing) => [
  {id: currentId, truckId, loadNumber: 1441, orderNumber: 'CURRENT-1441', status: 'in_transit', stops: stops.slice(0, 1),
    eta: forecast(estimates.slice(0, 1), pending, timing), loadedMiles: 500, emptyMiles: 50, totalMiles: 550},
  {id: futureId, truckId, loadNumber: 1442, orderNumber: 'FUTURE-1442', status: 'planned', stops: stops.slice(1),
    eta: forecast(estimates.slice(1), pending, timing), loadedMiles: 300, emptyMiles: 40, totalMiles: 340}
].map(load => ({...load, customerName: pending ? 'Fixture Customer refreshed' : 'Fixture Customer',
  driverName: 'Fixture Driver', truckNumber: '11006', trailerNumber: 'TR-100'}));
const fuelPlan = {truckId, dispatchIds: [currentId, futureId], calculatedAt: '2026-09-08T10:15:00Z',
  pricingDate: '2026-09-08', selectionVersion: 11, routeVersion: 1, startProgressMiles: 380,
  needsRefresh: false, reusedCheckedRoute: true, refreshReasons: [], remainingMiles: 460,
  stops: [{stationId: '77777777-7777-7777-7777-777777777777', visitKey: 'future-fuel:1',
    dispatchId: futureId, beforeStopId: stopIds[1], name: 'Kingston travel stop',
    point: point(44.1, -76.8), currentRouteMile: null, milesAhead: 100,
    buyGallons: 35, arrivalGallons: 45, departureGallons: 80, fillToTarget: false}],
  scheduleImpact: {calculatedAt: '2026-09-08T10:15:00Z', complete: true, cycleKnown: true,
    baselineCycleShort: false, cycleShort: true, addedMinutes: 30, addedLateMinutes: 30,
    stops: [], unavailableReason: null}};
const plan = {id: '66666666-6666-6666-6666-666666666666', dispatchId: currentId, truckId,
  version: 1, calculatedAt: now, originalPlannedMiles: 500, fromCurrentPosition: true, profile: {}, fuelPlan,
  stops: [{...stops[0], point: point(stops[0].latitude, stops[0].longitude)}],
  tracking: {nextStopId: stopIds[0], passedStopIds: [], visitedStops: {}},
  route: {miles: 500, seconds: 30_000, warnings: [], points: [],
    legs: [{miles: 500, seconds: 30_000, points: [point(41.8, -87.6), point(43.65, -79.38)]}]}};
const planning = (pending, timing) => ({truckId, dispatchId: currentId, loadNumber: 1441,
  hos: {breakMs: 25_200_000, driveMs: 21_600_000, shiftMs: 28_800_000, cycleMs: 36_000_000,
    currentDutyStatus: 'sleeperBerth', updatedAt: now}, state: {profile: {}, plan,
  apiConfigured: false, fuelPercent: 75, fuelUpdatedAt: '2026-09-08T10:00:00Z', eta: forecast(estimates, pending, timing),
  progress: {progressMiles: 380, remainingMiles: 120, remainingSeconds: 7200, distanceFromRouteMiles: 0,
    offRoute: false, locationStale: false, locationTime: now, position: point(41.8, -87.6)}}});
const futureRoute = {id: futureId, loadNumber: 1442, status: 'planned', stopCount: 2,
  stops: stops.slice(1).map(stop => ({id: stop.id, latitude: stop.latitude, longitude: stop.longitude, job: stop.job, name: stop.name})),
  deadhead: {miles: 40, points: [point(43.65, -79.38), point(44.23, -76.48)]},
  legs: [{miles: 300, seconds: 18_000, points: [point(44.23, -76.48), point(45.42, -75.70)]}]};
const truck = {truckId, unitNumber: '11006', driverName: 'Fixture Driver', trailerNumber: 'TR-100',
  latitude: 41.8, longitude: -87.6, speed: 45, heading: 90, updatedAt: now, engineState: 'Driving', fuelPercent: 75};
const success = response => ({success: true, response, errors: []});
const viewportSource = await readFile(new URL('../../Scripts/fleetMap/ui/cameraViewport.js', import.meta.url), 'utf8');
const mapStub = `${viewportSource}
export async function createFleetMap(element, _key, callbacks) {
  element.dataset.hoursFixture = 'offline-map-callbacks';
  element.style.background = 'var(--ui-surface-muted)';
  const viewport = createCameraViewport(element, {});
  let revision = 0, selectedTruck = null;
  const transition = kind => callbacks.invokeMethodAsync('OnMapInspectorChanged', kind, selectedTruck, ++revision);
  const fixture = window.hoursFixture = {next: null, async selectTruck(id) {
    selectedTruck = id;
    await transition('truck');
    return callbacks.invokeMethodAsync('OnTruckSelected', id);
  }, async selectStop(index) {
    await transition('next-stop');
    return callbacks.invokeMethodAsync('OnNextLoadSelected', this.next.truckId,
      this.next.currentDispatchId, this.next.routes[0].id, index);
  }};
  return {setOptions(){},setTrucks(){},setTrucksVisible(){},setStationsVisible(){},setTrafficVisible(){},setIfta(){},
    setInspectorMode(kind, truckId){selectedTruck = truckId; return transition(kind);},
    clearMapInspection(){return transition('closed');},
    clearSelection(){},clearNextLoads(){fixture.next = null;},setNextLoadsVisible(){},clearNextLoadSelection(){},
    setStopEtas(value){fixture.etas = value;},setLoadReference(){},setFollow(){},
    setRouteBytes(bytes){fixture.plan = JSON.parse(new TextDecoder().decode(bytes));return true;},
    setNextLoadsBytes(bytes){const data = JSON.parse(new TextDecoder().decode(bytes)); if(data.routes)fixture.next = data;},
    focusTruck(){return true;},dispose(){viewport.dispose();delete window.hoursFixture;}};
}`;
const stubIntegrity = `sha256-${createHash('sha256').update(mapStub).digest('base64')}`;
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g, (_all, open, json, close) => {
    const map = JSON.parse(json);
    for (const name of Object.keys(map.integrity ?? {}))
      if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(name)) map.integrity[name] = stubIntegrity;
    return open + JSON.stringify(map) + close;
  });
const report = {artifact, fleetOnly, scope: 'Staged Blazor Dispatch and selected future-stop ETA/cycle cards with recap-only alternatives. Standalone fuel summary remains absent; saved future-trip fuel metadata stays on the map bridge through ETA polling. Deterministic API and map callbacks; no provider/GPU, backend authentication, database, or real calculations.',
  cases: [], failures: [], unexpectedRequests: [], browserErrors: []};
const browser = await chromium.launch({headless: true, channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome'});
await mkdir(output, {recursive: true});
const check = (condition, message) => {if (!condition) report.failures.push(message);};
const normalize = text => text.replace(/\s+/g, ' ').trim();
async function stableMapRect(page, original, name) {
  const current = await page.locator('#fleet-map').evaluate(element => {
    const {x, y, width, height} = element.getBoundingClientRect();
    return {x, y, width, height, sameNode: window.hoursFixtureMapElement === element,
      sameInspectorHost: window.hoursFixtureInspectorHost === document.querySelector('.fleet-map-inspector__native')};
  });
  assert.equal(current.sameNode, true, `${name}: selection replaced the mounted map`);
  assert.equal(current.sameInspectorHost, true, `${name}: selection replaced the native inspector host`);
  for (const key of ['x', 'y', 'width', 'height'])
    check(Math.abs(current[key] - original[key]) <= 1, `${name}: map ${key} changed from ${original[key]} to ${current[key]}`);
  return current;
}
async function openStopDetails(page, dialog) {
  for (const summary of await dialog.locator('.dispatch-load__stop-details > summary').all()) {
    if (await summary.evaluate(element => element.parentElement.open)) continue;
    await summary.focus();
    await page.keyboard.press('Enter');
  }
}
async function checkRemovedDisplays(scope, name) {
  check(await scope.locator('.stop-hours__departure-cycle').count() === 0, `${name}: removed departure balance returned`);
  check(!/After service/i.test(await scope.textContent()), `${name}: removed service wording returned`);
  check(await scope.locator('[data-kind="restart"]').count() === 0, `${name}: removed reset alternative returned`);
  check(!/If reset|On time if reset/i.test(await scope.textContent()), `${name}: removed reset wording returned`);
  check(await scope.locator('.fuel-plan-summary').count() === 0, `${name}: removed standalone fuel summary returned`);
}
const savedFuelPayload = page => page.evaluate(() => window.hoursFixture?.plan?.fuelPlan ?? null);
async function forecastAppearance(scope) {
  return scope.locator('.stop-hours').evaluateAll(cards => cards.map(card => ({
    text: card.textContent.replace(/\s+/g, ' ').trim(),
    times: [...card.querySelectorAll('time')].map(node => node.dateTime),
    values: [...card.querySelectorAll('.stop-hours__value, .stop-hours__status')].map(node => {
      const style = getComputedStyle(node);
      return {text: node.textContent.replace(/\s+/g, ' ').trim(), className: node.className,
        color: style.color, background: style.backgroundColor};
    })
  })));
}
function heldResponse() {
  let arrive, resume;
  const entered = new Promise(resolve => {arrive = resolve;});
  const released = new Promise(resolve => {resume = resolve;});
  return {entered, release: () => resume(), block: async () => {arrive(); await released;}};
}
async function waitForHeld(held, name) {
  let timer;
  try {
    await Promise.race([held.entered, new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error(`${name}: the polling HTTP request was not held`)), 10_000);
    })]);
  } finally {clearTimeout(timer);}
}
async function watchQuietReplacement(scope) {
  await scope.evaluate(element => {
    const identities = new WeakMap();
    let sequence = 0;
    const snapshot = () => {
      const cards = [...element.querySelectorAll('.stop-hours')];
      const bridge = window.hoursFixture?.etas;
      return {at: Date.now(), connected: element.isConnected,
        ids: cards.map(card => {if (!identities.has(card)) identities.set(card, ++sequence); return identities.get(card);}),
        rows: cards.map(card => card.textContent.replace(/\s+/g, ' ').trim()),
        eta: bridge && {calculatedAt: bridge.eta?.calculatedAt, validUntil: bridge.eta?.validUntil,
          pending: bridge.eta?.routeUpdatePending, refreshing: bridge.refreshing,
          stops: bridge.eta?.stops?.map(stop => stop.stopId)},
        emptyMarkup: cards.length ? undefined : [...element.querySelectorAll('.fleet-map-route-info, .fleet-map-next-load-card')]
          .map(card => card.outerHTML).join('\n').slice(0, 12000)};
    };
    const initial = snapshot();
    const probe = {before: initial.rows, initial, samples: [], observations: [], snapshot};
    probe.observer = new MutationObserver(() => {
      const value = snapshot();
      probe.samples.push(value.rows);
      probe.observations.push(value);
    });
    probe.observer.observe(element, {subtree: true, childList: true, characterData: true, attributes: true});
    element.quietEtaProbe = probe;
  });
}
async function checkQuietReplacement(scope, name) {
  const result = await scope.evaluate(element => {
    const probe = element.quietEtaProbe;
    probe.observer.disconnect();
    delete element.quietEtaProbe;
    const final = probe.snapshot();
    return {before: probe.before, samples: probe.samples, after: final.rows,
      initial: probe.initial, observations: probe.observations, final};
  });
  (report.etaProbes ??= []).push({name, ...result});
  assert.equal(result.after.length, result.before.length, `${name}: replacement removed forecast cards`);
  assert.notDeepEqual(result.after, result.before, `${name}: complete replacement did not change ETA values`);
  for (const sample of result.samples) {
    assert.equal(sample.length, result.before.length, `${name}: an intermediate render removed forecast cards`);
    sample.forEach((text, index) => assert.ok(text === result.before[index] || text === result.after[index],
      `${name}: an intermediate render exposed an incomplete forecast`));
  }
  return {samples: result.samples.length, retainedCards: result.before.length};
}
async function measure(scope, name) {
  const result = await scope.evaluate(element => ({text: element.textContent.replace(/\s+/g, ' ').trim(),
    scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
    rows: [...element.querySelectorAll('.stop-hours__row')].map(row => {
      const rect = row.getBoundingClientRect();
      return {text: row.textContent.replace(/\s+/g, ' ').trim(), left: rect.left, right: rect.right,
        scrollWidth: row.scrollWidth, clientWidth: row.clientWidth};
    }), viewport: innerWidth, documentWidth: document.documentElement.scrollWidth}));
  check(result.scrollWidth <= result.clientWidth + 2, `${name}: card horizontal overflow`);
  check(result.documentWidth <= result.viewport + 2, `${name}: document horizontal overflow`);
  check(!/[\u0400-\u04ff]/u.test(result.text), `${name}: forecast card contains untranslated text`);
  for (const row of result.rows) check(row.scrollWidth <= row.clientWidth + 2 && row.left >= -1 && row.right <= result.viewport + 1,
    `${name}: row overflow: ${row.text}`);
  return result;
}
async function checkCycleAlignment(card, name) {
  const alignment = await card.locator('.stop-hours__road').evaluate(row => {
    const label = row.querySelector('.stop-hours__label').getBoundingClientRect();
    const cycle = row.querySelector('.stop-hours__cycle-status').getBoundingClientRect();
    const arrival = row.querySelector('.stop-hours__arrival').getBoundingClientRect();
    const late = row.querySelector('.stop-hours__arrival .stop-hours__status');
    return {labelLeft: label.left, cycleLeft: cycle.left, cycleTop: cycle.top, arrivalBottom: arrival.bottom,
      lateTop: late?.getBoundingClientRect().top, timeTop: row.querySelector('time').getBoundingClientRect().top};
  });
  check(Math.abs(alignment.labelLeft - alignment.cycleLeft) <= 1, `${name}: cycle warning is indented under the ETA value`);
  check(alignment.cycleTop >= alignment.arrivalBottom - 1, `${name}: cycle warning overlaps the ETA row`);
  if (alignment.lateTop !== undefined)
    check(Math.abs(alignment.lateTop - alignment.timeTop) <= 2, `${name}: known lateness no longer sits beside the ETA`);
}
async function checkHeaderLoadingSpace(page, name) {
  const sizes = await page.evaluate(() => {
    const source = document.querySelector('.fleet-map-inspector');
    const map = document.querySelector('#fleet-map');
    const rect = node => {const r = node.getBoundingClientRect();
      return {x: r.x, y: r.y, width: r.width, height: r.height};};
    const before = rect(map);
    const host = source.cloneNode(true);
    host.style.visibility = 'hidden';
    host.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
    source.parentElement.append(host);
    const truckCopy = host.querySelector('.fleet-map-truck-info');
    const routeCopy = host.querySelector('.fleet-map-route-info[aria-label="Current dispatch route"]');
    const measure = () => ({truckHeight: truckCopy.getBoundingClientRect().height,
      routeHeight: routeCopy.getBoundingClientRect().height, map: rect(map), overlay: rect(host),
      maxHeight: parseFloat(getComputedStyle(host).maxHeight) * map.getBoundingClientRect().height / 100,
      widthCap: parseFloat(getComputedStyle(host).getPropertyValue('--size-map-inspector'))
        * parseFloat(getComputedStyle(document.documentElement).fontSize),
      insetCap: parseFloat(getComputedStyle(host).getPropertyValue('--space-md'))
        * parseFloat(getComputedStyle(document.documentElement).fontSize),
      position: getComputedStyle(host).position,
      truckWidth: truckCopy.clientWidth, truckScrollWidth: truckCopy.scrollWidth,
      routeWidth: routeCopy.clientWidth, routeScrollWidth: routeCopy.scrollWidth});
    try {
      const ready = measure();
      truckCopy.querySelectorAll('.driver-duty > :not(.driver-duty__current)').forEach(node => node.remove());
      truckCopy.querySelectorAll('.driver-hours__dial strong').forEach(node => { node.textContent = '—'; });
      const duty = truckCopy.querySelector('.driver-duty__current');
      duty.replaceChildren(document.createTextNode('—'));
      truckCopy.querySelector('.driver-next-recap strong').textContent = '—';
      routeCopy.querySelector(':scope > .arrival-estimate').replaceChildren();
      routeCopy.querySelectorAll('.fleet-map-route-info__metric strong').forEach(node => { node.firstChild.textContent = '— '; });
      const next = routeCopy.querySelector('.fleet-map-route-info__next');
      next.querySelectorAll(':scope > :not(.fleet-map-route-info__label)').forEach(node => node.remove());
      const load = routeCopy.querySelector(':scope > .fleet-map-route-info__load');
      load.querySelectorAll(':scope > :not(.fleet-map-route-info__label):not(.fleet-map-route-info__total)').forEach(node => node.remove());
      load.querySelector('.fleet-map-route-info__total').textContent = 'Total — mi · — km';
      routeCopy.querySelector('.fleet-map-route-info__appointment strong').textContent = '—';
      return {before, ready, loading: measure()};
    } finally { host.remove(); }
  });
  for (const state of [sizes.ready, sizes.loading]) {
    for (const key of ['x', 'y', 'width', 'height'])
      check(Math.abs(state.map[key] - sizes.before[key]) <= 1, `${name}: ${key} of the actual map shifts while inspector values load`);
    const sideClearance = Math.max(0, Math.min(state.overlay.x - state.map.x,
      state.map.x + state.map.width - state.overlay.x - state.overlay.width));
    check(state.position === 'absolute' && state.overlay.height <= state.maxHeight + 1
      && Math.abs(state.overlay.x + state.overlay.width / 2 - state.map.x - state.map.width / 2) <= 1
      && Math.abs(state.overlay.y - state.map.y - Math.min(state.insetCap, sideClearance)) <= 1
      && Math.abs(state.overlay.width - Math.min(state.map.width, state.widthCap)) <= 1,
      `${name}: loading/ready inspector must remain centered and width-capped with coordinated top/side clearance`);
    check(state.truckScrollWidth <= state.truckWidth + 1 && state.routeScrollWidth <= state.routeWidth + 1,
      `${name}: loading/ready inspector content overflows horizontally`);
  }
  if (sizes.ready.routeWidth >= 1101)
    check(sizes.ready.routeHeight <= 113, `${name}: ordinary route summary reserves excess vertical space`);
  (report.headerLoadingSpace ??= []).push({name, ...sizes});
}
async function checkInspectorLargeText(page, name) {
  const original = await page.evaluate(() => {
    const style = document.documentElement.style;
    const map = document.querySelector('#fleet-map').getBoundingClientRect();
    const saved = {value: style.getPropertyValue('font-size'), priority: style.getPropertyPriority('font-size'),
      root: parseFloat(getComputedStyle(document.documentElement).fontSize),
      label: parseFloat(getComputedStyle(document.querySelector('.fleet-map-route-info__label')).fontSize),
      padding: parseFloat(getComputedStyle(document.querySelector('.fleet-map-truck-info')).paddingLeft),
      dials: [...document.querySelectorAll('.fleet-map-truck-info .driver-hours__dial')].map(element => element.getBoundingClientRect().width),
      map: {x: map.x, y: map.y, width: map.width, height: map.height}};
    style.setProperty('font-size', '200%', 'important');
    document.querySelector('.fleet-map-inspector').scrollTop = 0;
    return saved;
  });
  try {
    await page.waitForFunction(expected =>
      parseFloat(getComputedStyle(document.documentElement).fontSize) === expected.root * 2
      && parseFloat(getComputedStyle(document.querySelector('.fleet-map-route-info__label')).fontSize) === expected.label * 2
      && parseFloat(getComputedStyle(document.querySelector('.fleet-map-truck-info')).paddingLeft) === expected.padding * 2,
    original, {polling: 50});
    await page.screenshot({path: resolve(output, `${name}-selected-info-large-text.png`)});
    const layout = await page.locator('.fleet-map-info-content').evaluate(element => {
      const rect = node => {const r = node.getBoundingClientRect();
        return {left: r.left, right: r.right, top: r.top, bottom: r.bottom};};
      const panels = [...element.querySelectorAll('.fleet-map-truck-info, .fleet-map-route-info')];
      return panels.map(panel => ({bounds: rect(panel), width: panel.clientWidth, scroll: panel.scrollWidth,
        children: [...panel.children].filter(child => getComputedStyle(child).display !== 'none')
          .map(child => ({...rect(child), width: child.clientWidth, scroll: child.scrollWidth, className: child.className}))}));
    });
    const textSize = await page.locator('.fleet-map-route-info__label').first().evaluate(element =>
      parseFloat(getComputedStyle(element).fontSize));
    check(Math.abs(textSize - original.label * 2) <= .1, `${name}: large-text probe did not apply 200% label sizing`);
    const dials = await page.locator('.fleet-map-truck-info .driver-hours__dial').evaluateAll(elements => elements.map(element => {
      const dial = element.getBoundingClientRect(), time = element.querySelector('strong').getBoundingClientRect();
      return {size: dial.width, timeWidth: time.width, contained: time.left >= dial.left && time.right <= dial.right
        && time.top >= dial.top && time.bottom <= dial.bottom};
    }));
    check(dials.length === 4 && dials.every((dial, index) => dial.contained && Math.abs(dial.size - original.dials[index] * 2) <= 1),
      `${name}: enlarged HOS times must fit inside proportionally scaled dials`);
    for (const panel of layout) {
      check(panel.scroll <= panel.width + 1, `${name}: 200% text overflows the inspector horizontally`);
      for (const child of panel.children)
        check(child.left >= panel.bounds.left - 1 && child.right <= panel.bounds.right + 1 && child.scroll <= child.width + 1,
          `${name}: 200% text clips ${child.className}`);
      for (let i = 0; i < panel.children.length; i++) for (const other of panel.children.slice(i + 1)) {
        const child = panel.children[i];
        check(!(child.left < other.right - 1 && child.right > other.left + 1 && child.top < other.bottom - 1 && child.bottom > other.top + 1),
          `${name}: 200% text overlaps ${child.className} and ${other.className}`);
      }
    }
    (report.largeTextInspector ??= []).push({name, labelSize: textSize, ordinaryLabelSize: original.label, dials, panels: layout});
  } finally {
    await page.evaluate(saved => {
      const style = document.documentElement.style;
      if (saved.value) style.setProperty('font-size', saved.value, saved.priority);
      else style.removeProperty('font-size');
    }, original);
    await page.waitForFunction(expected => {
      const rect = document.querySelector('#fleet-map').getBoundingClientRect();
      return parseFloat(getComputedStyle(document.documentElement).fontSize) === expected.root
        && parseFloat(getComputedStyle(document.querySelector('.fleet-map-route-info__label')).fontSize) === expected.label
        && parseFloat(getComputedStyle(document.querySelector('.fleet-map-truck-info')).paddingLeft) === expected.padding
        && ['x', 'y', 'width', 'height'].every(key => Math.abs(rect[key] - expected.map[key]) <= 1);
    }, original, {polling: 50});
  }
}
try {
  for (const width of widths) for (const theme of themes) {
    const name = `${width}-${theme}`;
    let pending = false;
    let boardReads = 0;
    let planningReads = 0;
    let timing = {};
    let holdBoard = null;
    let holdPlanning = null;
    let holdPreview = null;
    const context = await browser.newContext({viewport: {width, height: 1000}, colorScheme: theme, locale: 'en-US',
      timezoneId: 'America/Toronto', reducedMotion: 'reduce', serviceWorkers: 'block'});
    await context.addInitScript(({userId, theme}) => {
      localStorage.setItem('auth_session', JSON.stringify({Id: userId, AccessToken: 'fixture', RefreshToken: 'fixture'}));
      document.addEventListener('DOMContentLoaded', () => {document.documentElement.dataset.theme = theme;});
      Object.defineProperty(navigator, 'clipboard', {configurable: true,
        value: {writeText: async text => {window.hoursFixtureCopiedText = text;}}});
    }, {userId, theme});
    await installReleaseArtifact(context, artifact, origin);
    await context.route('**/*', async route => {
      const request = route.request();
      const url = new URL(request.url());
      let fixture;
      if (url.origin === origin && ['GET', 'POST'].includes(request.method()) && url.pathname === `/api/fleet/trucks/${truckId}/planning`) {
        planningReads++;
        if (holdPlanning) {const held = holdPlanning; holdPlanning = null; await held.block();}
        await route.fulfill({status: 200, json: success(planning(pending, timing))});
      } else if (url.origin !== origin || !['GET', 'HEAD'].includes(request.method())) {
        report.unexpectedRequests.push(`${request.method()} ${url.origin}${url.pathname}`);
        await route.abort('blockedbyclient');
      } else if (url.pathname.startsWith('/api/')) {
        if (url.pathname === '/api/auth/me') fixture = {id: userId, name: 'Fixture Administrator', email: 'fixture@example.invalid', isAdmin: true};
        else if (url.pathname === '/api/settings/dispatch') fixture = success({loadNumberPrefix: 'AMF', revision: 1, updatedAt: null});
        else if (url.pathname === '/api/fleet/locations') fixture = success({trucks: [truck], points: [truck]});
        else if (url.pathname === '/api/fleet/planning/previews') fixture = success([]);
        else if (url.pathname === '/api/dispatch/board') {
          boardReads++;
          if (holdBoard) {const held = holdBoard; holdBoard = null; await held.block();}
          fixture = success({items: [{key: truckId, truckId, truckNumber: '11006', driverName: truck.driverName,
            trailerNumber: truck.trailerNumber, hos: planning(pending, timing).hos,
            currentCycle: {calculatedAt: now, validUntil: '2026-09-08T14:00:00Z', cycle: recap},
            dispatches: loads(pending, timing)}], page: 1, pageSize: 20, totalCount: 1, totalPages: 1});
        } else if (url.pathname === `/api/fleet/trucks/${truckId}/planning/preview`) {
          if (holdPreview) {const held = holdPreview; holdPreview = null; await held.block();}
          fixture = success(planning(pending, timing));
        }
        else if (url.pathname === `/api/dispatch/${currentId}`) fixture = success(loads(pending, timing)[0]);
        else if (url.pathname === `/api/dispatch/${futureId}`) fixture = success(loads(pending, timing)[1]);
        else if (url.pathname === `/api/dispatch/truck/${truckId}`) fixture = success(loads(pending, timing));
        else if (url.pathname === `/api/dispatch/truck/${truckId}/next-routes`)
          fixture = success({revision: 'fixture-v1', unchanged: url.searchParams.get('revision') === 'fixture-v1', routes: [futureRoute]});
        if (!fixture) report.unexpectedRequests.push(`Unmocked API ${url.pathname}`);
        await route.fulfill({status: fixture ? 200 : 500, json: fixture ?? {success: false, errors: ['Unmocked API']}});
      } else if (/\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(url.pathname))
        await route.fulfill({status: 200, contentType: 'text/javascript', body: mapStub});
      else if (request.isNavigationRequest()) await route.fulfill({status: 200, contentType: 'text/html', body: html});
      else await route.fallback();
    });
    const page = await context.newPage();
    page.on('pageerror', error => report.browserErrors.push(`${name}: ${error.message}`));
    page.on('console', message => {if (message.type() === 'error') report.browserErrors.push(`${name}: ${message.text()}`);});
    await page.clock.install({time: new Date(now)});
    let dispatchBefore = null, dispatchPending = null, dispatchHeaderGeometry = null, dispatchQuiet = null;
    if (!fleetOnly) {
    await page.goto(`${origin}/dispatch`);
    await page.locator('.dispatch-load__stop-times .stop-hours').nth(2).waitFor();
    const current = page.locator('.dispatch-load--current .dispatch-load__stop');
    const futureCard = page.locator('.dispatch-load:not(.dispatch-load--current)');
    const future = page.locator('.dispatch-load-dialog');
    const pickup = future.locator('.dispatch-load__stop').first();
    const delivery = future.locator('.dispatch-load__stop').last();
    check(await page.locator('.dispatch-load details, .dispatch-load-dialog').count() === 0,
      `${name}: supplemental details should not create inline disclosures or an initially open dialog`);
    check(await page.locator('.dispatch-load .stop-hours__cycle').count() === 0,
      `${name}: cycle balances belong in load details, not the compact main card`);
    check(await page.locator('.dispatch-load__stop-times .stop-hours__road:visible').count() === 3,
      `${name}: all three compact stop ETA/status summaries must remain visible`);
    await page.locator('.dispatch-truck').screenshot({path: resolve(output, `${name}-dispatch-compact.png`)});
    check(normalize(await current.innerText()).includes('Cycle short'), `${name}: current cycle shortage missing`);
    check(await current.locator('.stop-hours__departure-cycle').count() === 0, `${name}: duplicate cycle balance shown`);
    check(await current.locator('.stop-hours__road .stop-hours__status--success').count() === 0, `${name}: infeasible current road ETA shown green`);
    await futureCard.locator('.dispatch-load__details').focus();
    await page.keyboard.press('Enter');
    await future.waitFor({state: 'visible'});
    await future.screenshot({path: resolve(output, `${name}-dispatch-dialog-initial.png`)});
    await openStopDetails(page, future);
    check(await future.evaluate(element => element.open) && await future.locator('.dispatch-load__stop').count() === 2,
      `${name}: keyboard Details opens one dialog with both future stops`);
    check(normalize(await pickup.innerText()).includes('−3h 00m'), `${name}: signed cycle arrival missing`);
    check(await pickup.locator('[data-kind="recap"]').count() === 1, `${name}: conditional recap alternative missing`);
    check(await pickup.locator('[data-kind="recap"] .stop-hours__label').textContent() === 'With recap',
      `${name}: recap label is not sentence case`);
    check(await pickup.locator('[data-kind="recap"] .stop-hours__status--danger').textContent() === 'Late by 2h 00m',
      `${name}: recap lateness or its danger tone changed`);
    await checkRemovedDisplays(page.locator('body'), `${name}-dispatch`);
    check(normalize(await delivery.innerText()).includes('Late by 1h 05m') && normalize(await delivery.innerText()).includes('Cycle unknown'),
      `${name}: unknown cycle hides known lateness`);
    check(await delivery.locator('.stop-hours__alternative').count() === 0, `${name}: unknown cycle invents alternatives`);
    dispatchBefore = await measure(future, `${name}-dispatch`);
    await future.getByRole('button', {name: 'Close load details'}).click();
    await future.waitFor({state: 'detached'});
    await page.locator('.driver-duty__cycle-reset').first().waitFor();
    const dispatchRecap = page.locator('.dispatch-truck__equipment .driver-next-recap');
    check(await dispatchRecap.count() === 1 && normalize(await dispatchRecap.innerText()) === 'Next recap Sep 9 +3h 05m',
      `${name}: one current-driver recap remains visible in the truck header with date and hours only`);
    check(await page.locator('.dispatch-load .driver-next-recap, .dispatch-load .stop-hours__recap').count() === 0
      && !(await futureCard.innerText()).includes('Next recap'), `${name}: load cards repeat the truck recap`);
    const compactHeader = page.locator('.dispatch-planning--compact');
    check(await compactHeader.locator('.dispatch-planning__driver .driver-duty').count() === 1
      && await compactHeader.locator('.driver-hours-panel .driver-duty').count() === 0,
      `${name}: duty status belongs in the route area, not beneath HOS`);
    check(await compactHeader.locator('.driver-hours__clock:not(.is-unavailable)').count() === 4,
      `${name}: Dispatch retains all four current HOS clocks`);
    dispatchHeaderGeometry = await compactHeader.evaluate(element => {
      const rect = node => {const r = node.getBoundingClientRect(); return {left: r.left, right: r.right,
        top: r.top, bottom: r.bottom, height: r.height, width: r.width};};
      return {header: rect(element), identity: rect(element.closest('.dispatch-truck').querySelector('.dispatch-truck__header')),
        route: rect(element.querySelector('.dispatch-planning__content')),
        driver: rect(element.querySelector('.dispatch-planning__driver')), hours: rect(element.querySelector('.driver-hours-panel')),
        clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
        padding: parseFloat(getComputedStyle(element).paddingTop) + parseFloat(getComputedStyle(element).paddingBottom),
        gap: parseFloat(getComputedStyle(element).columnGap)};
    });
    check(dispatchHeaderGeometry.scrollWidth <= dispatchHeaderGeometry.clientWidth + 1,
      `${name}: compact Dispatch header overflows horizontally`);
    if (width >= 1200) {
      const g = dispatchHeaderGeometry;
      check(Math.abs(g.identity.left - g.header.left) <= 1
        && g.route.left >= g.identity.right - 1 && g.route.left - g.identity.right <= g.gap + 1
        && Math.abs(g.hours.left - Math.max(g.route.right, g.driver.right) - g.gap) <= 1,
        `${name}: Dispatch identity, status/fuel and mileage, and HOS must be left-packed without elastic gaps`);
      check(g.driver.top >= Math.max(g.route.bottom, g.identity.bottom) - 1
        && Math.abs(g.driver.left - g.identity.left) <= 1 && g.driver.right <= g.hours.left + 1,
        `${name}: duty/rest and Next recap must use the strip below truck identity and route metrics, not a tall right column`);
      check(g.header.height <= Math.max(Math.max(g.route.height, g.identity.height) + g.driver.height, g.hours.height) + 3,
        `${name}: Dispatch header must fit its two natural content rows without reserved blank height`);
    } else if (width < 551) {
      const g = dispatchHeaderGeometry;
      check(g.driver.top >= g.route.bottom - 1 && g.hours.top >= g.driver.bottom - 1,
        `${name}: narrow Dispatch must show route, duty/recap and HOS in separate unclipped rows`);
    }
    await compactHeader.screenshot({path: resolve(output, `${name}-dispatch-header.png`)});
    check(normalize(await page.locator('.dispatch-planning__fuel').innerText()) === 'Fuel 75%',
      `${name}: saved fuel reading is a percentage without maintenance wording`);
    check(await page.locator('.dispatch-truck .dispatch-load__stop-times .stop-hours').count() === 3
      && await page.locator('.dispatch-truck .dispatch-load__stop-detail-content .stop-hours').count() === 0
      && await page.locator('.dispatch-truck__equipment .dispatch-planning__details').count() === 0,
      `${name}: each actual stop has one compact summary without inline detailed forecasts or header duplicates`);
    check((await page.locator('.driver-duty__cycle-reset').allTextContents()).some(text => text.includes('27h 52m left to complete 34h reset')),
      `${name}: current-driver reset countdown missing`);
    await futureCard.locator('.dispatch-load__details').click();
    await future.waitFor({state: 'visible'});
    await openStopDetails(page, future);
    check(await future.locator('.stop-hours').count() === 4,
      `${name}: load dialog contains a summary and detailed cycle for each future stop`);
    const dispatchAppearance = await forecastAppearance(page.locator('.dispatch-truck'));
    const initialTimes = await future.locator('.stop-hours time').evaluateAll(nodes => nodes.map(node => node.dateTime));
    await future.screenshot({path: resolve(output, `${name}-dispatch.png`)});
    pending = true;
    const initialBoardReads = boardReads;
    await page.clock.fastForward(65_000);
    await page.waitForFunction(() => [...document.querySelectorAll('.dispatch-load__reference')]
      .every(node => node.textContent.includes('Fixture Customer refreshed')));
    check(boardReads > initialBoardReads, `${name}: pending Dispatch response was not polled`);
    assert.deepEqual(await future.locator('.stop-hours time').evaluateAll(nodes => nodes.map(node => node.dateTime)), initialTimes,
      `${name}: pending erased or replaced stop/alternative times`);
    assert.deepEqual(await forecastAppearance(page.locator('.dispatch-truck')), dispatchAppearance,
      `${name}: pending changed current/future ETA text, status, cycle, alternatives or colors`);
    await checkRemovedDisplays(page.locator('body'), `${name}-dispatch-pending`);
    dispatchPending = await measure(future, `${name}-dispatch-pending`);
    await future.screenshot({path: resolve(output, `${name}-dispatch-pending.png`)});

    pending = false;
    const dispatchTime = await page.evaluate(() => Date.now());
    timing = {calculatedAt: new Date(dispatchTime).toISOString(), validUntil: new Date(dispatchTime + 120_000).toISOString()};
    await page.clock.fastForward(65_000);
    await page.waitForFunction(() => [...document.querySelectorAll('.dispatch-load__reference')]
      .every(node => !node.textContent.includes('Fixture Customer refreshed')));
    const dispatchScope = page.locator('.dispatch-truck');
    const dispatchHeldAppearance = await forecastAppearance(dispatchScope);
    await watchQuietReplacement(dispatchScope);
    const heldDispatch = holdBoard = heldResponse();
    await page.clock.fastForward(65_000);
    await waitForHeld(heldDispatch, `${name}-dispatch-deadline`);
    assert.ok(await page.evaluate(() => Date.now()) > Date.parse(timing.validUntil), `${name}: Dispatch HTTP did not cross ValidUntil`);
    assert.deepEqual(await forecastAppearance(dispatchScope), dispatchHeldAppearance,
      `${name}: held Dispatch HTTP crossing ValidUntil changed ETA text, times, status or colors`);
    const dispatchReplacementTime = await page.evaluate(() => Date.now());
    timing = {calculatedAt: new Date(dispatchReplacementTime).toISOString(),
      validUntil: new Date(dispatchReplacementTime + 120_000).toISOString(), shiftMinutes: 5};
    heldDispatch.release();
    await page.waitForFunction(() => [...document.querySelectorAll('.dispatch-load .stop-hours__road')]
      .some(node => node.textContent.includes('07:10 PM')));
    dispatchQuiet = await checkQuietReplacement(dispatchScope, `${name}-dispatch-deadline`);
    }

    pending = false;
    timing = {};
    await page.clock.setSystemTime(new Date(now));
    await page.goto(`${origin}/fleet/map`);
    await page.locator('[data-hours-fixture]').waitFor();
    const mapRect = await page.locator('#fleet-map').evaluate(element => {
      window.hoursFixtureMapElement = element;
      window.hoursFixtureInspectorHost = document.querySelector('.fleet-map-inspector__native');
      const {x, y, width, height} = element.getBoundingClientRect();
      return {x, y, width, height};
    });
    const unselectedHeight = await page.locator('.fleet-map-info-reserved').evaluate(element => element.getBoundingClientRect().height);
    check(unselectedHeight <= (width === 390 ? 48 : 80)
      && await page.locator('.fleet-map-info-reserved.has-selection').count() === 0,
      `${name}: an unselected map wastes viewport height on an empty selected-truck reserve`);
    await page.screenshot({path: resolve(output, `${name}-unselected-map.png`)});
    const previewHeld = holdPreview = heldResponse();
    await page.evaluate(id => {void window.hoursFixture.selectTruck(id);}, truckId);
    await waitForHeld(previewHeld, `${name}-cold-preview`);
    await page.locator('.fleet-map-route-info[aria-busy="true"]').waitFor({state: 'attached'});
    const loadingMapRect = await stableMapRect(page, mapRect, `${name}-first-selection-loading`);
    previewHeld.release();
    const duty = page.locator('.fleet-map-truck-info__duty > .driver-duty');
    await page.waitForFunction(() => document.querySelector('.fleet-map-truck-info .driver-duty')?.textContent.includes('Sleeper Berth · 6h 8min'));
    const selectedMapRect = await stableMapRect(page, mapRect, `${name}-first-selection-ready`);
    await page.evaluate(id => window.hoursFixture.selectTruck(id), truckId);
    await stableMapRect(page, mapRect, `${name}-repeat-selection`);
    await page.screenshot({path: resolve(output, `${name}-selected-info-initial.png`)});
    check(normalize(await duty.textContent()).includes('Sleeper Berth · 6h 8min')
      && !(await duty.textContent()).includes('Current status:'),
      `${name}: current status does not explain elapsed time beside HOS clocks`);
    check(await page.locator('.fleet-map-route-info .driver-duty').count() === 0,
      `${name}: current duty status mixed with stop forecast`);
    const dutyBefore = normalize(await duty.textContent());
    if (width === 390) await page.locator('.fleet-map-mobile-summary__toggle').click();
    await stableMapRect(page, mapRect, `${name}-expanded-selection`);
    const overlayGeometry = await page.locator('.fleet-map-info-reserved').evaluate(element => {
      const rect = element.getBoundingClientRect(), style = getComputedStyle(element);
      const truck = element.querySelector('.fleet-map-truck-info'), route = element.querySelector('.fleet-map-route-info');
      const truckStyle = getComputedStyle(truck), routeStyle = getComputedStyle(route);
      const shadowReference = document.createElement('div');
      shadowReference.style.boxShadow = style.getPropertyValue('--shadow-card');
      element.append(shadowReference);
      const expectedShadow = getComputedStyle(shadowReference).boxShadow;
      shadowReference.remove();
      return {position: style.position, height: rect.height, top: rect.top, left: rect.left, right: rect.right, width: rect.width,
        widthCap: parseFloat(style.getPropertyValue('--size-map-inspector')) * parseFloat(getComputedStyle(document.documentElement).fontSize),
        insetCap: parseFloat(style.getPropertyValue('--space-md')) * parseFloat(getComputedStyle(document.documentElement).fontSize),
        expectedShadow,
        expectedRadius: parseFloat(style.getPropertyValue('--radius-sm')) * parseFloat(getComputedStyle(document.documentElement).fontSize),
        shadow: style.boxShadow, radius: style.borderRadius, background: style.backgroundColor,
        rowGap: route.getBoundingClientRect().top - truck.getBoundingClientRect().bottom,
        truckRadius: truckStyle.borderRadius, routeRadius: routeStyle.borderRadius,
        divider: routeStyle.borderTopWidth, scrollHeight: element.scrollHeight,
        overflowY: style.overflowY, parent: element.parentElement.className,
        headingBottom: document.querySelector('.fleet-map-page h1').getBoundingClientRect().bottom,
        filtersBottom: document.querySelector('.fleet-map-toolbar').getBoundingClientRect().bottom};
    });
    check(overlayGeometry.position === 'absolute' && overlayGeometry.parent.includes('fleet-map-stage')
      && overlayGeometry.height <= mapRect.height * (width === 390 ? .60 : .55) + 2
      && overlayGeometry.top >= mapRect.y && overlayGeometry.overflowY === 'auto',
      `${name}: selected information must scroll inside a bounded overlay without covering the full map`);
    check(overlayGeometry.top >= mapRect.y && mapRect.y >= overlayGeometry.filtersBottom - 1
      && overlayGeometry.top >= overlayGeometry.headingBottom,
      `${name}: the info overlay must stay inside the map, never cover the page heading or filters`);
    const sideClearance = Math.max(0, Math.min(overlayGeometry.left - mapRect.x, mapRect.x + mapRect.width - overlayGeometry.right));
    check(Math.abs(overlayGeometry.top - mapRect.y - Math.min(overlayGeometry.insetCap, sideClearance)) <= 1
      && Math.abs(overlayGeometry.left + overlayGeometry.width / 2 - mapRect.x - mapRect.width / 2) <= 1
      && Math.abs(overlayGeometry.width - Math.min(mapRect.width, overlayGeometry.widthCap)) <= 1
      && Math.abs(overlayGeometry.rowGap) <= 1,
      `${name}: unified information must be centered with coordinated top/side clearance and no gap between its rows`);
    check(overlayGeometry.expectedShadow !== 'none' && overlayGeometry.shadow === overlayGeometry.expectedShadow
      && Math.abs(parseFloat(overlayGeometry.radius) - overlayGeometry.expectedRadius) <= 1
      && overlayGeometry.truckRadius === '0px' && overlayGeometry.routeRadius === '0px' && overlayGeometry.divider === '1px',
      `${name}: information must use the shared popup surface, radius and shadow without separate row corners`);
    await page.screenshot({path: resolve(output, `${name}-selected-info-expanded.png`)});
    const fuelReading = page.locator('.fleet-map-truck-info__reading').filter({hasText: 'Fuel'});
    check(await fuelReading.locator('.fuel-reading__value').textContent() === '75%'
      && !normalize(await fuelReading.textContent()).includes('last reading'),
      `${name}: map fuel reading remains a compact percentage`);
    check(await fuelReading.locator('.fuel-reading--metric').count() === 1
      && await page.locator('.fleet-map-truck-info__illustration .truck-illustration__trailer').count() === 1,
      `${name}: selected map uses the metric fuel column and filled truck illustration from the approved composition`);
    const truckHeader = page.locator('.fleet-map-truck-info');
    check(normalize(await truckHeader.locator('.fleet-map-truck-info__hours-label').textContent()) === 'HOS (Samsara)'
      && await truckHeader.locator('.fleet-map-truck-info__reading > small > svg[aria-hidden="true"]').count() === 2,
      `${name}: HOS source and icon-led telemetry match the approved header`);
    check(normalize(await truckHeader.locator('.driver-next-recap').textContent()) === 'Next recap Sep 9 +3h 05m',
      `${name}: truck header exposes the current driver recap, not a stop forecast`);
    check(await truckHeader.locator('.driver-hours__clock:not(.is-unavailable)').count() === 4,
      `${name}: the selected header has four fresh HOS clocks`);
    for (const text of ['Sleeper Berth · 6h 8min', '3h 52m left to complete 10h rest', '27h 52m left to complete 34h reset'])
      check(normalize(await duty.textContent()).includes(text), `${name}: selected header is missing ${text}`);
    const headerGeometry = await truckHeader.evaluate(element => {
      const rect = node => {const bounds = node.getBoundingClientRect();
        return {left: bounds.left, right: bounds.right, top: bounds.top, bottom: bounds.bottom, width: bounds.width, height: bounds.height};};
      const text = document.createTreeWalker(element.querySelector('.driver-duty'), NodeFilter.SHOW_TEXT);
      const textBounds = [];
      while (text.nextNode()) if (text.currentNode.textContent.trim()) {
        const range = document.createRange();
        range.selectNodeContents(text.currentNode);
        textBounds.push(...range.getClientRects());
      }
      return {header: rect(element), hours: rect(element.querySelector('.driver-hours')),
        hoursGroup: rect(element.querySelector('.fleet-map-truck-info__hours')),
        dials: [...element.querySelectorAll('.driver-hours__dial')].map(rect),
        hosGap: parseFloat(getComputedStyle(element.querySelector('.driver-hours')).columnGap),
        identity: rect(element.querySelector('.fleet-map-truck-info__identity')),
        telemetry: rect(element.querySelector('.fleet-map-truck-info__telemetry')),
        duty: rect(element.querySelector('.driver-duty')), clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
        dutyGroup: rect(element.querySelector('.fleet-map-truck-info__duty')),
        actions: rect(element.querySelector('.fleet-map-truck-info__actions')),
        buttons: [...element.querySelectorAll('.fleet-map-truck-info__buttons button')].map(rect),
        dutyTextRight: Math.max(...textBounds.map(bounds => bounds.right)),
        columnGap: parseFloat(getComputedStyle(element).columnGap),
        paddingRight: parseFloat(getComputedStyle(element).paddingRight),
        rows: [...element.querySelector('.driver-duty').children].map(row => ({...rect(row),
          clientWidth: row.clientWidth, scrollWidth: row.scrollWidth}))};
    });
    check(headerGeometry.scrollWidth <= headerGeometry.clientWidth + 1,
      `${name}: selected truck header has horizontal overflow`);
    check(headerGeometry.dials.length === 4 && Math.abs(headerGeometry.hosGap - 8) <= 1,
      `${name}: selected HOS must use the shared compact 8px gap`);
    for (let i = 1; i < headerGeometry.dials.length; i++) {
      const previous = headerGeometry.dials[i - 1], dial = headerGeometry.dials[i];
      check(Math.abs(dial.top - previous.top) <= 1 && Math.abs(dial.left - previous.right - headerGeometry.hosGap) <= 1,
        `${name}: HOS circles stretched or wrapped despite available space`);
    }
    for (const row of headerGeometry.rows)
      check(row.left >= headerGeometry.header.left - 1 && row.right <= headerGeometry.header.right + 1
        && row.top >= headerGeometry.header.top - 1 && row.bottom <= headerGeometry.header.bottom + 1
        && row.scrollWidth <= row.clientWidth + 1, `${name}: selected driver status/rest text is clipped`);
    if (width >= 1440) {
      check(headerGeometry.duty.left >= headerGeometry.hours.right - 1
        && headerGeometry.duty.top < headerGeometry.hours.bottom,
        `${name}: desktop driver status belongs beside the HOS clocks, not below`);
      check(headerGeometry.telemetry.left >= headerGeometry.identity.right - 1
        && headerGeometry.hours.left >= headerGeometry.telemetry.right - 1,
        `${name}: unified identity, unboxed readings and HOS retain the approved desktop order`);
      check(Math.abs(headerGeometry.hoursGroup.left - headerGeometry.telemetry.right - headerGeometry.columnGap) <= 1,
        `${name}: the entire HOS group must sit directly after telemetry without an elastic spacer`);
      check(Math.abs(headerGeometry.dutyGroup.left - headerGeometry.hoursGroup.right - headerGeometry.columnGap) <= 1,
        `${name}: driver status must sit directly after HOS without an elastic spacer`);
      if (headerGeometry.actions.top < headerGeometry.dutyGroup.bottom)
        check(Math.abs(headerGeometry.actions.left - headerGeometry.dutyGroup.right - headerGeometry.columnGap) <= 1,
          `${name}: truck actions must follow duty information instead of stretching to the far right`);
      else check(Math.abs(headerGeometry.actions.left - headerGeometry.identity.left) <= 1,
        `${name}: wrapped truck actions must restart at the left edge`);
    }
    for (const button of headerGeometry.buttons) {
      check(button.left >= headerGeometry.header.left && button.right <= headerGeometry.header.right,
        `${name}: truck action leaves the selected header`);
      if (width === 390) check(button.width >= 44 && button.height >= 44,
        `${name}: mobile truck actions retain touch targets`);
    }
    await truckHeader.screenshot({path: resolve(output, `${name}-current-status.png`)});
    const routeInfo = page.locator('.fleet-map-route-info[aria-label="Current dispatch route"]');
    const routeGroups = await routeInfo.evaluate(element => {
      const bounds = [...element.children].filter(child => !child.classList.contains('fleet-map-route-info__messages')).map(child => {
        const r = child.getBoundingClientRect();
        return {left: r.left, right: r.right, top: r.top, bottom: r.bottom};
      });
      return {bounds, gap: parseFloat(getComputedStyle(element).columnGap), display: getComputedStyle(element).display};
    });
    if (routeGroups.display === 'flex') for (let i = 1; i < routeGroups.bounds.length; i++) {
      const previous = routeGroups.bounds[i - 1], group = routeGroups.bounds[i];
      check(Math.abs(group.top - previous.top) <= 1
        ? Math.abs(group.left - previous.right - routeGroups.gap) <= 1
        : Math.abs(group.left - routeGroups.bounds[0].left) <= 1,
      `${name}: address, appointment and ETA must follow compact left-packed route groups`);
    }
    assert.deepEqual(await routeInfo.locator(':scope > .fleet-map-route-info__metric .fleet-map-route-info__label').allTextContents(),
      ['Remaining', 'Next Stop'], `${name}: remaining and next-stop distances lead the summary`);
    check(await routeInfo.locator(':scope > .fleet-map-route-info__load').count() === 1
      && await routeInfo.locator(':scope > .fleet-map-route-info__appointment').count() === 1
      && normalize(await routeInfo.locator('.fleet-map-route-info__total').textContent()) === 'Total 500 mi · 805 km',
      `${name}: load, address, appointment and ETA are distinct groups without losing total distance`);
    const address = routeInfo.locator('.fleet-map-route-info__copy-address');
    check(await address.locator('.fleet-map-route-info__address-lines > span').textContent() === '100 Current Street'
      && await address.locator('strong.fleet-map-route-info__address').textContent() === 'Toronto, ON, Canada',
      `${name}: current route address is street first and locality second`);
    check(await routeInfo.locator('.fleet-map-route-info__next > .fleet-map-route-info__label').textContent() === 'Delivery',
      `${name}: stop kind stays separate from its address`);
    const addressGeometry = await address.evaluate(element => {
      const streetElement = element.querySelector('.fleet-map-route-info__address-lines > span');
      const localityElement = element.querySelector('strong.fleet-map-route-info__address');
      const street = streetElement.getBoundingClientRect();
      const locality = localityElement.getBoundingClientRect();
      return {streetBottom: street.bottom, localityTop: locality.top, streetLeft: street.left,
        localityLeft: locality.left, clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
        streetWeight: Number(getComputedStyle(streetElement).fontWeight),
        localityWeight: Number(getComputedStyle(localityElement).fontWeight)};
    });
    check(addressGeometry.localityTop >= addressGeometry.streetBottom - 1
      && Math.abs(addressGeometry.streetLeft - addressGeometry.localityLeft) <= 1
      && addressGeometry.scrollWidth <= addressGeometry.clientWidth + 1,
      `${name}: current route address lines overlap or clip`);
    check(addressGeometry.streetWeight < addressGeometry.localityWeight,
      `${name}: current route locality is emphasized instead of the street`);
    await address.click();
    check(await page.evaluate(() => window.hoursFixtureCopiedText) === stops[0].address,
      `${name}: copying the compact route address preserves its full original value`);
    await routeInfo.screenshot({path: resolve(output, `${name}-current-route.png`)});
    await page.screenshot({path: resolve(output, `${name}-selected-map.png`)});
    await checkHeaderLoadingSpace(page, name);
    await checkInspectorLargeText(page, name);
    await stableMapRect(page, mapRect, `${name}-restored-text-size`);
    const mapKey = page.getByRole('complementary', {name: 'Map key'});
    check(normalize(await mapKey.textContent()).includes('Current route')
      && normalize(await mapKey.textContent()).includes('Pickup / delivery')
      && await mapKey.locator('.fleet-map-key__line--next').count() === 0
      && await mapKey.locator('.fleet-map-key__fuel').count() === 0,
      `${name}: map key distinguishes current stops and does not advertise disabled layers`);
    check(await mapKey.evaluate(element => getComputedStyle(element).position) === 'absolute',
      `${name}: map key must not change the map's available height`);
    await page.waitForFunction(() => window.hoursFixture?.plan?.fuelPlan?.stops?.length === 1);
    await checkRemovedDisplays(page.locator('body'), `${name}-map`);
    const fuelBefore = await savedFuelPayload(page);
    const fuelVisit = fuelBefore.stops[0];
    check(fuelVisit.name === 'Kingston travel stop' && fuelVisit.dispatchId === futureId && fuelVisit.buyGallons === 35,
      `${name}: future-trip station or server purchase quantity missing from map metadata`);
    check(fuelVisit.milesAhead === 100 && fuelVisit.arrivalGallons === 45 && fuelVisit.departureGallons === 80,
      `${name}: map metadata changed server fuel distance or gallons using current-route progress`);
    check(fuelBefore.scheduleImpact.calculatedAt === '2026-09-08T10:15:00Z'
      && fuelBefore.scheduleImpact.addedLateMinutes === 30 && fuelBefore.scheduleImpact.cycleShort === true,
      `${name}: historical fuel schedule metadata was lost`);
    if (width === 390) await page.locator('.fleet-map-mobile-summary__toggle').click();
    if (width === 390) await page.getByRole('button', {name: 'Filters', exact: true}).click();
    await page.getByRole('checkbox', {name: 'Next loads', exact: true}).check();
    await page.waitForFunction(() => window.hoursFixture?.next?.routes?.length === 1);
    check(normalize(await mapKey.textContent()).includes('Next loads · color by load'),
      `${name}: future route key explains per-load colors`);
    if (width === 390) await page.getByRole('button', {name: 'Filters', exact: true}).click();
    const card = page.getByRole('region', {name: 'Selected next load', exact: true});
    await page.evaluate(() => window.hoursFixture.selectStop(0));
    await card.locator('.stop-hours').waitFor();
    check(await card.locator('xpath=ancestor::*[contains(@class,"fleet-map-inspector")]').count() === 1
      && await page.locator('.fleet-map-details-card').count() === 0,
      `${name}: future details must replace the shared inspector content, not open a lower popup`);
    check(await page.locator('#fleet-map-details').evaluate(element => getComputedStyle(element).display) === 'none',
      `${name}: selected future details must hide, not duplicate, the retained truck and route information`);
    await stableMapRect(page, mapRect, `${name}-future-inspection`);
    check(normalize(await card.innerText()).includes('Cycle short'), `${name}: selected pickup shortage missing`);
    check(normalize(await card.locator('.stop-hours__recap').innerText()) === 'Next recap Sep 9 +3h 05m',
      `${name}: selected pickup baseline recap shows only date and amount`);
    check(await card.locator('[data-kind="recap"]').count() === 1
      && await card.locator('[data-kind="recap"] .stop-hours__label').textContent() === 'With recap',
      `${name}: selected pickup recap alternative missing`);
    await checkRemovedDisplays(page.locator('body'), `${name}-selected-pickup`);
    const selected = await measure(card, `${name}-selected-pickup`);
    await checkCycleAlignment(card, `${name}-selected-pickup`);
    await card.screenshot({path: resolve(output, `${name}-selected-pickup.png`)});
    await page.screenshot({path: resolve(output, `${name}-selected-inspector.png`)});
    await page.evaluate(() => window.hoursFixture.selectStop(1));
    await page.waitForFunction(() => document.querySelector('.fleet-map-next-load-card .stop-hours')?.textContent.includes('Cycle unknown'));
    check(normalize(await card.innerText()).includes('Late by 1h 05m'), `${name}: selected delivery known lateness missing`);
    const selectedUnknown = await measure(card, `${name}-selected-delivery`);
    await checkCycleAlignment(card, `${name}-selected-delivery`);
    const selectedAppearance = await forecastAppearance(card);
    await card.screenshot({path: resolve(output, `${name}-selected-delivery.png`)});
    const selectedTimes = await card.locator('.stop-hours time').evaluateAll(nodes => nodes.map(node => node.dateTime));
    pending = true;
    const initialPlanningReads = planningReads;
    await page.clock.fastForward(65_000);
    await page.waitForFunction(() => window.hoursFixture?.etas?.eta?.routeUpdatePending === true
      && window.hoursFixture?.etas?.refreshing === false);
    check(planningReads > initialPlanningReads, `${name}: pending planning response was not polled`);
    assert.equal(normalize(await duty.textContent()), dutyBefore, `${name}: pending refresh changed current duty summary`);
    assert.deepEqual(await card.locator('.stop-hours time').evaluateAll(nodes => nodes.map(node => node.dateTime)), selectedTimes,
      `${name}: pending selected stop lost its ETA/recap`);
    assert.deepEqual(await forecastAppearance(card), selectedAppearance,
      `${name}: pending changed selected-stop ETA text, status, cycle, recap or colors`);
    const selectedPending = await measure(card, `${name}-selected-pending`);
    const pendingMapRect = await stableMapRect(page, mapRect, `${name}-pending-forecast`);
    await card.screenshot({path: resolve(output, `${name}-selected-pending.png`)});
    await checkRemovedDisplays(page.locator('body'), `${name}-map-pending`);
    const fuelPending = await savedFuelPayload(page);
    assert.deepEqual(fuelPending, fuelBefore,
      `${name}: pending ETA refresh changed saved fuel metadata`);

    pending = false;
    const mapTime = await page.evaluate(() => Date.now());
    timing = {calculatedAt: new Date(mapTime).toISOString(), validUntil: new Date(mapTime + 120_000).toISOString()};
    await page.clock.fastForward(15_000);
    await page.waitForFunction(calculatedAt => Date.parse(window.hoursFixture?.etas?.eta?.calculatedAt) === Date.parse(calculatedAt)
      && window.hoursFixture?.etas?.refreshing === false, timing.calculatedAt);
    const mapScope = page.locator('.fleet-map-page');
    const mapHeldAppearance = await forecastAppearance(mapScope);
    await watchQuietReplacement(mapScope);
    const heldMap = holdPlanning = heldResponse();
    await page.clock.fastForward(15_000);
    await waitForHeld(heldMap, `${name}-map-deadline`);
    await page.clock.fastForward(100_000);
    assert.ok(await page.evaluate(() => Date.now()) > Date.parse(timing.validUntil), `${name}: Fleet HTTP did not cross ValidUntil`);
    assert.deepEqual(await forecastAppearance(mapScope), mapHeldAppearance,
      `${name}: held Fleet HTTP crossing ValidUntil changed current/future ETA text, times, status or colors`);
    assert.deepEqual(await savedFuelPayload(page), fuelBefore,
      `${name}: held ETA request altered the saved fuel metadata`);
    await checkRemovedDisplays(page.locator('body'), `${name}-map-held`);
    const mapReplacementTime = await page.evaluate(() => Date.now());
    timing = {calculatedAt: new Date(mapReplacementTime).toISOString(),
      validUntil: new Date(mapReplacementTime + 120_000).toISOString(), shiftMinutes: 5};
    heldMap.release();
    await page.waitForFunction(calculatedAt => Date.parse(window.hoursFixture?.etas?.eta?.calculatedAt) === Date.parse(calculatedAt)
      && window.hoursFixture?.etas?.refreshing === false, timing.calculatedAt);
    await page.waitForFunction(() => document.querySelector('.fleet-map-next-load-card .stop-hours__road')?.textContent.includes('07:10 PM'));
    const mapQuiet = await checkQuietReplacement(mapScope, `${name}-map-deadline`);
    assert.deepEqual(await savedFuelPayload(page), fuelBefore,
      `${name}: complete ETA replacement altered the saved fuel metadata`);
    await checkRemovedDisplays(page.locator('body'), `${name}-map-replaced`);
    const replacedMapRect = await stableMapRect(page, mapRect, `${name}-replaced-forecast`);
    await page.getByRole('button', {name: 'Close map information', exact: true}).click();
    await page.locator('.fleet-map-info-reserved.has-selection').waitFor({state: 'detached'});
    const clearedMapRect = await stableMapRect(page, mapRect, `${name}-cleared-selection`);
    report.cases.push({name, dispatchBefore, dispatchPending, selected, selectedUnknown, selectedPending,
      fuelBefore, fuelPending, headerGeometry, dispatchHeaderGeometry, addressGeometry, dispatchQuiet, mapQuiet, boardReads, planningReads,
      mapRects: {unselected: mapRect, loading: loadingMapRect, selected: selectedMapRect, pending: pendingMapRect,
        replaced: replacedMapRect, cleared: clearedMapRect}, overlayGeometry});
    await context.close();
  }
} catch (error) {
  report.failures.push(error.stack ?? String(error));
} finally {
  await browser.close();
  await writeFile(resolve(output, 'report.json'), JSON.stringify(report, null, 2));
}
console.log(JSON.stringify({cases: report.cases.length, failures: report.failures, browserErrors: report.browserErrors,
  unexpectedRequests: report.unexpectedRequests, output}, null, 2));
if (report.cases.length !== widths.length * themes.length || report.failures.length || report.browserErrors.length || report.unexpectedRequests.length) process.exitCode = 1;
