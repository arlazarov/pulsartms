import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { installReleaseArtifact } from './releaseArtifact.mjs';
import { checkMobileTruckScrolling } from './mobileTruckScrolling.mjs';
import { checkTruckReadingsLayout } from './truckReadingsLayout.mjs';
import { workspaceReadModel } from './uiSmokeWorkspace.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'MAP_TEST_ARTIFACT_DIR must identify a verified staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const fleetOnly = process.env.HOURS_TEST_FLEET_ONLY === '1';
const output = browserOutput(
  'hours-forecast',
  process.env.HOURS_TEST_OUTPUT_DIR,
);
const lifecycle = process.env.HOURS_TEST_LIFECYCLE === '1';
// One width and one theme can be asked for by name, so a case deep in the
// matrix can be re-run on its own while it is being worked on.
const widths = process.env.HOURS_TEST_WIDTHS
  ? process.env.HOURS_TEST_WIDTHS.split(',').map(Number)
  : lifecycle
    ? [1440]
    : [2344, 1920, 1440, 1200, 900, 390];
const themes = process.env.HOURS_TEST_THEMES
  ? process.env.HOURS_TEST_THEMES.split(',')
  : lifecycle
    ? ['light']
    : ['light', 'dark'];
const origin = 'http://localhost:5079';
const userId = '11111111-1111-1111-1111-111111111111';
const truckId = '22222222-2222-2222-2222-222222222222';
const currentId = '33333333-3333-3333-3333-333333333333';
const futureId = '44444444-4444-4444-4444-444444444444';
const stopIds = [1, 2, 3].map(
  index => `55555555-5555-5555-5555-${String(index).padStart(12, '0')}`,
);
const now = '2026-09-08T12:00:00Z';
const point = (latitude, longitude) => ({ latitude, longitude });
const stops = [
  {
    id: stopIds[0],
    sequence: 1,
    job: 'Delivery',
    name: 'Current receiving facility',
    city: 'Toronto',
    address: '100 Current Street, Toronto, ON, Canada',
    scheduledDate: '2026-09-08',
    scheduledTime: '16:00:00',
    latitude: 43.65,
    longitude: -79.38,
  },
  {
    id: stopIds[1],
    sequence: 1,
    job: 'Pickup',
    name: 'East logistics terminal',
    city: 'Kingston',
    address: '200 Future Avenue',
    scheduledDate: '2026-09-11',
    scheduledTime: '08:00:00',
    latitude: 44.23,
    longitude: -76.48,
  },
  {
    id: stopIds[2],
    sequence: 2,
    job: 'Delivery',
    name: 'Capital distribution centre',
    city: 'Ottawa',
    address: '300 Receiving Road',
    scheduledDate: '2026-09-11',
    scheduledTime: '18:00:00',
    latitude: 45.42,
    longitude: -75.7,
  },
].map(stop => ({ ...stop, province: 'ON', country: 'Canada' }));
const recap = {
  remainingMinutes: 600,
  nextRecapAt: '2026-09-09T00:00:00-04:00',
  nextRecapMinutes: 185,
  homeTimeZoneId: 'America/Toronto',
  recapVerified: true,
};
const estimate = (stop, index) => {
  const arrival = `${stop.scheduledDate}T${index === 2 ? '19:05:00' : stop.scheduledTime}-04:00`;
  return {
    dispatchId: index === 0 ? currentId : futureId,
    stopId: stop.id,
    arrival,
    timeZoneId: 'America/Toronto',
    appointment: `${stop.scheduledDate}T${stop.scheduledTime}-04:00`,
    lateMinutes: index === 2 ? 65 : 0,
    drivingMinutes: 600,
    restMinutes: 120,
    cycleAfterDeparture: recap,
    hours:
      index === 2
        ? {
            cycleAtArrivalMinutes: null,
            cycleAfterStopMinutes: null,
            drivingShortfallMinutes: null,
            firstCycleShortageAt: null,
            cycleVerified: false,
            alternatives: [],
            unavailableReason: 'Unverified cycle history fixture.',
          }
        : {
            cycleAtArrivalMinutes: -180,
            cycleAfterStopMinutes: index === 0 ? -180 : -300,
            drivingShortfallMinutes: 180,
            firstCycleShortageAt: '2026-09-08T15:00:00-04:00',
            cycleVerified: true,
            unavailableReason: null,
            alternatives:
              index === 0
                ? []
                : [
                    {
                      kind: 'recap',
                      arrival: '2026-09-11T10:00:00-04:00',
                      departure: '2026-09-11T12:00:00-04:00',
                      lateMinutes: 120,
                      cycleAfterStopMinutes: 100,
                      restStartedAt: now,
                      resumeAt: '2026-09-09T00:00:00-04:00',
                    },
                    {
                      kind: 'restart',
                      arrival: '2026-09-11T07:00:00-04:00',
                      departure: '2026-09-11T10:00:00-04:00',
                      lateMinutes: 0,
                      cycleAfterStopMinutes: 2000,
                      restStartedAt: now,
                      resumeAt: '2026-09-09T22:00:00Z',
                    },
                  ],
          },
  };
};
const estimates = stops.map(estimate);
const shiftTime = (value, minutes) => {
  const offset = value.match(/(?:Z|[+-]\d{2}:\d{2})$/)[0];
  return (
    new Date(
      Date.parse(`${value.slice(0, -offset.length)}Z`) + minutes * 60_000,
    )
      .toISOString()
      .slice(0, 19) + offset
  );
};
const forecast = (values, pending = false, timing = {}) => ({
  calculatedAt: timing.calculatedAt ?? (pending ? '2026-09-08T12:01:00Z' : now),
  validUntil: timing.validUntil ?? '2026-09-08T14:00:00Z',
  stops: pending
    ? []
    : values.map(value =>
        timing.shiftMinutes
          ? {
              ...value,
              arrival: shiftTime(value.arrival, timing.shiftMinutes),
              lateMinutes: value.lateMinutes + timing.shiftMinutes,
            }
          : value,
      ),
  assumptions: [],
  unavailableReason: pending ? 'Route refresh pending.' : null,
  routeUpdatePending: pending,
  cycleAtCalculation: recap,
  dutyStatus: {
    status: 'sleeperBerth',
    statusStartedAt: '2026-09-08T05:52:00Z',
    restStartedAt: '2026-09-08T05:52:00Z',
    observedAt: now,
    cycleResetHours: 34,
    cycleResetCountry: 'US',
    cycleResetRemainingMinutes: 1672,
  },
});
const loads = (pending, timing) =>
  [
    {
      id: currentId,
      truckId,
      loadNumber: 1441,
      orderNumber: 'CURRENT-1441',
      status: 'in_transit',
      stops: stops.slice(0, 1),
      eta: forecast(estimates.slice(0, 1), pending, timing),
      loadedMiles: 500,
      emptyMiles: 50,
      totalMiles: 550,
    },
    {
      id: futureId,
      truckId,
      loadNumber: 1442,
      orderNumber: 'FUTURE-1442',
      status: 'planned',
      stops: stops.slice(1),
      eta: forecast(estimates.slice(1), pending, timing),
      loadedMiles: 300,
      emptyMiles: 40,
      totalMiles: 340,
    },
  ].map(load => ({
    ...load,
    customerName: pending ? 'Fixture Customer refreshed' : 'Fixture Customer',
    driverName: 'Fixture Driver',
    truckNumber: '11006',
    trailerNumber: 'TR-100',
  }));
const fuelPlan = {
  truckId,
  dispatchIds: [currentId, futureId],
  calculatedAt: '2026-09-08T10:15:00Z',
  pricingDate: '2026-09-08',
  selectionVersion: 11,
  routeVersion: 1,
  startProgressMiles: 380,
  needsRefresh: false,
  reusedCheckedRoute: true,
  refreshReasons: [],
  remainingMiles: 460,
  stops: [
    {
      stationId: '77777777-7777-7777-7777-777777777777',
      visitKey: 'future-fuel:1',
      dispatchId: futureId,
      beforeStopId: stopIds[1],
      name: 'Kingston travel stop',
      point: point(44.1, -76.8),
      currentRouteMile: null,
      milesAhead: 100,
      buyGallons: 35,
      arrivalGallons: 45,
      departureGallons: 80,
      fillToTarget: false,
    },
  ],
  scheduleImpact: {
    calculatedAt: '2026-09-08T10:15:00Z',
    complete: true,
    cycleKnown: true,
    baselineCycleShort: false,
    cycleShort: true,
    addedMinutes: 30,
    addedLateMinutes: 30,
    stops: [],
    unavailableReason: null,
  },
};
const plan = {
  id: '66666666-6666-6666-6666-666666666666',
  dispatchId: currentId,
  truckId,
  version: 1,
  calculatedAt: now,
  originalPlannedMiles: 500,
  fromCurrentPosition: true,
  profile: {},
  fuelPlan,
  stops: [{ ...stops[0], point: point(stops[0].latitude, stops[0].longitude) }],
  tracking: { nextStopId: stopIds[0], passedStopIds: [], visitedStops: {} },
  route: {
    miles: 500,
    seconds: 30_000,
    warnings: [],
    points: [],
    legs: [
      {
        miles: 500,
        seconds: 30_000,
        points: [point(41.8, -87.6), point(43.65, -79.38)],
      },
    ],
  },
};
const planning = (pending, timing) => ({
  truckId,
  dispatchId: currentId,
  loadNumber: 1441,
  hos: {
    breakMs: 25_200_000,
    driveMs: 21_600_000,
    shiftMs: 28_800_000,
    cycleMs: 36_000_000,
    currentDutyStatus: 'sleeperBerth',
    updatedAt: now,
  },
  state: {
    profile: {},
    plan,
    apiConfigured: false,
    fuelPercent: 75,
    fuelUpdatedAt: '2026-09-08T10:00:00Z',
    eta: forecast(estimates, pending, timing),
    progress: {
      progressMiles: 380,
      remainingMiles: 120,
      remainingSeconds: 7200,
      distanceFromRouteMiles: 0,
      offRoute: false,
      locationStale: false,
      locationTime: now,
      position: point(41.8, -87.6),
    },
  },
});
const futureRoute = {
  id: futureId,
  loadNumber: 1442,
  status: 'planned',
  stopCount: 2,
  stops: stops.slice(1).map(stop => ({
    id: stop.id,
    latitude: stop.latitude,
    longitude: stop.longitude,
    job: stop.job,
    name: stop.name,
  })),
  deadhead: { miles: 40, points: [point(43.65, -79.38), point(44.23, -76.48)] },
  legs: [
    {
      miles: 300,
      seconds: 18_000,
      points: [point(44.23, -76.48), point(45.42, -75.7)],
    },
  ],
};
const truck = {
  truckId,
  unitNumber: '11006',
  driverName: 'Fixture Driver',
  trailerNumber: 'TR-100',
  latitude: 41.8,
  longitude: -87.6,
  speed: 45,
  heading: 90,
  updatedAt: now,
  engineState: 'Driving',
  fuelPercent: 75,
  outsideTemperatureCelsius: 22.5,
  outsideTemperatureUpdatedAt: now,
  formattedLocation: '200 Example Road, Chicago, IL 60601, US',
};
const success = response => ({ success: true, response, errors: [] });
// The viewport module is TypeScript, so the stub is built the way the other
// map probes build theirs. Pasting the source into a served module left type
// syntax the browser cannot parse: the import rejected, createFleetMap never
// ran and every Fleet Map case timed out waiting for its marker.
const mapStubSource = `
import {createCameraViewport} from './Scripts/fleetMap/ui/cameraViewport.ts';
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
  }, async background() {
    return callbacks.invokeMethodAsync('OnMapBackgroundClicked',
      selectedTruck, revision);
  }, async selectStop(index) {
    await transition('next-stop');
    return callbacks.invokeMethodAsync('OnNextLoadSelected', this.next.truckId,
      this.next.currentDispatchId, this.next.routes[0].id, index);
  }};
  return {setOptions(){},setTrucks(){},setStationsVisible(){},
    setTrafficVisible(){},setIfta(){},
    setPriceOverview(data,date,useIfta){fixture.prices={data,date,useIfta};},
    setInspectorMode(kind, truckId){selectedTruck = truckId; return transition(kind);},
    clearMapInspection(){return transition('closed');},setInspectionSuspended(){},
    clearSelection(){},clearNextLoads(){fixture.next = null;},setNextLoadsVisible(){},clearNextLoadSelection(){},
    setStopEtas(value){fixture.etas = value;},setLoadReference(){},
    setDistanceUnit(value){fixture.distanceUnit = value;},
    async setFollow(id, enabled){
      fixture.follow = enabled ?? !fixture.follow;
      fixture.followTruck = id;
      await callbacks.invokeMethodAsync('OnFollowChanged', fixture.follow);
    },
    setRouteBytes(bytes, progress, fit){
      fixture.plan = JSON.parse(new TextDecoder().decode(bytes));
      fixture.routeFits = (fixture.routeFits ?? 0) + Number(fit === true);
      return true;
    },
    showRoute(id){fixture.shownRoute = id;},
    setNextLoadsBytes(bytes){const data = JSON.parse(new TextDecoder().decode(bytes)); if(data.routes)fixture.next = data;},
    focusTruck(){
      fixture.focusCalls = (fixture.focusCalls ?? 0) + 1;
      return true;
    },
    dispose(){viewport.dispose();delete window.hoursFixture;}};
}`;
const mapStub = (
  await build({
    stdin: { resolveDir: process.cwd(), contents: mapStubSource },
    bundle: true,
    format: 'esm',
    write: false,
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
const report = {
  artifact,
  fleetOnly,
  scope:
    'Staged Blazor Dispatch and selected future-stop ETA/cycle cards with recap-only alternatives. Standalone fuel summary remains absent; saved future-trip fuel metadata stays on the map bridge through ETA polling. Deterministic API and map callbacks; no provider/GPU, backend authentication, database, or real calculations.',
  cases: [],
  failures: [],
  unexpectedRequests: [],
  browserErrors: [],
};
const browserChannel = process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome';
let browser;
await mkdir(output, { recursive: true });
const check = (condition, message) => {
  if (!condition) report.failures.push(message);
};
const normalize = text => text.replace(/\s+/g, ' ').trim();
async function stableMapRect(page, original, name) {
  const current = await page.locator('#fleet-map').evaluate(element => {
    const { x, y, width, height } = element.getBoundingClientRect();
    return {
      x,
      y,
      width,
      height,
      sameNode: window.hoursFixtureMapElement === element,
      sameInspectorHost:
        window.hoursFixtureInspectorHost ===
        document.querySelector('.fleet-map-inspector__native'),
    };
  });
  assert.equal(
    current.sameNode,
    true,
    `${name}: selection replaced the mounted map`,
  );
  assert.equal(
    current.sameInspectorHost,
    true,
    `${name}: selection replaced the native inspector host`,
  );
  for (const key of ['x', 'y', 'width', 'height'])
    check(
      Math.abs(current[key] - original[key]) <= 1,
      `${name}: map ${key} changed from ${original[key]} to ${current[key]}`,
    );
  return current;
}
// With no room for two panes the workspace shows one at a time under a row
// of tabs, and it opens on Details. Coming back to the itinerary is the tab
// a dispatcher presses. The tabs belong to the workspace, which is the
// frame around the load's details rather than a part of them.
async function showWorkspacePane(page, label) {
  const tab = page
    .locator('.stop-workspace__mobile-tabs')
    .getByRole('button', { name: label, exact: true });
  if (
    (await tab.count()) === 1 &&
    (await tab.isVisible()) &&
    (await tab.getAttribute('aria-pressed')) === 'false'
  )
    await tab.click();
}
async function openStopDetails(page, workspace, stopId, name = '') {
  await showWorkspacePane(page, 'Stops');
  const rows = workspace.locator('.stop-workspace__stop');
  // Read where a row sits in the itinerary, not where the itinerary
  // happens to be scrolled to. Opening a stop's details scrolls the pane
  // that holds it, and only the window's own scroll used to be taken back
  // out, so a row that had not moved read as though it had.
  const positions = () =>
    rows.evaluateAll(elements =>
      elements.map(element => {
        const rect = element.getBoundingClientRect();
        let scrolledX = 0,
          scrolledY = 0;
        for (
          let node = element.parentElement;
          node;
          node = node.parentElement
        ) {
          scrolledX += node.scrollLeft;
          scrolledY += node.scrollTop;
        }
        return {
          id: element.dataset.stopId,
          x: rect.x + scrolledX,
          y: rect.y + scrolledY,
          width: rect.width,
          height: rect.height,
          scrolledX,
          scrolledY,
        };
      }),
    );
  const before = await positions();
  const row = workspace.locator(`[data-stop-id="${stopId}"]`);
  const button = row.locator('.stop-workspace__select');
  await button.focus();
  await page.keyboard.press('Enter');
  const editor = workspace.locator(`#stop-editor-${stopId}`);
  await editor.waitFor();
  assert.equal(await button.getAttribute('aria-pressed'), 'true');
  assert.equal(await row.locator('.stop-workspace__editor').count(), 0);
  // Choosing a stop brings its details forward, which on a card too narrow
  // for two panes means leaving the itinerary. The itinerary is what this
  // measures, so it is asked for again before it is read.
  await showWorkspacePane(page, 'Stops');
  const after = await positions();
  const moved = after.filter(
    (bounds, index) =>
      before[index] === undefined ||
      bounds.id !== before[index].id ||
      !['x', 'y', 'width', 'height'].every(
        key => Math.abs(bounds[key] - before[index][key]) <= 1,
      ),
  );
  if (moved.length || after.length !== before.length)
    (report.movedItineraryRows ??= []).push({ name, stopId, before, after });
  check(
    after.length === before.length && moved.length === 0,
    `${name}: selecting a stop keeps every itinerary row in its original position`,
  );
  check(
    (await workspace.locator('.stop-workspace__editor').count()) === 1,
    'One selected stop editor appears below the complete itinerary',
  );
  return editor;
}
async function checkRemovedDisplays(scope, name) {
  check(
    (await scope.locator('.stop-hours__departure-cycle').count()) === 0,
    `${name}: removed departure balance returned`,
  );
  check(
    !/After service/i.test(await scope.textContent()),
    `${name}: removed service wording returned`,
  );
  check(
    (await scope.locator('[data-kind="restart"]').count()) === 0,
    `${name}: removed reset alternative returned`,
  );
  check(
    !/If reset|On time if reset/i.test(await scope.textContent()),
    `${name}: removed reset wording returned`,
  );
  check(
    (await scope.locator('.fuel-plan-summary').count()) === 0,
    `${name}: removed standalone fuel summary returned`,
  );
}
const savedFuelPayload = page =>
  page.evaluate(() => window.hoursFixture?.plan?.fuelPlan ?? null);
async function forecastAppearance(scope) {
  return scope.locator('.stop-hours').evaluateAll(cards =>
    cards.map(card => ({
      text: card.textContent.replace(/\s+/g, ' ').trim(),
      times: [...card.querySelectorAll('time')].map(node => node.dateTime),
      values: [
        ...card.querySelectorAll('.stop-hours__value, .stop-hours__status'),
      ].map(node => {
        const style = getComputedStyle(node);
        return {
          text: node.textContent.replace(/\s+/g, ' ').trim(),
          className: node.className,
          color: style.color,
          background: style.backgroundColor,
        };
      }),
    })),
  );
}
function heldResponse() {
  let arrive, resume;
  const entered = new Promise(resolve => {
    arrive = resolve;
  });
  const released = new Promise(resolve => {
    resume = resolve;
  });
  return {
    entered,
    release: () => resume(),
    block: async () => {
      arrive();
      await released;
    },
  };
}
async function waitForHeld(held, name) {
  let timer;
  try {
    await Promise.race([
      held.entered,
      new Promise((_, reject) => {
        timer = setTimeout(
          () =>
            reject(new Error(`${name}: the polling HTTP request was not held`)),
          10_000,
        );
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}
async function watchQuietReplacement(scope) {
  await scope.evaluate(element => {
    const identities = new WeakMap();
    let sequence = 0;
    const snapshot = () => {
      const cards = [...element.querySelectorAll('.stop-hours')];
      const bridge = window.hoursFixture?.etas;
      return {
        at: Date.now(),
        connected: element.isConnected,
        ids: cards.map(card => {
          if (!identities.has(card)) identities.set(card, ++sequence);
          return identities.get(card);
        }),
        rows: cards.map(card => card.textContent.replace(/\s+/g, ' ').trim()),
        eta: bridge && {
          calculatedAt: bridge.eta?.calculatedAt,
          validUntil: bridge.eta?.validUntil,
          pending: bridge.eta?.routeUpdatePending,
          refreshing: bridge.refreshing,
          stops: bridge.eta?.stops?.map(stop => stop.stopId),
        },
        emptyMarkup: cards.length
          ? undefined
          : [
              ...element.querySelectorAll(
                '.fleet-map-route-info, .fleet-map-next-load-card',
              ),
            ]
              .map(card => card.outerHTML)
              .join('\n')
              .slice(0, 12000),
      };
    };
    const initial = snapshot();
    const probe = {
      before: initial.rows,
      initial,
      samples: [],
      observations: [],
      snapshot,
    };
    probe.observer = new MutationObserver(() => {
      const value = snapshot();
      probe.samples.push(value.rows);
      probe.observations.push(value);
    });
    probe.observer.observe(element, {
      subtree: true,
      childList: true,
      characterData: true,
      attributes: true,
    });
    element.quietEtaProbe = probe;
  });
}
async function checkQuietReplacement(scope, name) {
  const result = await scope.evaluate(element => {
    const probe = element.quietEtaProbe;
    probe.observer.disconnect();
    delete element.quietEtaProbe;
    const final = probe.snapshot();
    return {
      before: probe.before,
      samples: probe.samples,
      after: final.rows,
      initial: probe.initial,
      observations: probe.observations,
      final,
    };
  });
  (report.etaProbes ??= []).push({ name, ...result });
  assert.equal(
    result.after.length,
    result.before.length,
    `${name}: replacement removed forecast cards`,
  );
  assert.notDeepEqual(
    result.after,
    result.before,
    `${name}: complete replacement did not change ETA values`,
  );
  for (const sample of result.samples) {
    assert.equal(
      sample.length,
      result.before.length,
      `${name}: an intermediate render removed forecast cards`,
    );
    sample.forEach((text, index) =>
      assert.ok(
        text === result.before[index] || text === result.after[index],
        `${name}: an intermediate render exposed an incomplete forecast`,
      ),
    );
  }
  return {
    samples: result.samples.length,
    retainedCards: result.before.length,
  };
}
async function measure(scope, name) {
  const result = await scope.evaluate(element => ({
    text: element.textContent.replace(/\s+/g, ' ').trim(),
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth,
    rows: [...element.querySelectorAll('.stop-hours__row')].map(row => {
      const rect = row.getBoundingClientRect();
      // A row past the edge of the window is only lost if nothing between
      // it and the page can be scrolled to reach it. A board shows more
      // loads than fit in a lane that scrolls sideways; the boxes that clip
      // the row are reported with it, and the innermost lane that does
      // scroll decides whether the row can be brought into view.
      const clippers = [];
      let reach = null;
      for (
        let node = row.parentElement;
        node && node !== document.documentElement;
        node = node.parentElement
      ) {
        const style = getComputedStyle(node);
        if (style.overflowX === 'visible') continue;
        const box = node.getBoundingClientRect();
        clippers.push({
          className: node.className,
          overflowX: style.overflowX,
          left: box.left,
          right: box.right,
          clientWidth: node.clientWidth,
          scrollWidth: node.scrollWidth,
          scrollLeft: node.scrollLeft,
        });
        if (
          reach === null &&
          ['auto', 'scroll'].includes(style.overflowX) &&
          node.scrollWidth > node.clientWidth + 1
        ) {
          const origin = box.left + node.clientLeft - node.scrollLeft;
          reach = { left: origin, right: origin + node.scrollWidth };
        }
      }
      return {
        text: row.textContent.replace(/\s+/g, ' ').trim(),
        left: rect.left,
        right: rect.right,
        scrollWidth: row.scrollWidth,
        clientWidth: row.clientWidth,
        clippers,
        reach,
      };
    }),
    viewport: innerWidth,
    documentWidth: document.documentElement.scrollWidth,
  }));
  check(
    result.scrollWidth <= result.clientWidth + 2,
    `${name}: card horizontal overflow`,
  );
  check(
    result.documentWidth <= result.viewport + 2,
    `${name}: document horizontal overflow`,
  );
  check(
    !/[\u0400-\u04ff]/u.test(result.text),
    `${name}: forecast card contains untranslated text`,
  );
  const tooWide = result.rows.filter(
    row =>
      !(
        row.scrollWidth <= row.clientWidth + 2 &&
        (row.reach
          ? row.left >= row.reach.left - 1 && row.right <= row.reach.right + 1
          : row.left >= -1 && row.right <= result.viewport + 1)
      ),
  );
  for (const row of tooWide) check(false, `${name}: row overflow: ${row.text}`);
  if (tooWide.length)
    (report.overflowingRows ??= []).push({
      name,
      viewport: result.viewport,
      rows: tooWide,
    });
  return result;
}
async function checkCycleAlignment(card, name) {
  const alignment = await card.locator('.stop-hours__road').evaluate(row => {
    const label = row
      .querySelector('.stop-hours__label')
      .getBoundingClientRect();
    const cycle = row
      .querySelector('.stop-hours__cycle-status')
      .getBoundingClientRect();
    const arrival = row
      .querySelector('.stop-hours__arrival')
      .getBoundingClientRect();
    const late = row.querySelector('.stop-hours__arrival .stop-hours__status');
    return {
      labelLeft: label.left,
      cycleLeft: cycle.left,
      cycleTop: cycle.top,
      arrivalBottom: arrival.bottom,
      lateTop: late?.getBoundingClientRect().top,
      timeTop: row.querySelector('time').getBoundingClientRect().top,
    };
  });
  check(
    Math.abs(alignment.labelLeft - alignment.cycleLeft) <= 1,
    `${name}: cycle warning is indented under the ETA value`,
  );
  check(
    alignment.cycleTop >= alignment.arrivalBottom - 1,
    `${name}: cycle warning overlaps the ETA row`,
  );
  if (alignment.lateTop !== undefined)
    check(
      Math.abs(alignment.lateTop - alignment.timeTop) <= 2,
      `${name}: known lateness no longer sits beside the ETA`,
    );
}
async function checkHeaderLoadingSpace(page, name) {
  const sizes = await page.evaluate(() => {
    const source = document.querySelector('.fleet-map-inspector');
    const map = document.querySelector('#fleet-map');
    const rect = node => {
      const r = node.getBoundingClientRect();
      return { x: r.x, y: r.y, width: r.width, height: r.height };
    };
    const before = rect(map);
    const host = source.cloneNode(true);
    host.style.visibility = 'hidden';
    host.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
    source.parentElement.append(host);
    const truckCopy = host.querySelector('.fleet-map-truck-info');
    const routeCopy = host.querySelector(
      '.fleet-map-route-info[aria-label="Current dispatch route"]',
    );
    const measure = () => ({
      truckHeight: truckCopy.getBoundingClientRect().height,
      routeHeight: routeCopy.getBoundingClientRect().height,
      map: rect(map),
      overlay: rect(host),
      maxHeight:
        (parseFloat(getComputedStyle(host).maxHeight) *
          map.getBoundingClientRect().height) /
        100,
      widthCap:
        parseFloat(
          getComputedStyle(host).getPropertyValue(
            '--size-map-compact-inspector',
          ),
        ) * parseFloat(getComputedStyle(document.documentElement).fontSize),
      insetCap:
        parseFloat(getComputedStyle(host).getPropertyValue('--space-md')) *
        parseFloat(getComputedStyle(document.documentElement).fontSize),
      position: getComputedStyle(host).position,
      truckWidth: truckCopy.clientWidth,
      truckScrollWidth: truckCopy.scrollWidth,
      routeWidth: routeCopy.clientWidth,
      routeScrollWidth: routeCopy.scrollWidth,
    });
    try {
      const ready = measure();
      // The map asks DriverHours for clocks without a duty line, so there
      // is no duty text to blank here.
      truckCopy.querySelectorAll('.driver-hours__dial strong').forEach(node => {
        node.textContent = '—';
      });
      truckCopy
        .querySelectorAll('.driver-hours__reading')
        .forEach(node => (node.textContent = '—'));
      routeCopy
        .querySelector(
          ':scope > .fleet-map-route-info__timing > .arrival-estimate',
        )
        .replaceChildren();
      routeCopy
        .querySelectorAll('.fleet-map-route-info__metric strong')
        .forEach(node => {
          node.firstChild.textContent = '— ';
        });
      const next = routeCopy.querySelector('.fleet-map-route-info__next');
      next
        .querySelectorAll(':scope > :not(.fleet-map-route-info__label)')
        .forEach(node => node.remove());
      host
        .querySelectorAll('.fleet-map-mobile-summary__remaining strong')
        .forEach(node => {
          node.textContent = '—';
        });
      routeCopy.querySelector(
        '.fleet-map-route-info__appointment strong',
      ).textContent = '—';
      return { before, ready, loading: measure() };
    } finally {
      host.remove();
    }
  });
  for (const state of [sizes.ready, sizes.loading]) {
    for (const key of ['x', 'y', 'width', 'height'])
      check(
        Math.abs(state.map[key] - sizes.before[key]) <= 1,
        `${name}: ${key} of the actual map shifts while inspector values load`,
      );
    const sideClearance = Math.max(
      0,
      Math.min(
        state.overlay.x - state.map.x,
        state.map.x + state.map.width - state.overlay.x - state.overlay.width,
      ),
    );
    check(
      state.position === 'absolute' &&
        state.overlay.height <= state.maxHeight + 1 &&
        Math.abs(
          state.overlay.x +
            state.overlay.width / 2 -
            state.map.x -
            state.map.width / 2,
        ) <= 1 &&
        Math.abs(
          state.overlay.y -
            state.map.y -
            Math.min(state.insetCap, sideClearance),
        ) <= 1 &&
        Math.abs(
          state.overlay.width - Math.min(state.map.width, state.widthCap),
        ) <= 1,
      `${name}: loading/ready inspector must remain centered and width-capped with coordinated top/side clearance`,
    );
    check(
      state.truckScrollWidth <= state.truckWidth + 1 &&
        state.routeScrollWidth <= state.routeWidth + 1,
      `${name}: loading/ready inspector content overflows horizontally`,
    );
  }
  if (sizes.ready.routeWidth >= 1101)
    check(
      sizes.ready.routeHeight <= 113,
      `${name}: ordinary route summary reserves excess vertical space`,
    );
  (report.headerLoadingSpace ??= []).push({ name, ...sizes });
}
async function checkInspectorLargeText(page, name) {
  const original = await page.evaluate(() => {
    const style = document.documentElement.style;
    const map = document.querySelector('#fleet-map').getBoundingClientRect();
    const saved = {
      value: style.getPropertyValue('font-size'),
      priority: style.getPropertyPriority('font-size'),
      root: parseFloat(getComputedStyle(document.documentElement).fontSize),
      label: parseFloat(
        getComputedStyle(document.querySelector('.fleet-map-route-info__label'))
          .fontSize,
      ),
      padding: parseFloat(
        getComputedStyle(document.querySelector('.fleet-map-truck-info'))
          .paddingLeft,
      ),
      map: { x: map.x, y: map.y, width: map.width, height: map.height },
    };
    style.setProperty('font-size', '200%', 'important');
    document.querySelector('.fleet-map-inspector').scrollTop = 0;
    return saved;
  });
  try {
    await page.waitForFunction(
      expected =>
        parseFloat(getComputedStyle(document.documentElement).fontSize) ===
          expected.root * 2 &&
        parseFloat(
          getComputedStyle(
            document.querySelector('.fleet-map-route-info__label'),
          ).fontSize,
        ) ===
          expected.label * 2 &&
        parseFloat(
          getComputedStyle(document.querySelector('.fleet-map-truck-info'))
            .paddingLeft,
        ) ===
          expected.padding * 2,
      original,
      { polling: 50 },
    );
    await page.screenshot({
      path: resolve(output, `${name}-selected-info-large-text.png`),
    });
    const layout = await page
      .locator('.fleet-map-info-content')
      .evaluate(element => {
        const rect = node => {
          const r = node.getBoundingClientRect();
          return { left: r.left, right: r.right, top: r.top, bottom: r.bottom };
        };
        const panels = [
          ...element.querySelectorAll(
            '.fleet-map-truck-info, .fleet-map-route-info',
          ),
        ];
        return panels.map(panel => ({
          bounds: rect(panel),
          width: panel.clientWidth,
          scroll: panel.scrollWidth,
          children: [...panel.children]
            .filter(child => getComputedStyle(child).display !== 'none')
            .map(child => ({
              ...rect(child),
              width: child.clientWidth,
              scroll: child.scrollWidth,
              className: child.className,
              // A group that scrolls says nothing about what is too wide
              // inside it. These are the boxes that reach past its edge or
              // hold more than they can show - the words that would not fold.
              past: [...child.querySelectorAll('*')]
                .filter(
                  node =>
                    node.getBoundingClientRect().right >
                      child.getBoundingClientRect().right + 1 ||
                    node.scrollWidth > node.clientWidth + 1,
                )
                .map(node => ({
                  className: node.className,
                  text: (node.textContent ?? '').trim().slice(0, 40),
                  right: node.getBoundingClientRect().right,
                  width: node.clientWidth,
                  scroll: node.scrollWidth,
                })),
            })),
        }));
      });
    const textSize = await page
      .locator('.fleet-map-route-info__label')
      .first()
      .evaluate(element => parseFloat(getComputedStyle(element).fontSize));
    check(
      Math.abs(textSize - original.label * 2) <= 0.1,
      `${name}: large-text probe did not apply 200% label sizing`,
    );
    // The clocks read as text in the card's head. At 200% they must still
    // stay inside the line that holds them rather than spill out of it.
    const clocks = await page
      .locator('.fleet-map-inspector__hours')
      .evaluate(element => {
        const panel = element.getBoundingClientRect();
        return [...element.querySelectorAll('.driver-hours__clock')].map(
          clock => {
            const box = clock.getBoundingClientRect();
            return {
              contained:
                box.left >= panel.left - 1 && box.right <= panel.right + 1,
              clipped: clock.scrollWidth > clock.clientWidth + 1,
            };
          },
        );
      });
    check(
      clocks.length === 4 &&
        clocks.every(clock => clock.contained && !clock.clipped),
      `${name}: enlarged HOS times stay inside the head's line unclipped`,
    );
    for (const panel of layout) {
      check(
        panel.scroll <= panel.width + 1,
        `${name}: 200% text overflows the inspector horizontally`,
      );
      for (const child of panel.children)
        check(
          child.left >= panel.bounds.left - 1 &&
            child.right <= panel.bounds.right + 1 &&
            child.scroll <= child.width + 1,
          `${name}: 200% text clips ${child.className}`,
        );
      for (let i = 0; i < panel.children.length; i++)
        for (const other of panel.children.slice(i + 1)) {
          const child = panel.children[i];
          check(
            !(
              child.left < other.right - 1 &&
              child.right > other.left + 1 &&
              child.top < other.bottom - 1 &&
              child.bottom > other.top + 1
            ),
            `${name}: 200% text overlaps ${child.className} and ${other.className}`,
          );
        }
    }
    (report.largeTextInspector ??= []).push({
      name,
      labelSize: textSize,
      ordinaryLabelSize: original.label,
      clocks,
      panels: layout,
    });
  } finally {
    await page.evaluate(saved => {
      const style = document.documentElement.style;
      if (saved.value)
        style.setProperty('font-size', saved.value, saved.priority);
      else style.removeProperty('font-size');
    }, original);
    await page.waitForFunction(
      expected => {
        const rect = document
          .querySelector('#fleet-map')
          .getBoundingClientRect();
        return (
          parseFloat(getComputedStyle(document.documentElement).fontSize) ===
            expected.root &&
          parseFloat(
            getComputedStyle(
              document.querySelector('.fleet-map-route-info__label'),
            ).fontSize,
          ) === expected.label &&
          parseFloat(
            getComputedStyle(document.querySelector('.fleet-map-truck-info'))
              .paddingLeft,
          ) === expected.padding &&
          ['x', 'y', 'width', 'height'].every(
            key => Math.abs(rect[key] - expected.map[key]) <= 1,
          )
        );
      },
      original,
      { polling: 50 },
    );
  }
}
async function checkTruckTypography(page, name, phase, units) {
  // Sizes and weights as the card's own stylesheets set them: labels and
  // echoes are small, a value is body weight 600 unless its own rule says
  // otherwise, and only the unit number is larger than the body.
  const groups = [
    {
      size: 12,
      selectors: [
        '.fleet-map-truck-info__reading > small',
        '.fuel-reading--metric .fuel-reading__label',
        '.driver-hours__label',
        '.fleet-map-truck-info__location > span',
        '.fleet-map-route-info__label',
        '.stop-hours__road > .stop-hours__label',
      ],
    },
    {
      size: 14,
      weight: 600,
      selectors: [
        '.fleet-map-route-info__next strong',
        '.fleet-map-route-info__appointment > strong',
        '.stop-hours__road time',
        '.fleet-map-route-info__metric strong',
        '.fleet-map-truck-info__reading > strong',
        '.fuel-reading--metric .fuel-reading__value',
        '[title="Copy order number"] > strong',
        '.fleet-map-route-info__facility',
      ],
    },
    {
      size: 14,
      weight: 400,
      selectors: [
        // The crew reads beside the unit, quietly; the address and the
        // unit of a distance are values that do not need the emphasis.
        '.fleet-map-inspector__driver',
        '.fleet-map-inspector__trailer',
        '.fleet-map-truck-info__location > strong',
        '.fleet-map-route-info__total',
        '.fleet-map-truck-info__reading > strong > small',
      ],
    },
    {
      size: 16,
      weight: 600,
      selectors: ['.fleet-map-inspector__title'],
    },
  ];
  // The second unit is an echo of the first and exists only when both are
  // asked for: small beside the run's total, and inline with the next
  // stop's distance at that reading's own size.
  if (units.distanceUnit === 'both') {
    groups.push({
      size: 12,
      selectors: [
        '.fleet-map-route-info__metric .fleet-map-route-info__secondary',
      ],
    });
    groups.push({
      size: 14,
      weight: 400,
      selectors: [
        '.fleet-map-route-info__distance > ' +
          '.fleet-map-route-info__secondary',
      ],
    });
  }
  const typography = await page
    .locator('.fleet-map-inspector[data-inspector-mode="truck"]')
    .evaluate((element, groups) => {
      const root = parseFloat(
        getComputedStyle(document.documentElement).fontSize,
      );
      return groups.flatMap(group =>
        group.selectors.map(selector => ({
          selector,
          expectedSize: (group.size * root) / 16,
          expectedWeight: group.weight,
          nodes: [...element.querySelectorAll(selector)].map(node => {
            const style = getComputedStyle(node);
            return {
              text: node.textContent.trim(),
              size: parseFloat(style.fontSize),
              weight: Number(style.fontWeight),
              family: style.fontFamily,
            };
          }),
        })),
      );
    }, groups);
  for (const sample of typography) {
    check(
      sample.nodes.length > 0,
      `${name}: typography fixture is missing ${sample.selector}`,
    );
    for (const node of sample.nodes) {
      check(
        Math.abs(node.size - sample.expectedSize) <= 0.1,
        `${name}: ${sample.selector} uses ${node.size}px instead of ${sample.expectedSize}px`,
      );
      if (sample.expectedWeight)
        check(
          node.weight === sample.expectedWeight,
          `${name}: ${sample.selector} uses inconsistent value emphasis`,
        );
    }
  }
  check(
    new Set(typography.flatMap(sample => sample.nodes.map(node => node.family)))
      .size === 1,
    `${name}: inspector text must use one shared font family`,
  );
  const hierarchy = await page
    .locator('.fleet-map-inspector[data-inspector-mode="truck"]')
    .evaluate(element => {
      const valueColor = getComputedStyle(
        element.querySelector('.fleet-map-truck-info__location > strong'),
      ).color;
      // The load and its order are the card head's own label/value pair;
      // the route card below says neither again.
      return ['.fleet-map-mobile-summary__order'].map(selector => {
        const label = element.querySelector(selector),
          value = label.querySelector('.fleet-map-inspector__value');
        const labelStyle = getComputedStyle(label),
          valueStyle = getComputedStyle(value);
        return {
          selector,
          labelColor: labelStyle.color,
          valueColor: valueStyle.color,
          expectedColor: valueColor,
          labelWeight: Number(labelStyle.fontWeight),
          valueWeight: Number(valueStyle.fontWeight),
        };
      });
    });
  for (const sample of hierarchy) {
    check(
      sample.labelColor !== sample.valueColor &&
        sample.valueColor === sample.expectedColor,
      `${name}: ${sample.selector} label and value need distinct semantic colors`,
    );
    check(
      sample.valueWeight > sample.labelWeight,
      `${name}: ${sample.selector} label and value need distinct emphasis`,
    );
  }
  (report.truckTypography ??= []).push({ name, phase, typography });
}
async function measureTruckControls(page, name) {
  const inspector = page.locator(
    '.fleet-map-inspector[data-inspector-mode="truck"]',
  );
  const toggle = inspector.getByRole('button', { name: 'Truck details' });
  check(
    (await toggle.count()) === 1 &&
      (await inspector
        .getByRole('button', { name: /^Details$|^Hide$/ })
        .count()) === 0,
    `${name}: the card has one disclosure and it is a chevron, not a word ` +
      'whose width changes as the card opens',
  );
  if ((await toggle.getAttribute('aria-expanded')) === 'true')
    await toggle.click();
  const closedToggle = await toggle.boundingBox();
  check(
    !(await inspector.locator('#fleet-map-details').isVisible()),
    `${name}: the closed card hides the lower section`,
  );
  // Half of what a dispatcher looks a truck up by stays in the closed card.
  for (const selector of [
    '.fleet-map-inspector__driver',
    '.fleet-map-inspector__trailer',
  ])
    check(
      await inspector.locator(selector).isVisible(),
      `${name}: ${selector} stays in the closed card`,
    );
  await toggle.click();
  const openToggle = await toggle.boundingBox();
  check(
    ['x', 'y', 'width', 'height'].every(
      key => Math.abs(closedToggle[key] - openToggle[key]) <= 1,
    ),
    `${name}: opening the card does not move its own control`,
  );
  for (const selector of [
    '#fleet-map-telemetry-details',
    '#fleet-map-route-details',
    '#fleet-map-truck-location',
    '.fleet-map-inspector__driver',
    '.fleet-map-inspector__trailer',
    '.fleet-map-inspector__actions',
  ])
    check(
      await inspector.locator(selector).isVisible(),
      `${name}: ${selector} is shown once the card is open`,
    );
  const result = await page
    .getByRole('button', { name: 'Close map information', exact: true })
    .evaluate(element => {
      const style = getComputedStyle(element),
        bounds = element.getBoundingClientRect();
      const root = parseFloat(
        getComputedStyle(document.documentElement).fontSize,
      );
      return {
        width: bounds.width,
        height: bounds.height,
        minimumHeight: ((innerWidth < 800 ? 44 : 32) * root) / 16,
        flexGrow: Number(style.flexGrow),
        titleVisible:
          document
            .querySelector('.fleet-map-inspector__title')
            .getBoundingClientRect().width > 0,
      };
    });
  check(
    result.titleVisible &&
      result.flexGrow === 0 &&
      Math.abs(result.width - result.height) <= 1,
    `${name}: Close stays square beside the separate visible truck identity`,
  );
  check(
    Math.abs(result.height - result.minimumHeight) <= 1,
    `${name}: Close matches compact desktop and touch mobile action heights`,
  );
  return result;
}
async function checkIndependentRouteGroups(page, name) {
  const result = await page
    .locator('.fleet-map-inspector[data-inspector-mode="truck"]')
    .evaluate(source => {
      const host = source.cloneNode(true);
      host.style.visibility = 'hidden';
      host.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
      source.parentElement.append(host);
      try {
        const route = host.querySelector(
          '.fleet-map-route-info[aria-label="Current dispatch route"]',
        );
        route.classList.remove('has-final-stop');
        const bounds = node => {
          const rect = node.getBoundingClientRect(),
            origin = route.getBoundingClientRect();
          return {
            x: rect.x - origin.x,
            y: rect.y - origin.y,
            width: rect.width,
            height: rect.height,
          };
        };
        const measure = () => ({
          eta: bounds(
            route.querySelector(
              ':scope > .fleet-map-route-info__timing > .arrival-estimate',
            ),
          ),
          timingChildren: [
            ...route.querySelector('.fleet-map-route-info__timing').children,
          ].filter(node => !node.hidden).length,
          groups: [
            '.fleet-map-route-info__distances',
            '.fleet-map-route-info__visit',
          ].map(selector => {
            const group = route.querySelector(selector);
            const children = [...group.children]
              .filter(node => getComputedStyle(node).display !== 'none')
              .map(bounds);
            const style = getComputedStyle(group);
            return {
              selector,
              ...bounds(group),
              children,
              rowGap: parseFloat(style.rowGap),
              columnGap: parseFloat(style.columnGap),
            };
          }),
        });
        const before = measure();
        const cycle = route.querySelector('.stop-hours__cycle');
        for (let i = 0; i < 4; i++)
          cycle.append(cycle.firstElementChild.cloneNode(true));
        if (
          route.clientWidth >=
          52 * parseFloat(getComputedStyle(document.documentElement).fontSize)
        )
          route.querySelector('.fleet-map-route-info__total').textContent =
            'Total 3,003 mi · 4,833 km';
        return { before, after: measure() };
      } finally {
        host.remove();
      }
    });
  check(
    result.after.eta.height > result.before.eta.height + 20,
    `${name}: extra forecast rows must exercise an increased ETA column height`,
  );
  // The facts measured against the stop: the cycle it leaves with, and the
  // fuel it arrives on.
  check(
    result.before.timingChildren === 2 && result.after.timingChildren === 2,
    `${name}: the timing column holds the cycle and the arrival fuel ` +
      `(${result.before.timingChildren}/${result.after.timingChildren})`,
  );
  for (const [index, group] of result.before.groups.entries()) {
    const after = result.after.groups[index];
    // The run's total is the one distance fact; the stop keeps its address
    // and the window it has to make.
    const expectedChildren = group.selector.endsWith('__visit') ? 2 : 1;
    check(
      group.children.length === expectedChildren &&
        after.children.length === expectedChildren,
      `${name}: the run's total is the sole distance metric and the visit ` +
        `keeps its address and window (${group.selector}: ` +
        `${group.children.length})`,
    );
    // The stop is the left column of a two-column card, so a taller
    // forecast stretches it by design; it may not move or change width.
    const fixed = group.selector.endsWith('__visit')
      ? ['x', 'y', 'width']
      : ['x', 'y', 'width', 'height'];
    for (const key of fixed)
      check(
        Math.abs(group[key] - after[key]) <= 1,
        `${name}: longer ETA stretches ${group.selector} ${key}`,
      );
    for (const sample of [group, after]) {
      if (sample.children.length < 2) continue;
      const [first, second] = sample.children;
      const horizontal = Math.abs(first.y - second.y) <= 1;
      const gap = horizontal
        ? second.x - first.x - first.width
        : second.y - first.y - first.height;
      check(
        gap >= -1 && gap <= (horizontal ? sample.columnGap : sample.rowGap) + 1,
        `${name}: ${sample.selector} reserves blank space between adjacent facts`,
      );
    }
  }
  (report.independentRouteGroups ??= []).push({ name, ...result });
}
async function checkNextStopDistanceLayout(page, name) {
  await page
    .locator('.fleet-map-inspector[data-inspector-mode="truck"]')
    .evaluate(source => {
      const host = source.cloneNode(true);
      host.dataset.nextDistancePreview = 'true';
      host.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
      host
        .querySelector('.fleet-map-route-info')
        .classList.remove('has-final-stop');
      host.querySelector(
        '.fleet-map-route-info__visit-heading > .fleet-map-route-info__label',
      ).textContent = 'Pick Up';
      source.parentElement.append(host);
    });
  const preview = page.locator('[data-next-distance-preview]');
  try {
    const result = await preview.evaluate(host => {
      const route = host.querySelector('.fleet-map-route-info');
      const heading = route.querySelector(
        '.fleet-map-route-info__visit-heading',
      );
      const distance = heading.querySelector('.fleet-map-route-info__distance');
      const rect = node => {
        const { x, y, width, height } = node.getBoundingClientRect();
        return { x, y, width, height };
      };
      return {
        wide:
          route.clientWidth >=
          52 * parseFloat(getComputedStyle(document.documentElement).fontSize),
        metrics: route.querySelectorAll('.fleet-map-route-info__distances > *')
          .length,
        heading: rect(heading),
        label: rect(heading.firstElementChild),
        distance: rect(distance),
        overflow: heading.scrollWidth > heading.clientWidth + 1,
        visible: getComputedStyle(distance).display !== 'none',
      };
    });
    check(
      result.metrics === 1 && result.visible && !result.overflow,
      `${name}: next-stop distance is visible beside the visit without a second Remaining metric or overflow`,
    );
    if (result.wide)
      check(
        result.distance.x >= result.label.x + result.label.width &&
          result.distance.y < result.label.y + result.label.height,
        `${name}: next-stop distance shares the pickup heading line on wide cards`,
      );
    (report.nextStopDistance ??= []).push({ name, ...result });
    await preview.screenshot({
      path: resolve(output, `${name}-next-pickup.png`),
    });
  } finally {
    await preview.evaluate(host => host.remove());
  }
}
async function checkRouteFactRows(page, name, distanceUnit) {
  const result = await page
    .locator('.fleet-map-inspector[data-inspector-mode="truck"]')
    .evaluate(source => {
      const measure = host => {
        const route = host.querySelector(
          '.fleet-map-route-info[aria-label="Current dispatch route"]',
        );
        const timing = route.querySelector('.fleet-map-route-info__timing');
        const rect = node => {
          const bounds = node.getBoundingClientRect();
          const marker = document.createElement('span');
          marker.style.cssText =
            'display:inline-block;width:0;height:0;vertical-align:baseline';
          node.append(marker);
          const baseline = marker.getBoundingClientRect().top;
          marker.remove();
          return {
            left: bounds.left,
            right: bounds.right,
            top: bounds.top,
            bottom: bounds.bottom,
            baseline,
          };
        };
        const metric = route.querySelector('.fleet-map-route-info__metric');
        const visit = route.querySelector('.fleet-map-route-info__visit');
        // The delivery window belongs to the visit it is measured against,
        // not to the timing column beside it.
        const appointment = route.querySelector(
          '.fleet-map-route-info__appointment',
        );
        const eta =
          host.querySelector(
            '.fleet-map-inspector__arrival .arrival-estimate .stop-hours__road',
          ) ?? timing.querySelector('.stop-hours__road');
        const labelText = node => {
          const range = document.createRange();
          range.selectNodeContents(node);
          return range.getBoundingClientRect().right;
        };
        return {
          wide:
            route.clientWidth >=
            52 *
              parseFloat(getComputedStyle(document.documentElement).fontSize),
          factGap: parseFloat(getComputedStyle(appointment).columnGap),
          appointmentTextRight: labelText(appointment.firstElementChild),
          etaTextRight: labelText(eta.querySelector('.stop-hours__label')),
          label: rect(metric.querySelector('.fleet-map-route-info__label')),
          miles: rect(metric.querySelector('strong')),
          kilometres: metric.querySelector('.fleet-map-route-info__secondary')
            ? rect(metric.querySelector('.fleet-map-route-info__secondary'))
            : null,
          appointment: rect(appointment),
          appointmentLabel: rect(
            appointment.querySelector('.fleet-map-route-info__label'),
          ),
          appointmentValue: rect(appointment.querySelector('strong')),
          eta: rect(eta),
          etaLabel: rect(eta.querySelector('.stop-hours__label')),
          etaTime: rect(eta.querySelector('time')),
          etaStatuses: [
            ...eta.querySelectorAll('.stop-hours__arrival .stop-hours__status'),
          ].map(rect),
          cycleWarnings: [
            ...eta.querySelectorAll('.stop-hours__cycle-status'),
          ].map(node => ({
            ...rect(node),
            text: node.textContent.trim(),
          })),
          visitWidth: visit.clientWidth,
          visitScrollWidth: visit.scrollWidth,
          distancesWidth: route.querySelector(
            '.fleet-map-route-info__distances',
          ).clientWidth,
          timingWidth: timing.clientWidth,
          timingScrollWidth: timing.scrollWidth,
          appointmentWidth: appointment.clientWidth,
          appointmentScrollWidth: appointment.scrollWidth,
        };
      };
      const normal = measure(source);
      const host = source.cloneNode(true);
      host.style.visibility = 'hidden';
      host.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
      source.parentElement.append(host);
      try {
        host.querySelector(
          '.fleet-map-route-info__appointment > strong',
        ).textContent = 'Sep 8 · 04:00 PM – 06:00 PM';
        host.querySelector('.fleet-map-route-info__facility').textContent =
          'COSTCO SE DEPOT 174';
        host.querySelector(
          '.fleet-map-route-info__address-lines > span:not(.fleet-map-route-info__facility)',
        ).textContent = '13077 SW Anthony F. Sansone Sr. Blvd';
        host.querySelector('.fleet-map-route-info__address').textContent =
          'Port St. Lucie, FL 34987, US';
        return { normal, window: measure(host) };
      } finally {
        host.remove();
      }
    });
  for (const [variant, layout] of Object.entries(result)) {
    // Every fact is the same two cells: a quiet label, then its value.
    check(
      Math.abs(layout.miles.baseline - layout.label.baseline) <= 1 &&
        layout.miles.left >= layout.label.right - 1,
      `${name}-${variant}: the run's label and its value read as one row`,
    );
    check(
      distanceUnit === 'both'
        ? layout.kilometres !== null &&
            (layout.kilometres.left >= layout.miles.right - 1 ||
              layout.kilometres.top >= layout.miles.bottom - 1)
        : layout.kilometres === null,
      `${name}-${variant}: the second unit echoes the first, never before it`,
    );
    // The arrival reads in the card's head, above everything the card
    // opens; the window the stop has to make reads with the stop.
    check(
      layout.eta.bottom <= layout.appointment.top + 1,
      `${name}-${variant}: the ETA reads above the stop's window`,
    );
    check(
      layout.timingScrollWidth <= layout.timingWidth + 1 &&
        layout.appointmentScrollWidth <= layout.appointmentWidth + 1,
      `${name}-${variant}: the appointment window overflows its timing column`,
    );
    check(
      layout.visitScrollWidth <= layout.visitWidth + 1,
      `${name}-${variant}: facility and long address overflow their location column`,
    );
    check(
      layout.cycleWarnings.length === 1 &&
        layout.cycleWarnings.every(
          warning =>
            warning.text === 'Cycle short' &&
            warning.bottom > warning.top &&
            warning.top >=
              Math.max(
                layout.etaTime.bottom,
                ...layout.etaStatuses.map(status => status.bottom),
              ) -
                1 &&
            warning.left >= layout.eta.left - 1 &&
            warning.right <= layout.eta.right + 1,
        ),
      `${name}-${variant}: Cycle short remains visible below the arrival row`,
    );
    if (layout.wide) {
      check(
        Math.abs(
          layout.appointmentValue.left -
            layout.appointmentTextRight -
            layout.factGap,
        ) <= 1,
        `${name}-${variant}: the appointment keeps its own text gap`,
      );
      check(
        Math.abs(layout.visitWidth - layout.distancesWidth) <= 1,
        `${name}-${variant}: the stop and the facts share the card's width`,
      );
      check(
        Math.abs(
          layout.appointmentLabel.baseline - layout.appointmentValue.baseline,
        ) <= 1 &&
          layout.appointmentValue.left >= layout.appointmentLabel.right - 1,
        `${name}-${variant}: wide appointments keep their label and value inline`,
      );
      check(
        Math.abs(layout.etaLabel.baseline - layout.etaTime.baseline) <= 1 &&
          layout.etaTime.left >= layout.etaLabel.right - 1 &&
          layout.etaStatuses.every(
            status => Math.abs(status.baseline - layout.etaTime.baseline) <= 1,
          ),
        `${name}-${variant}: wide ETA keeps label, time and status inline`,
      );
    }
  }
  (report.routeFactRows ??= []).push({ name, ...result });
}

// The selected-truck card opens collapsed at every width: the top line
// carries the load, the miles left to the next stop and the clocks, and
// everything else waits behind the chevron. Every new selection closes it
// again, so anything that reads the lower section opens it first, by the
// same click a dispatcher would use.
async function expandTruckCard(page, name) {
  const toggle = page.getByRole('button', { name: 'Truck details' });
  if ((await toggle.getAttribute('aria-expanded')) === 'false')
    await toggle.click();
  check(
    (await toggle.getAttribute('aria-expanded')) === 'true' &&
      (await page.locator('#fleet-map-details').isVisible()),
    `${name}: the chevron opens the truck card`,
  );
}

async function truckLoadingGeometry(page) {
  return page
    .locator('.fleet-map-inspector[data-inspector-mode="truck"]')
    .evaluate(host => {
      // Named, so a selector the card no longer has says which one it is
      // instead of throwing on null.
      const rect = selector => {
        const element = host.querySelector(selector);
        if (!element) throw new Error(`No ${selector} in the truck card`);
        const { x, y, width, height } = element.getBoundingClientRect();
        return { x, y, width, height };
      };
      return Object.fromEntries(
        [
          '.fleet-map-inspector__header',
          // The head's own rows: the card is anchored at the map's top, so
          // anything that grows here pushes every row below it down.
          '.fleet-map-inspector__identity',
          '.fleet-map-inspector__arrival',
          '.fleet-map-inspector__clocks',
          '.fleet-map-truck-info',
          '.fleet-map-inspector__hours',
          '.fleet-map-truck-info__location',
          '.fleet-map-route-info',
          // The load and its order moved into the header line; the group is
          // drawn empty so nothing to the right of it shifts as they land.
          '.fleet-map-mobile-summary__remaining',
          '.fleet-map-route-info__distances',
          '.fleet-map-route-info__visit',
          '.fleet-map-route-info__timing',
          '.fleet-map-route-info__delivery',
          '.fleet-map-route-info__delivery > .fleet-map-route-info__label',
          'button[aria-label="Route options"]',
        ].map(selector => [selector, rect(selector)]),
      );
    });
}

try {
  for (const width of widths)
    for (const theme of themes) {
      const name = `${width}-${theme}`;
      browser = await chromium.launch({
        headless: true,
        ...(browserChannel ? { channel: browserChannel } : {}),
      });
      const units =
        theme === 'dark'
          ? { temperatureUnit: 'celsius', distanceUnit: 'kilometers' }
          : width === 390
            ? { temperatureUnit: 'fahrenheit', distanceUnit: 'miles' }
            : { temperatureUnit: 'both', distanceUnit: 'both' };
      let pending = false;
      let boardReads = 0;
      let planningReads = 0;
      let summaryReads = 0;
      let apiReads = 0;
      let timing = {};
      let holdBoard = null;
      let holdPlanning = null;
      let holdPreview = null;
      let holdDetails = null;
      const context = await browser.newContext({
        viewport: { width, height: 1000 },
        colorScheme: theme,
        locale: 'en-US',
        timezoneId: 'America/Toronto',
        reducedMotion: 'no-preference',
        serviceWorkers: 'block',
      });
      await context.addInitScript(
        ({ userId, theme }) => {
          window.google = {
            maps: {
              importLibrary: async () => {},
              // The stop map switches to satellite and back, which moves the
              // camera by type, centre and tilt. Without these the real
              // component throws and the run stops at the first stop opened.
              Map: class {
                fitBounds() {}
                setZoom() {}
                setCenter() {}
                setMapTypeId() {}
                setTilt() {}
              },
              LatLngBounds: class {
                extend() {}
              },
              Polyline: class {
                setMap() {}
              },
              marker: {
                AdvancedMarkerElement: class {
                  constructor(options) {
                    Object.assign(this, options);
                  }
                },
              },
              event: { clearInstanceListeners() {} },
            },
          };
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
          Object.defineProperty(navigator, 'clipboard', {
            configurable: true,
            value: {
              writeText: async text => {
                window.hoursFixtureCopiedText = text;
              },
            },
          });
        },
        { userId, theme },
      );
      await installReleaseArtifact(context, artifact, origin);
      await context.route('**/*', async route => {
        const request = route.request();
        const url = new URL(request.url());
        if (url.origin === origin && url.pathname.startsWith('/api/'))
          apiReads++;
        let fixture;
        if (
          url.origin === origin &&
          request.method() === 'POST' &&
          url.pathname === '/api/dispatch/board/planning'
        ) {
          summaryReads++;
          const scope = request.postDataJSON();
          assert.equal(scope.page, 1);
          assert.equal(typeof scope.search, 'string');
          const summary = structuredClone(planning(pending, timing));
          summary.state.plan.geometryOmitted = true;
          summary.state.plan.route.points = [];
          for (const leg of summary.state.plan.route.legs) leg.points = [];
          await route.fulfill({ status: 200, json: success([summary]) });
        } else if (
          url.origin === origin &&
          request.method() === 'POST' &&
          url.pathname === `/api/fleet/trucks/${truckId}/planning`
        ) {
          planningReads++;
          if (holdPlanning) {
            const held = holdPlanning;
            holdPlanning = null;
            await held.block();
          }
          await route.fulfill({
            status: 200,
            json: success(planning(pending, timing)),
          });
        } else if (
          url.origin !== origin ||
          !['GET', 'HEAD'].includes(request.method())
        ) {
          report.unexpectedRequests.push(
            `${request.method()} ${url.origin}${url.pathname}`,
          );
          await route.abort('blockedbyclient');
        } else if (url.pathname.startsWith('/api/')) {
          if (url.pathname === '/api/auth/me')
            fixture = {
              id: userId,
              name: 'Fixture Administrator',
              email: 'fixture@example.invalid',
              isAdmin: true,
            };
          else if (url.pathname === '/api/settings/appearance')
            fixture = success({ theme, ...units });
          else if (url.pathname === '/api/fuel/price-overview')
            fixture = success([]);
          // The map asks for the fuel price basis beside itself. It keeps the
          // page's own default, so the answer changes nothing here.
          else if (url.pathname === '/api/settings/planning')
            fixture = success({
              preferences: { useIfta: true },
              revision: 1,
              updatedAt: null,
            });
          else if (url.pathname === '/api/settings/dispatch')
            fixture = success({
              loadNumberPrefix: 'AMF',
              revision: 1,
              updatedAt: null,
              ...units,
            });
          else if (url.pathname === '/api/dispatch/board/telemetry') {
            assert.deepEqual(url.searchParams.getAll('truckIds'), [truckId]);
            fixture = success([
              {
                truckId,
                speed: truck.speed,
                engineState: truck.engineState,
                trailerNumber: truck.trailerNumber,
              },
            ]);
          } else if (url.pathname === `/api/fleet/trucks/${truckId}/weather`)
            fixture = success({
              celsius: 22.5,
              condition: 'CLEAR',
              description: 'Clear',
              isDaytime: true,
              updatedAt: now,
            });
          else if (url.pathname === '/api/fleet/locations')
            fixture = success({ trucks: [truck], points: [truck] });
          else if (url.pathname === '/api/fleet/hos')
            fixture = success({
              [truckId]: {
                driverName: truck.driverName,
                hos: planning(pending, timing).hos,
              },
            });
          else if (url.pathname === '/api/dispatch/board/enrichment') {
            const financials =
              url.searchParams.get('includeFinancials') === 'true';
            fixture = success([
              {
                key: truckId,
                truckId,
                driverName: truck.driverName,
                currentCycle: financials
                  ? null
                  : {
                      calculatedAt: now,
                      validUntil: '2026-09-08T14:00:00Z',
                      cycle: recap,
                    },
                dispatches: loads(pending, timing).map(load => ({
                  id: load.id,
                  truckId,
                  lastSyncedAt: '0001-01-01T00:00:00',
                  planningAssignmentRevision: 0,
                  routeChoiceRevision: 0,
                  stops: load.stops.map(stop => ({
                    id: stop.id,
                    manualCompletionRevision: 0,
                    operationRevision: 0,
                  })),
                  financials: financials
                    ? {
                        emptyMiles: load.emptyMiles,
                        totalMiles: load.totalMiles,
                        emptyMilesStatus: 'available',
                      }
                    : null,
                  eta: financials ? null : load.eta,
                })),
              },
            ]);
          } else if (url.pathname === '/api/fleet/planning/previews')
            fixture = success([]);
          else if (url.pathname === '/api/dispatch/board') {
            boardReads++;
            if (holdBoard) {
              const held = holdBoard;
              holdBoard = null;
              await held.block();
            }
            fixture = success({
              items: [
                {
                  key: truckId,
                  truckId,
                  truckNumber: '11006',
                  driverName: truck.driverName,
                  trailerNumber: truck.trailerNumber,
                  hos: planning(pending, timing).hos,
                  currentCycle: {
                    calculatedAt: now,
                    validUntil: '2026-09-08T14:00:00Z',
                    cycle: recap,
                  },
                  dispatches: loads(pending, timing),
                },
              ],
              page: 1,
              pageSize: 20,
              totalCount: 1,
              totalPages: 1,
            });
          } else if (
            url.pathname === `/api/fleet/trucks/${truckId}/planning/preview`
          ) {
            if (holdPreview) {
              const held = holdPreview;
              holdPreview = null;
              await held.block();
            }
            const preview = structuredClone(planning(pending, timing));
            preview.hos = null;
            preview.state.eta = null;
            preview.state.plan.fuelPlan = null;
            for (const stop of preview.state.plan.stops)
              for (const field of [
                'scheduledDate',
                'scheduledTime',
                'scheduledDate2',
                'scheduledTime2',
              ])
                stop[field] = null;
            fixture = success(preview);
          } else if (
            [currentId, futureId].some(
              id => url.pathname === `/api/dispatch/${id}/planning/map`,
            )
          ) {
            fixture = success({
              dispatchId: url.pathname.split('/')[3],
              segments: [],
              missingSections: 0,
            });
          } else if (
            [currentId, futureId].some(
              id => url.pathname === `/api/dispatch/${id}/workspace`,
            )
          ) {
            const load = loads(pending, timing).find(
              item => url.pathname === `/api/dispatch/${item.id}/workspace`,
            );
            fixture = success({
              ...workspaceReadModel(load),
              sourceUpdatedAt: now,
            });
          } else if (
            [currentId, futureId].some(
              id => url.pathname === `/api/dispatch/${id}/activity`,
            )
          ) {
            fixture = success({
              dispatchId: url.pathname.split('/')[3],
              revision: 0,
              items: [],
              openItems: [],
              openCount: 0,
            });
          } else if (
            [currentId, futureId].some(
              id => url.pathname === `/api/dispatch/${id}/documents`,
            )
          )
            fixture = success([]);
          else if (url.pathname === `/api/dispatch/${currentId}`) {
            if (holdDetails) {
              const held = holdDetails;
              holdDetails = null;
              await held.block();
            }
            fixture = success(loads(pending, timing)[0]);
          } else if (url.pathname === `/api/dispatch/${futureId}`)
            fixture = success(loads(pending, timing)[1]);
          else if (url.pathname === `/api/dispatch/truck/${truckId}`)
            fixture = success(loads(pending, timing));
          else if (
            url.pathname === `/api/dispatch/truck/${truckId}/next-routes`
          )
            fixture = success({
              revision: 'fixture-v1',
              unchanged: url.searchParams.get('revision') === 'fixture-v1',
              routes: [futureRoute],
            });
          if (!fixture)
            report.unexpectedRequests.push(`Unmocked API ${url.pathname}`);
          await route.fulfill({
            status: fixture ? 200 : 500,
            json: fixture ?? { success: false, errors: ['Unmocked API'] },
          });
        } else if (
          /\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(
            url.pathname,
          )
        )
          await route.fulfill({
            status: 200,
            contentType: 'text/javascript',
            body: mapStub,
          });
        else if (request.isNavigationRequest())
          await route.fulfill({
            status: 200,
            contentType: 'text/html',
            body: html,
          });
        else await route.fallback();
      });
      const page = await context.newPage();
      page.on('pageerror', error =>
        report.browserErrors.push(`${name}: ${error.message}`),
      );
      page.on('console', message => {
        if (message.type() === 'error')
          report.browserErrors.push(`${name}: ${message.text()}`);
      });
      await page.clock.install({ time: new Date(now) });
      let dispatchBefore = null,
        dispatchPending = null,
        dispatchHeaderGeometry = null,
        dispatchQuiet = null;
      if (!fleetOnly) {
        await page.goto(`${origin}/dispatch`);
        await page
          .locator('.dispatch-load__stop-times .stop-hours')
          .nth(2)
          .waitFor();
        const current = page.locator(
          '.dispatch-load--current .dispatch-load__stop',
        );
        const futureCard = page.locator(
          '.dispatch-load:not(.dispatch-load--current)',
        );
        const future = page.locator('.dispatch-details');
        const board = page.locator('.dispatch-truck');
        check(
          (await page
            .locator('.dispatch-load details, .dispatch-load-dialog')
            .count()) === 0,
          `${name}: supplemental details should not create inline disclosures or an initially open dialog`,
        );
        check(
          (await page.locator('.dispatch-load .stop-hours__cycle').count()) ===
            0,
          `${name}: cycle balances belong in load details, not the compact main card`,
        );
        check(
          (await page
            .locator('.dispatch-load__stop-times .stop-hours__road:visible')
            .count()) === 3,
          `${name}: all three compact stop ETA/status summaries must remain visible`,
        );
        const summaryWarnings = await page
          .locator('.dispatch-load__stop--summary .stop-hours__road')
          .evaluateAll(rows =>
            rows.map(row => {
              const label = row
                .querySelector('.stop-hours__label')
                .getBoundingClientRect();
              const cycle = row
                .querySelector('.stop-hours__cycle-status')
                .getBoundingClientRect();
              const arrival = row
                .querySelector('.stop-hours__arrival')
                .getBoundingClientRect();
              return {
                labelLeft: label.left,
                cycleLeft: cycle.left,
                cycleTop: cycle.top,
                arrivalBottom: arrival.bottom,
              };
            }),
          );
        for (const warning of summaryWarnings) {
          check(
            Math.abs(warning.cycleLeft - warning.labelLeft) <= 1,
            `${name}: summary cycle warning is indented`,
          );
          check(
            warning.cycleTop >= warning.arrivalBottom - 1,
            `${name}: summary cycle warning overlaps arrival/lateness`,
          );
        }
        await page.locator('.dispatch-truck').screenshot({
          path: resolve(output, `${name}-dispatch-compact.png`),
        });
        check(
          normalize(await current.innerText()).includes('Cycle short'),
          `${name}: current cycle shortage missing`,
        );
        check(
          (await current.locator('.stop-hours__departure-cycle').count()) === 0,
          `${name}: duplicate cycle balance shown`,
        );
        check(
          (await current
            .locator('.stop-hours__road .stop-hours__status--success')
            .count()) === 0,
          `${name}: infeasible current road ETA shown green`,
        );
        check(
          summaryReads === 1 && planningReads === 0,
          `${name}: initial Dispatch reads one summary batch ` +
            'without per-truck planning',
        );
        await futureCard.locator('.dispatch-load__details').focus();
        await page.keyboard.press('Enter');
        await future
          .locator('.stop-workspace__stop')
          .first()
          .waitFor({ state: 'attached' });
        await showWorkspacePane(page, 'Stops');
        await future.locator('.stop-workspace__stop').first().waitFor();
        await future.screenshot({
          path: resolve(output, `${name}-dispatch-workspace-initial.png`),
        });
        await showWorkspacePane(page, 'Notes & files');
        await future.locator('.dispatch-activity textarea').waitFor();
        await future
          .getByText('No documents attached.', { exact: true })
          .waitFor();
        const workspaceReads = apiReads;
        const pickupEditor = await openStopDetails(
          page,
          future,
          stopIds[1],
          name,
        );
        const pickup = future.locator(`[data-stop-id="${stopIds[1]}"]`);
        check(
          new URL(page.url()).pathname === `/dispatch/${futureId}` &&
            (await page.locator('dialog[open]').count()) === 0 &&
            (await future.locator('.stop-workspace__stop').count()) === 2,
          `${name}: keyboard Details opens a full page with both future stops`,
        );
        check(
          normalize(await pickup.innerText()).includes('Cycle short'),
          `${name}: compact pickup cycle warning missing`,
        );
        check(
          (await pickupEditor.locator('.stop-hours').count()) === 0,
          `${name}: editing fields must not repeat cycle forecasts`,
        );
        check(
          (await pickup.locator('.stop-hours__road time').count()) === 1,
          `${name}: compact pickup ETA missing`,
        );
        await checkRemovedDisplays(page.locator('body'), `${name}-dispatch`);
        await openStopDetails(page, future, stopIds[2], name);
        const delivery = future.locator(`[data-stop-id="${stopIds[2]}"]`);
        check(
          normalize(await delivery.innerText()).includes('Late by 1h 05m') &&
            normalize(await delivery.innerText()).includes('Cycle unknown'),
          `${name}: unknown cycle hides known lateness`,
        );
        check(
          (await delivery.locator('.stop-hours__alternative').count()) === 0,
          `${name}: unknown cycle invents alternatives`,
        );
        dispatchBefore = await measure(future, `${name}-dispatch`);
        check(
          apiReads === workspaceReads,
          `${name}: selecting stops reuses the workspace ` +
            'without extra API reads',
        );
        const boardSummaryReads = summaryReads;
        await future.getByRole('link', { name: '← Back to Dispatch' }).click();
        await future.waitFor({ state: 'detached' });
        await board.locator('.stop-hours').nth(2).waitFor();
        await page.locator('.driver-duty__cycle-reset').first().waitFor();
        const dispatchRecap = page.locator(
          '.dispatch-truck__equipment .driver-next-recap',
        );
        check(
          (await dispatchRecap.count()) === 1 &&
            normalize(await dispatchRecap.innerText()) ===
              'Next recap Sep 9 +3h 05m',
          `${name}: one current-driver recap remains visible in the truck header with date and hours only`,
        );
        check(
          (await page
            .locator(
              '.dispatch-load .driver-next-recap, .dispatch-load .stop-hours__recap',
            )
            .count()) === 0 &&
            !(await futureCard.innerText()).includes('Next recap'),
          `${name}: load cards repeat the truck recap`,
        );
        const compactHeader = page.locator('.dispatch-planning--compact');
        check(
          summaryReads <= boardSummaryReads + 1 && planningReads === 0,
          `${name}: returning to Dispatch uses at most one summary batch ` +
            'and no per-truck planning',
        );
        check(
          (await compactHeader
            .locator('.dispatch-planning__driver .driver-duty')
            .count()) === 1 &&
            (await compactHeader
              .locator('.driver-hours-panel .driver-duty')
              .count()) === 0,
          `${name}: duty status belongs in the route area, not beneath HOS`,
        );
        check(
          (await compactHeader
            .locator('.driver-hours__clock:not(.is-unavailable)')
            .count()) === 4,
          `${name}: Dispatch retains all four current HOS clocks`,
        );
        dispatchHeaderGeometry = await compactHeader.evaluate(element => {
          const rect = node => {
            const r = node.getBoundingClientRect();
            return {
              left: r.left,
              right: r.right,
              top: r.top,
              bottom: r.bottom,
              height: r.height,
              width: r.width,
            };
          };
          return {
            header: rect(element),
            identity: rect(
              element
                .closest('.dispatch-truck')
                .querySelector('.dispatch-truck__header'),
            ),
            route: rect(element.querySelector('.dispatch-planning__content')),
            driver: rect(element.querySelector('.dispatch-planning__driver')),
            hours: rect(element.querySelector('.driver-hours-panel')),
            clientWidth: element.clientWidth,
            scrollWidth: element.scrollWidth,
            padding:
              parseFloat(getComputedStyle(element).paddingTop) +
              parseFloat(getComputedStyle(element).paddingBottom),
            gap: parseFloat(getComputedStyle(element).columnGap),
          };
        });
        check(
          dispatchHeaderGeometry.scrollWidth <=
            dispatchHeaderGeometry.clientWidth + 1,
          `${name}: compact Dispatch header overflows horizontally`,
        );
        if (width >= 1200) {
          const g = dispatchHeaderGeometry;
          check(
            Math.abs(g.identity.left - g.header.left) <= 1 &&
              g.route.left >= g.identity.right - 1 &&
              g.route.left - g.identity.right <= g.gap + 1 &&
              Math.abs(
                g.hours.left - Math.max(g.route.right, g.driver.right) - g.gap,
              ) <= 1,
            `${name}: Dispatch identity, status/fuel and mileage, and HOS must be left-packed without elastic gaps`,
          );
          check(
            g.driver.top >= Math.max(g.route.bottom, g.identity.bottom) - 1 &&
              Math.abs(g.driver.left - g.identity.left) <= 1 &&
              g.driver.right <= g.hours.left + 1,
            `${name}: duty/rest and Next recap must use the strip below truck identity and route metrics, not a tall right column`,
          );
          check(
            g.header.height <=
              Math.max(
                Math.max(g.route.height, g.identity.height) + g.driver.height,
                g.hours.height,
              ) +
                3,
            `${name}: Dispatch header must fit its two natural content rows without reserved blank height`,
          );
        } else if (width < 551) {
          const g = dispatchHeaderGeometry;
          check(
            g.driver.top >= g.route.bottom - 1 &&
              g.hours.top >= g.driver.bottom - 1,
            `${name}: narrow Dispatch must show route, duty/recap and HOS in separate unclipped rows`,
          );
        }
        await compactHeader.screenshot({
          path: resolve(output, `${name}-dispatch-header.png`),
        });
        check(
          normalize(
            await page.locator('.dispatch-planning__fuel').innerText(),
          ) === 'Fuel 75%',
          `${name}: saved fuel reading is a percentage without maintenance wording`,
        );
        check(
          (await page
            .locator('.dispatch-truck .dispatch-load__stop-times .stop-hours')
            .count()) === 3 &&
            (await page
              .locator(
                '.dispatch-truck .dispatch-load__stop-detail-content .stop-hours',
              )
              .count()) === 0 &&
            (await page
              .locator('.dispatch-truck__equipment .dispatch-planning__details')
              .count()) === 0,
          `${name}: each actual stop has one compact summary without inline detailed forecasts or header duplicates`,
        );
        check(
          (
            await page.locator('.driver-duty__cycle-reset').allTextContents()
          ).some(text => text.includes('27h 52m left to complete 34h reset')),
          `${name}: current-driver reset countdown missing`,
        );
        await futureCard.locator('.dispatch-load__details').click();
        await future
          .locator('.stop-workspace__stop')
          .first()
          .waitFor({ state: 'attached' });
        await showWorkspacePane(page, 'Stops');
        await future.locator('.stop-workspace__stop').first().waitFor();
        await openStopDetails(page, future, stopIds[1], `${name}-pending`);
        check(
          (await future.locator('.stop-hours').count()) === 2,
          `${name}: workspace retains two summaries ` +
            'without a duplicate forecast in the editor',
        );
        await future.screenshot({
          path: resolve(output, `${name}-dispatch.png`),
        });
        await future.getByRole('link', { name: '← Back to Dispatch' }).click();
        await board.locator('.stop-hours').nth(2).waitFor();
        const dispatchAppearance = await forecastAppearance(board);
        const initialTimes = await board
          .locator('.stop-hours time')
          .evaluateAll(nodes => nodes.map(node => node.dateTime));
        pending = true;
        const initialBoardReads = boardReads;
        await page.clock.fastForward(65_000);
        await page.waitForFunction(() =>
          [...document.querySelectorAll('.dispatch-load__reference')].every(
            node => node.textContent.includes('Fixture Customer refreshed'),
          ),
        );
        check(
          boardReads > initialBoardReads,
          `${name}: pending Dispatch response was not polled`,
        );
        assert.deepEqual(
          await board
            .locator('.stop-hours time')
            .evaluateAll(nodes => nodes.map(node => node.dateTime)),
          initialTimes,
          `${name}: pending erased or replaced retained board stop times`,
        );
        assert.deepEqual(
          await forecastAppearance(page.locator('.dispatch-truck')),
          dispatchAppearance,
          `${name}: pending changed current/future ETA text, status, cycle, alternatives or colors`,
        );
        await checkRemovedDisplays(
          page.locator('body'),
          `${name}-dispatch-pending`,
        );
        dispatchPending = await measure(board, `${name}-dispatch-pending`);
        await board.screenshot({
          path: resolve(output, `${name}-dispatch-pending.png`),
        });

        pending = false;
        const dispatchTime = await page.evaluate(() => Date.now());
        timing = {
          calculatedAt: new Date(dispatchTime).toISOString(),
          validUntil: new Date(dispatchTime + 120_000).toISOString(),
        };
        await page.clock.fastForward(65_000);
        await page.waitForFunction(() =>
          [...document.querySelectorAll('.dispatch-load__reference')].every(
            node => !node.textContent.includes('Fixture Customer refreshed'),
          ),
        );
        const dispatchScope = page.locator('.dispatch-truck');
        const dispatchHeldAppearance = await forecastAppearance(dispatchScope);
        await watchQuietReplacement(dispatchScope);
        const heldDispatch = (holdBoard = heldResponse());
        await page.clock.fastForward(65_000);
        await waitForHeld(heldDispatch, `${name}-dispatch-deadline`);
        assert.ok(
          (await page.evaluate(() => Date.now())) >
            Date.parse(timing.validUntil),
          `${name}: Dispatch HTTP did not cross ValidUntil`,
        );
        assert.deepEqual(
          await forecastAppearance(dispatchScope),
          dispatchHeldAppearance,
          `${name}: held Dispatch HTTP crossing ValidUntil changed ETA text, times, status or colors`,
        );
        const dispatchReplacementTime = await page.evaluate(() => Date.now());
        timing = {
          calculatedAt: new Date(dispatchReplacementTime).toISOString(),
          validUntil: new Date(dispatchReplacementTime + 120_000).toISOString(),
          shiftMinutes: 5,
        };
        heldDispatch.release();
        await page.waitForFunction(() =>
          [
            ...document.querySelectorAll('.dispatch-load .stop-hours__road'),
          ].some(node => node.textContent.includes('07:10 PM')),
        );
        dispatchQuiet = await checkQuietReplacement(
          dispatchScope,
          `${name}-dispatch-deadline`,
        );
      }

      pending = false;
      timing = {};
      await page.clock.setSystemTime(new Date(now));
      await page.goto(`${origin}/fleet/map`);
      await page.locator('[data-hours-fixture]').waitFor();
      const mapRect = await page.locator('#fleet-map').evaluate(element => {
        window.hoursFixtureMapElement = element;
        window.hoursFixtureInspectorHost = document.querySelector(
          '.fleet-map-inspector__native',
        );
        const { x, y, width, height } = element.getBoundingClientRect();
        return { x, y, width, height };
      });
      const unselectedHeight = await page
        .locator('.fleet-map-info-reserved')
        .evaluate(element => element.getBoundingClientRect().height);
      if (width >= 1200) {
        const toolbar = await page
          .locator('.fleet-map-toolbar')
          .evaluate(el => {
            const rect = node => node.getBoundingClientRect();
            const heading = rect(document.querySelector('.fleet-map-page h1'));
            const search = rect(el.querySelector('.fleet-map-search'));
            // The layer controls are the search's right-hand neighbour: the
            // date input that used to sit between them left the map with the
            // IFTA switch.
            const layers = rect(el.querySelector('.fleet-map-layer-controls'));
            return {
              headingCenter: heading.top + heading.height / 2,
              searchCenter: search.top + search.height / 2,
              layersCenter: layers.top + layers.height / 2,
              searchWidth: search.width,
              available: layers.left - search.left,
            };
          });
        check(
          ['headingCenter', 'layersCenter'].every(
            key => Math.abs(toolbar[key] - toolbar.searchCenter) <= 1,
          ),
          `${name}: heading, search and layers share one line`,
        );
        check(
          toolbar.searchWidth > 192 &&
            toolbar.available - toolbar.searchWidth <= 9,
          `${name}: search fills the remaining toolbar width`,
        );
        for (const label of ['Fuel Stations', 'Traffic', 'Next loads']) {
          const control = page.getByRole('checkbox', {
            name: label,
            exact: true,
          });
          check(
            (await control.locator('..').getAttribute('title')) === label &&
              (await control.locator('../span').isHidden()),
            `${name}: ${label} keeps an accessible name and tooltip`,
          );
        }
      }
      check(
        unselectedHeight <= (width === 390 ? 48 : 80) &&
          (await page
            .locator('.fleet-map-info-reserved.has-selection')
            .count()) === 0,
        `${name}: an unselected map wastes viewport height on an empty selected-truck reserve`,
      );
      await page.screenshot({
        path: resolve(output, `${name}-unselected-map.png`),
      });
      const previewHeld = (holdPreview = heldResponse());
      const detailsHeld = (holdDetails = heldResponse());
      const planningHeld = (holdPlanning = heldResponse());
      await page.evaluate(id => {
        void window.hoursFixture.selectTruck(id);
      }, truckId);
      await page.waitForFunction(() =>
        Array.isArray(window.hoursFixture?.prices?.data),
      );
      check(
        (await page
          .locator('.fleet-map-page__message[role="alert"]')
          .count()) === 0,
        `${name}: successful daily prices must not show a marker-price error`,
      );
      await waitForHeld(previewHeld, `${name}-cold-preview`);
      await page
        .locator('.fleet-map-route-info[aria-busy="true"]')
        .waitFor({ state: 'attached' });
      const titleControls = () =>
        page
          .locator(
            '.fleet-map-inspector__title, ' +
              '.fleet-map-mobile-summary__remaining, ' +
              '.fleet-map-inspector__close',
          )
          .evaluateAll(elements =>
            elements.map(element => {
              const { x, y, width, height } = element.getBoundingClientRect();
              return { x, y, width, height };
            }),
          );
      const initialTitleControls = await titleControls();
      const checkPhoneTitleControls = async stage => {
        if (width !== 390) return;
        const controls = await titleControls();
        check(
          controls.every((bounds, index) =>
            Object.keys(bounds).every(
              key =>
                Math.abs(bounds[key] - initialTitleControls[index][key]) <= 1,
            ),
          ),
          `${name}-${stage}: phone title, remaining distance and Close ` +
            'retain their slots',
        );
      };
      // A cold card is closed, and what it says closed is the load, the
      // miles left to the next stop and the four clocks. Nothing below is
      // shown until it is asked for.
      const coldClosed = {
        lowerHidden: !(await page.locator('#fleet-map-details').isVisible()),
        load: await page
          .locator('.fleet-map-mobile-summary__remaining')
          .isVisible(),
        distance: await page
          .locator('.fleet-map-mobile-summary__distance')
          .isVisible(),
        hours: await page
          .locator('.fleet-map-inspector__hours .driver-hours-panel')
          .isVisible(),
        noRecap:
          (await page
            .locator('.fleet-map-inspector__hours .driver-next-recap')
            .count()) === 0,
      };
      check(
        Object.values(coldClosed).every(Boolean),
        `${name}: a cold card is closed and still says load, miles left ` +
          `and clocks (${JSON.stringify(coldClosed)})`,
      );
      await expandTruckCard(page, `${name}-cold`);
      const coldPlaceholders = {
        route: await page.locator('#fleet-map-route-details').isVisible(),
        telemetry: await page
          .locator('#fleet-map-telemetry-details')
          .isVisible(),
        location: await page.locator('#fleet-map-truck-location').isVisible(),
      };
      check(
        Object.values(coldPlaceholders).every(Boolean),
        `${name}: opening a cold card shows readings, location and route ` +
          `placeholders (${JSON.stringify(coldPlaceholders)})`,
      );
      if (width === 390) await checkPhoneTitleControls('readings');
      const loadingMapRect = await stableMapRect(
        page,
        mapRect,
        `${name}-first-selection-loading`,
      );
      const loadingGeometry = await truckLoadingGeometry(page);
      await page.locator('.fleet-map-route-info').evaluate(panel => {
        window.hoursFixtureRoutePanel = panel;
        window.hoursFixtureRouteGroups = [...panel.children];
        window.hoursFixtureDelivery = panel.querySelector(
          '.fleet-map-route-info__delivery',
        );
      });
      await page.screenshot({
        path: resolve(output, `${name}-route-loading.png`),
      });
      check(
        await page
          .getByRole('button', { name: 'Route options', exact: true })
          .isDisabled(),
        `${name}: route icon keeps its action slot disabled before load identity is known`,
      );
      previewHeld.release();
      await waitForHeld(detailsHeld, `${name}-load-reference`);
      await page
        .locator('.fleet-map-mobile-summary__remaining[aria-busy="true"]')
        .waitFor();
      const partialGeometry = await truckLoadingGeometry(page);
      await stableMapRect(page, mapRect, `${name}-preview-ready`);
      await waitForHeld(planningHeld, `${name}-live-planning`);
      await page.screenshot({
        path: resolve(output, `${name}-route-preview.png`),
      });
      detailsHeld.release();
      await page.waitForFunction(
        () =>
          document
            .querySelector('.fleet-map-mobile-summary__remaining')
            ?.getAttribute('aria-busy') === 'false',
      );
      const detailsGeometry = await truckLoadingGeometry(page);
      await stableMapRect(page, mapRect, `${name}-load-reference-ready`);
      await page.screenshot({
        path: resolve(output, `${name}-route-reference.png`),
      });
      planningHeld.release();
      await page.waitForFunction(() => {
        const route = document.querySelector('.fleet-map-route-info');
        return (
          route?.getAttribute('aria-busy') === 'false' &&
          !route.classList.contains('is-awaiting-route')
        );
      });
      const selectedMapRect = await stableMapRect(
        page,
        mapRect,
        `${name}-first-selection-ready`,
      );
      await page.waitForFunction(
        () =>
          document
            .querySelector('.fleet-map-mobile-summary__remaining')
            ?.getAttribute('aria-busy') === 'false',
      );
      const readyGeometry = await truckLoadingGeometry(page);
      check(
        (await page
          .locator('.fleet-map-route-info__facility')
          .textContent()) === stops[0].name,
        `${name}: the current stop keeps its facility name beside its address`,
      );
      check(
        await page
          .locator('.fleet-map-route-info')
          .evaluate(
            panel =>
              panel === window.hoursFixtureRoutePanel &&
              [...panel.children].every(
                (group, index) =>
                  group === window.hoursFixtureRouteGroups[index],
              ) &&
              panel.querySelector('.fleet-map-route-info__delivery') ===
                window.hoursFixtureDelivery &&
              window.hoursFixtureDelivery.parentElement.classList.contains(
                'fleet-map-route-info__visit',
              ),
          ),
        `${name}: route loading must populate the retained panel and groups, not replace their layout`,
      );
      for (const [stage, geometry] of Object.entries({
        loading: loadingGeometry,
        partial: partialGeometry,
        details: detailsGeometry,
      })) {
        const route = geometry['.fleet-map-route-info'];
        const readyRoute = readyGeometry['.fleet-map-route-info'];
        const telemetry = geometry['.fleet-map-truck-info'];
        const readyTelemetry = readyGeometry['.fleet-map-truck-info'];
        const telemetryGap = telemetry.y - route.y - route.height;
        const readyTelemetryGap =
          readyTelemetry.y - readyRoute.y - readyRoute.height;
        check(
          telemetryGap >= -1 && Math.abs(telemetryGap - readyTelemetryGap) <= 1,
          `${name}: ${stage} the vehicle line follows the route without ` +
            'changing its gap',
        );
        const bottomInset = snapshot => {
          const panel = snapshot['.fleet-map-route-info'];
          const contentBottom = Math.max(
            ...['distances', 'visit', 'timing'].map(group => {
              const bounds = snapshot[`.fleet-map-route-info__${group}`];
              return bounds.y + bounds.height;
            }),
          );
          return panel.y + panel.height - contentBottom;
        };
        check(
          bottomInset(geometry) >= -1 &&
            Math.abs(bottomInset(geometry) - bottomInset(readyGeometry)) <= 1,
          `${name}: ${stage} route height fits its facts, ` +
            'including the cycle warning, without blank reserved space',
        );
        // The head's top row divides its width between the unit and the
        // forecast, so those two resize as the forecast lands. What may not
        // change is where they sit and how tall they are, because every row
        // below follows them.
        const sharesTheTopRow = [
          '.fleet-map-inspector__identity',
          '.fleet-map-inspector__arrival',
        ];
        for (const selector of Object.keys(geometry)) {
          const withinRoute = selector.startsWith('.fleet-map-route-info');
          const dimensions = withinRoute
            ? selector.endsWith('> .fleet-map-route-info__label')
              ? ['x']
              : ['x', 'width']
            : sharesTheTopRow.includes(selector)
              ? ['y', 'height']
              : ['x', 'y', 'width'];
          check(
            dimensions.every(
              key =>
                Math.abs(
                  geometry[selector][key] - readyGeometry[selector][key],
                ) <= 1,
            ),
            `${name}: ${stage} ${selector} moves while selection data loads`,
          );
          if (withinRoute) {
            check(
              Math.abs(
                geometry[selector].y -
                  route.y -
                  (readyGeometry[selector].y - readyRoute.y),
              ) <= 1,
              `${name}: ${stage} route groups retain their relative ` +
                'positions below readings',
            );
          }
        }
      }
      (report.loadingGeometry ??= []).push({
        name,
        loading: loadingGeometry,
        partial: partialGeometry,
        ready: readyGeometry,
      });
      const repeatSelectionReads = apiReads;
      await page.evaluate(id => window.hoursFixture.selectTruck(id), truckId);
      await stableMapRect(page, mapRect, `${name}-repeat-selection`);
      check(
        apiReads === repeatSelectionReads,
        `${name}: reselecting the same truck does not reread ` +
          'planning or weather',
      );
      check(
        !(await page.locator('#fleet-map-details').isVisible()),
        `${name}: reselecting a truck closes its card again`,
      );
      await expandTruckCard(page, `${name}-reselected`);
      await page.screenshot({
        path: resolve(output, `${name}-selected-info-initial.png`),
      });
      if (width === 390) {
        // The miles left have their own group beside the load; on a phone
        // the head's line stacks, so each takes a row from the same edge.
        const distance = page.locator('.fleet-map-mobile-summary__distance');
        check(
          (await distance.isVisible()) &&
            normalize(await distance.locator('strong').innerText()) ===
              (units.distanceUnit === 'kilometers' ? '193' : '120'),
          `${name}: phone header keeps remaining distance in the primary unit`,
        );
        check(
          await distance.evaluate(element => {
            const own = element.getBoundingClientRect();
            const head = document
              .querySelector('.fleet-map-inspector__hours')
              .getBoundingClientRect();
            const load = document
              .querySelector('.fleet-map-mobile-summary__remaining')
              .getBoundingClientRect();
            return (
              own.left >= head.left - 1 &&
              own.right <= head.right + 1 &&
              Math.abs(own.left - load.left) <= 1 &&
              own.top >= load.bottom - 1
            );
          }),
          `${name}: the miles left take their own row from the load's edge`,
        );
        check(
          (await page.locator('#fleet-map-details').isVisible()) &&
            (await page.locator('#fleet-map-telemetry-details').isVisible()) &&
            (await page.locator('.fleet-map-inspector__title').isVisible()) &&
            (await page.locator('.fleet-map-inspector__driver').isVisible()) &&
            (await page.locator('.fleet-map-inspector__trailer').isVisible()) &&
            (await page.locator('.fleet-map-inspector__actions').isVisible()) &&
            (await page.locator('.fleet-map-inspector__close').isVisible()),
          `${name}: phone inspector keeps crew, actions and readings visible`,
        );
        check(
          (await page.locator('.fleet-map-info-reserved').boundingBox())
            .height <=
            mapRect.height * 0.6 + 1,
          `${name}: phone card stays bounded so the map remains visible`,
        );
        await measureTruckControls(page, `${name}-phone-ready`);
        await stableMapRect(page, mapRect, `${name}-phone-ready`);
        check(
          (await page.locator('#fleet-map-route-details').isVisible()) &&
            (await page.locator('#fleet-map-truck-location').isVisible()) &&
            (await page
              .locator('.fleet-map-inspector__clocks > .driver-hours-panel')
              .isVisible()) &&
            (await page.locator('.fleet-map-truck-info__outside').isVisible()),
          `${name}: phone card exposes readings, HOS and location/load`,
        );
        await checkPhoneTitleControls('summary');
        await page.screenshot({
          path: resolve(output, `${name}-phone-readings.png`),
        });
        await checkMobileTruckScrolling(page, output, name);
        await checkTruckReadingsLayout(page, output, name);
        check(
          (await page.locator('.fleet-map-truck-info__more').count()) === 0,
          `${name}: phone details do not require a secondary disclosure`,
        );
      } else
        check(
          (await page.locator('.fleet-map-truck-info__more').count()) === 0 &&
            (await page
              .locator('.fleet-map-mobile-summary__remaining')
              .isVisible()) &&
            (await page
              .locator('#fleet-map-details .fleet-map-mobile-summary__distance')
              .count()) === 0,
          `${name}: the desktop head carries the load and the miles left ` +
            'once, with no second disclosure',
        );
      check(
        (await page.locator('.fleet-map-inspector__hours').isVisible()) &&
          (await page
            .getByRole('button', { name: 'Fuel plan', exact: true })
            .isVisible()),
        `${name}: always-visible readings retain fuel controls and HOS`,
      );
      check(
        (await page.locator('#fleet-map-route-details').isVisible()) &&
          (await page
            .locator(
              '.fleet-map-inspector__identity .fleet-map-inspector__trailer',
            )
            .isVisible()) &&
          (await page
            .locator('.fleet-map-inspector__hours .driver-duty')
            .count()) === 0 &&
          (await page
            .locator('.fleet-map-inspector__hours .driver-next-recap')
            .count()) === 0 &&
          (await page
            .getByRole('link', { name: 'Route & load details' })
            .isVisible()) &&
          (await page
            .locator('.fleet-map-inspector__hours .driver-hours__clock')
            .count()) === 4,
        `${name}: the card retains trailer, load link and four clocks ` +
          'without a duty line or a recap',
      );
      const compactReadings = page.locator('.fleet-map-truck-info__reading');
      check(
        (await compactReadings.count()) === 3 &&
          (await compactReadings.nth(0).isVisible()) &&
          (await compactReadings.nth(1).isVisible()) &&
          (await compactReadings.nth(2).isVisible()) &&
          normalize(await compactReadings.nth(0).innerText()).includes(
            '45 mph',
          ) &&
          normalize(await compactReadings.nth(2).innerText()).includes(
            'Driving',
          ),
        `${name}: the card shows provider speed, fuel and engine`,
      );
      const readingBounds = await compactReadings.evaluateAll(elements =>
        elements.map(element => {
          const rect = element.getBoundingClientRect();
          return {
            top: rect.top,
            bottom: rect.bottom,
            left: rect.left,
            right: rect.right,
          };
        }),
      );
      const outside = page.locator(
        '.fleet-map-truck-info__telemetry > .fleet-map-truck-info__outside',
      );
      const expectedOutside =
        units.temperatureUnit === 'fahrenheit' ? '72.5 °F' : '22.5 °C';
      check(
        normalize(await outside.innerText()) === `Temp ${expectedOutside}`,
        `${name}: outside temperature follows the saved preference`,
      );
      check(
        (await page.evaluate(() => window.hoursFixture.distanceUnit)) ===
          units.distanceUnit,
        `${name}: map stop and station formatting receives the same preference`,
      );
      const outsideBounds = await outside.boundingBox();
      check(
        (await outside.getAttribute('aria-label')) === 'Outside temperature' &&
          (await outside.locator('svg').count()) === 1,
        `${name}: outside temperature has a named thermometer icon`,
      );
      (report.readingRows ??= []).push({
        name,
        width,
        readings: readingBounds,
        outside: outsideBounds,
      });
      // On a wide card the temperature closes the line of readings. A phone
      // card has no room for a fourth: the three readings fill the row and
      // the temperature folds under them, at the same edge they start from.
      // Both arms of this used to ask for the wide arrangement.
      check(
        width === 390
          ? Math.abs(outsideBounds.x - readingBounds[0].left) <= 1 &&
              outsideBounds.y >= readingBounds[2].bottom - 1
          : outsideBounds.x >= readingBounds[2].right &&
              outsideBounds.y < readingBounds[2].bottom,
        `${name}: temperature belongs to the desktop row or mobile left column`,
      );
      // The readings sit on one baseline, so their boxes share a row
      // without their tops matching to the pixel.
      const sameRow = (one, other) =>
        one.top < other.bottom - 1 && other.top < one.bottom - 1;
      // Three readings in the order they are said: on one line where there
      // is room for them, and otherwise the third folds under the first two.
      const alignedReadings = readingBounds.every(rect =>
        sameRow(rect, readingBounds[0]),
      )
        ? readingBounds[0].right <= readingBounds[1].left + 1 &&
          readingBounds[1].right <= readingBounds[2].left + 1
        : sameRow(readingBounds[0], readingBounds[1]) &&
          readingBounds[2].top >= readingBounds[0].bottom - 1;
      check(
        alignedReadings,
        `${name}: compact telemetry stays aligned without overlapping ` +
          `(${JSON.stringify(readingBounds)})`,
      );
      const compactBounds = await page
        .locator('.fleet-map-info-reserved')
        .boundingBox();
      const compactLayout = await page
        .locator('.fleet-map-info-reserved')
        .evaluate(element => {
          const rect = node => node.getBoundingClientRect();
          const map = rect(document.querySelector('#fleet-map'));
          const route = rect(element.querySelector('.fleet-map-route-info'));
          const actions = rect(
            element.querySelector('.fleet-map-inspector__actions'),
          );
          const style = getComputedStyle(element),
            root = parseFloat(
              getComputedStyle(document.documentElement).fontSize,
            );
          return {
            mapLeft: map.left,
            mapWidth: map.width,
            cap:
              parseFloat(
                style.getPropertyValue('--size-map-compact-inspector'),
              ) * root,
            // The actions read under the detail they act on.
            adjacent: actions.top >= route.bottom - 1,
          };
        });
      check(
        Math.abs(
          compactBounds.width -
            Math.min(compactLayout.mapWidth, compactLayout.cap),
        ) <= 1 &&
          Math.abs(
            compactBounds.x +
              compactBounds.width / 2 -
              compactLayout.mapLeft -
              compactLayout.mapWidth / 2,
          ) <= 1 &&
          compactLayout.adjacent,
        `${name}: the card uses its smaller centred cap and keeps the ` +
          'actions under the detail they act on',
      );
      const gpsLocation = page.locator('[aria-label="Truck GPS location"]');
      check(
        (await gpsLocation.isVisible()) &&
          (await gpsLocation.innerText()).includes(truck.formattedLocation),
        `${name}: compact truck shows its GPS address independently of route stops`,
      );
      check(
        (await gpsLocation.locator('time').count()) === 0,
        `${name}: the location card omits the GPS timestamp row`,
      );
      check(
        compactBounds.width <= 1024 + 1 &&
          compactBounds.height <= mapRect.height + 1,
        `${name}: compact inspector must leave most of the map visible`,
      );
      check(
        (await page.locator('.fleet-map-inspector .driver-duty').count()) ===
          0 &&
          (await page
            .locator('.fleet-map-inspector__hours .driver-hours--text')
            .count()) === 1 &&
          (await page
            .locator('.fleet-map-inspector__hours .driver-hours__dial')
            .count()) === 0,
        `${name}: the head reads four clocks as text, with no duty line and ` +
          'no second copy of the dials',
      );
      const headClocks = () =>
        page
          .locator('.fleet-map-inspector__hours .driver-hours__reading')
          .allTextContents();
      const clocksBefore = await headClocks();
      const primaryGeometry = () =>
        page
          .locator(
            [
              '.fleet-map-inspector__header',
              '.fleet-map-inspector__title',
              '.fleet-map-inspector__close',
              '.fleet-map-inspector__driver',
              '.fleet-map-inspector__trailer',
              '.fleet-map-truck-info__telemetry',
              '.fleet-map-inspector__hours',
              '.fleet-map-inspector__actions',
              '.fleet-map-truck-info__location',
            ].join(', '),
          )
          .evaluateAll(elements =>
            elements.map(element => {
              const { x, y, width, height } = element.getBoundingClientRect();
              return { x, y, width, height };
            }),
          );
      await checkTruckTypography(page, `${name}-ready`, 'ready', units);
      const initialControls = await measureTruckControls(page, `${name}-ready`);
      const initialPrimary = await primaryGeometry();
      const interactionReads = apiReads;
      const follow = page.getByRole('button', { name: 'Follow', exact: true });
      await page.keyboard.press('Tab');
      await follow.focus();
      check(
        await follow.evaluate(element => {
          const style = getComputedStyle(element);
          return (
            element.matches(':focus-visible') &&
            parseFloat(style.outlineWidth) >= 2 &&
            style.outlineStyle !== 'none'
          );
        }),
        `${name}: truck actions retain visible keyboard focus`,
      );
      await page.keyboard.press('Enter');
      await page.waitForFunction(
        () =>
          document
            .querySelector('button[aria-label="Follow"]')
            ?.getAttribute('aria-pressed') === 'true',
      );
      assert.equal(await follow.getAttribute('aria-pressed'), 'true');
      await page.evaluate(() => window.hoursFixture.background());
      await measureTruckControls(page, `${name}-following-background`);
      await stableMapRect(page, mapRect, `${name}-following-background`);
      await checkPhoneTitleControls('following-background');
      await follow.focus();
      await page.keyboard.press('Enter');
      await page.waitForFunction(
        () =>
          document
            .querySelector('button[aria-label="Follow"]')
            ?.getAttribute('aria-pressed') === 'false',
      );
      assert.equal(await follow.getAttribute('aria-pressed'), 'false');
      check(
        apiReads === interactionReads,
        `${name}: Follow and background clicks do not reread retained data`,
      );
      await page.waitForFunction(() => {
        const inspector = document.querySelector(
          '.fleet-map-inspector[data-inspector-mode="truck"]',
        );
        if (!inspector) return false;
        const cap =
          parseFloat(
            getComputedStyle(inspector).getPropertyValue(
              '--size-map-compact-inspector',
            ),
          ) * parseFloat(getComputedStyle(document.documentElement).fontSize);
        const bounds = inspector.getBoundingClientRect(),
          map = document.querySelector('#fleet-map').getBoundingClientRect();
        const insetCap =
          parseFloat(
            getComputedStyle(inspector).getPropertyValue('--space-md'),
          ) * parseFloat(getComputedStyle(document.documentElement).fontSize);
        const gap = Math.max(
          0,
          Math.min(bounds.left - map.left, map.right - bounds.right),
        );
        return (
          Math.abs(bounds.width - Math.min(map.width, cap)) <= 1 &&
          Math.abs(bounds.top - map.top - Math.min(insetCap, gap)) <= 1
        );
      });
      await stableMapRect(page, mapRect, `${name}-retained-selection`);
      check(
        (await page.locator('#fleet-map-route-details').isVisible()) &&
          (
            await page
              .locator('.fleet-map-mobile-summary__remaining')
              .innerText()
          ).includes('CURRENT-1441') &&
          normalize(
            await page
              .locator('.fleet-map-route-info__appointment')
              .innerText(),
          ).includes('Sep 8 · 04:00 PM'),
        `${name}: interactions retain the load/order and appointment`,
      );
      check(
        !(await page.locator('.fleet-map-route-info__distance').isVisible()) &&
          (await page.locator('.fleet-map-route-info__delivery').isVisible()) &&
          !(await page
            .locator('.fleet-map-route-info__next-appointment')
            .isVisible()) &&
          (await page
            .locator('.fleet-map-route-info__metric')
            .first()
            .locator('.fleet-map-route-info__secondary')
            .count()) === (units.distanceUnit === 'both' ? 1 : 0),
        `${name}: final-stop summary follows units ` +
          'without duplicate distance or appointment',
      );
      const retainedPrimary = await primaryGeometry();
      await checkTruckTypography(page, `${name}-retained`, 'retained', units);
      const retainedControls = await measureTruckControls(
        page,
        `${name}-retained`,
      );
      check(
        Math.abs(retainedControls.width - initialControls.width) <= 1,
        `${name}: truck actions never change the Close control width`,
      );
      check(
        retainedPrimary.length === initialPrimary.length &&
          retainedPrimary.every((rect, index) =>
            Object.keys(rect).every(
              key => Math.abs(rect[key] - initialPrimary[index][key]) <= 1,
            ),
          ),
        `${name}: Follow and background clicks never move ` +
          'or resize truck facts',
      );
      const overlayGeometry = await page
        .locator('.fleet-map-info-reserved')
        .evaluate(element => {
          const rect = element.getBoundingClientRect(),
            style = getComputedStyle(element);
          const truck = element.querySelector('.fleet-map-truck-info'),
            route = element.querySelector('.fleet-map-route-info');
          const truckStyle = getComputedStyle(truck),
            routeStyle = getComputedStyle(route);
          const shadowReference = document.createElement('div');
          shadowReference.style.boxShadow =
            style.getPropertyValue('--shadow-card');
          element.append(shadowReference);
          const expectedShadow = getComputedStyle(shadowReference).boxShadow;
          shadowReference.remove();
          return {
            position: style.position,
            height: rect.height,
            top: rect.top,
            left: rect.left,
            right: rect.right,
            width: rect.width,
            widthCap:
              parseFloat(
                style.getPropertyValue('--size-map-compact-inspector'),
              ) *
              parseFloat(getComputedStyle(document.documentElement).fontSize),
            insetCap:
              parseFloat(style.getPropertyValue('--space-md')) *
              parseFloat(getComputedStyle(document.documentElement).fontSize),
            expectedShadow,
            expectedRadius:
              parseFloat(style.getPropertyValue('--radius-sm')) *
              parseFloat(getComputedStyle(document.documentElement).fontSize),
            shadow: style.boxShadow,
            radius: style.borderRadius,
            background: style.backgroundColor,
            // The route reads first and the vehicle's line closes the
            // card, with nothing between them.
            rowGap:
              truck.getBoundingClientRect().top -
              route.getBoundingClientRect().bottom,
            routeFirst: !!(
              route.compareDocumentPosition(truck) &
              Node.DOCUMENT_POSITION_FOLLOWING
            ),
            truckRadius: truckStyle.borderRadius,
            routeRadius: routeStyle.borderRadius,
            divider: routeStyle.borderTopWidth,
            scrollHeight: element.scrollHeight,
            overflowY: style.overflowY,
            parent: element.parentElement.className,
            headingBottom: document
              .querySelector('.fleet-map-page h1')
              .getBoundingClientRect().bottom,
            filtersBottom: document
              .querySelector('.fleet-map-toolbar')
              .getBoundingClientRect().bottom,
          };
        });
      check(
        await page
          .locator(
            '.fleet-map-info-reserved, .fleet-map-info-content, .fleet-map-reveal',
          )
          .evaluateAll(elements =>
            elements.every(
              element => getComputedStyle(element).animationName === 'none',
            ),
          ),
        `${name}: retained inspector must not animate ` +
          'even without reduced motion',
      );
      if (width >= 1440)
        check(
          overlayGeometry.height <= 275,
          `${name}: truck details must remain content-sized and compact`,
        );
      check(
        overlayGeometry.position === 'absolute' &&
          overlayGeometry.parent.includes('fleet-map-stage') &&
          overlayGeometry.height <=
            mapRect.height * (width === 390 ? 0.6 : 0.55) + 2 &&
          overlayGeometry.top >= mapRect.y &&
          overlayGeometry.overflowY === 'auto',
        `${name}: selected information must scroll inside a bounded overlay without covering the full map`,
      );
      check(
        overlayGeometry.top >= mapRect.y &&
          mapRect.y >= overlayGeometry.filtersBottom - 1 &&
          overlayGeometry.top >= overlayGeometry.headingBottom,
        `${name}: the info overlay must stay inside the map, never cover the page heading or filters`,
      );
      const sideClearance = Math.max(
        0,
        Math.min(
          overlayGeometry.left - mapRect.x,
          mapRect.x + mapRect.width - overlayGeometry.right,
        ),
      );
      check(
        Math.abs(
          overlayGeometry.top -
            mapRect.y -
            Math.min(overlayGeometry.insetCap, sideClearance),
        ) <= 1 &&
          Math.abs(
            overlayGeometry.left +
              overlayGeometry.width / 2 -
              mapRect.x -
              mapRect.width / 2,
          ) <= 1 &&
          Math.abs(
            overlayGeometry.width -
              Math.min(mapRect.width, overlayGeometry.widthCap),
          ) <= 1 &&
          overlayGeometry.rowGap >= -1 &&
          overlayGeometry.routeFirst,
        `${name}: the centred card reads the route first and closes with ` +
          `the vehicle's line (gap ${overlayGeometry.rowGap})`,
      );
      check(
        overlayGeometry.expectedShadow !== 'none' &&
          overlayGeometry.shadow === overlayGeometry.expectedShadow &&
          Math.abs(
            parseFloat(overlayGeometry.radius) - overlayGeometry.expectedRadius,
          ) <= 1 &&
          overlayGeometry.truckRadius === '0px' &&
          overlayGeometry.routeRadius === '0px' &&
          overlayGeometry.divider === '1px',
        `${name}: information must use the shared popup surface, radius and shadow without separate row corners`,
      );
      await page.screenshot({
        path: resolve(output, `${name}-selected-info-retained.png`),
      });
      const fuelReading = page
        .locator('.fleet-map-truck-info__reading')
        .filter({ hasText: 'Fuel' });
      check(
        (await fuelReading.locator('.fuel-reading__value').textContent()) ===
          '75%' &&
          !normalize(await fuelReading.textContent()).includes('last reading'),
        `${name}: map fuel reading remains a compact percentage`,
      );
      check(
        (await fuelReading.locator('.fuel-reading--metric').count()) === 1 &&
          (await page.locator('.fleet-map-inspector__driver').isVisible()),
        `${name}: selected map keeps the metric fuel column and visible header driver`,
      );
      const truckHeader = page.locator('.fleet-map-truck-info');
      const headHours = page.locator('.fleet-map-inspector__hours');
      check(
        (await headHours
          .locator('.fleet-map-inspector__hours-label')
          .count()) === 1 &&
          (await headHours.locator('.driver-hours-panel').count()) === 1 &&
          (await truckHeader
            .locator(
              '.fleet-map-truck-info__reading > small > svg[aria-hidden="true"]',
            )
            .count()) === 2,
        `${name}: the clocks read under one HOS label in the head, and the ` +
          'vehicle line keeps its own icons',
      );
      check(
        (await page
          .locator('.fleet-map-inspector .driver-next-recap')
          .count()) === 0,
        `${name}: truck inspector omits duplicate Next recap`,
      );
      check(
        (await headHours
          .locator('.driver-hours__clock:not(.is-unavailable)')
          .count()) === 4,
        `${name}: the selected head has four fresh HOS clocks`,
      );
      const headerGeometry = await truckHeader.evaluate(element => {
        const rect = node => {
          if (!node) return null;
          const bounds = node.getBoundingClientRect();
          return {
            left: bounds.left,
            right: bounds.right,
            top: bounds.top,
            bottom: bounds.bottom,
            width: bounds.width,
            height: bounds.height,
          };
        };
        const dutyNode = element.querySelector('.driver-duty');
        const textBounds = [];
        if (dutyNode) {
          const text = document.createTreeWalker(
            dutyNode,
            NodeFilter.SHOW_TEXT,
          );
          while (text.nextNode())
            if (text.currentNode.textContent.trim()) {
              const range = document.createRange();
              range.selectNodeContents(text.currentNode);
              textBounds.push(...range.getClientRects());
            }
        }
        return {
          header: rect(element),
          hours: rect(element.querySelector('.driver-hours')),
          hoursGroup: rect(
            element.querySelector('.fleet-map-inspector__hours'),
          ),
          dials: [...element.querySelectorAll('.driver-hours__dial')].map(rect),
          hosGap: element.querySelector('.driver-hours')
            ? parseFloat(
                getComputedStyle(element.querySelector('.driver-hours'))
                  .columnGap,
              )
            : null,
          identity: rect(
            document.querySelector('.fleet-map-inspector__driver'),
          ),
          telemetry: rect(
            element.querySelector('.fleet-map-truck-info__telemetry'),
          ),
          location: rect(
            element.querySelector('.fleet-map-truck-info__location'),
          ),
          duty: rect(element.querySelector('.driver-duty')),
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
          dutyGroup: rect(element.querySelector('.fleet-map-truck-info__duty')),
          telemetryIcons: [
            ...element.querySelectorAll(
              '.fleet-map-truck-info__reading > small > svg, .fuel-reading__icon',
            ),
          ]
            .map(rect)
            .filter(icon => icon.width > 0),
          weather: rect(
            element.querySelector('.fleet-map-truck-info__outside svg'),
          ),
          readings: [
            ...element.querySelectorAll('.fleet-map-truck-info__reading'),
          ].map(rect),
          actions: rect(
            document.querySelector('.fleet-map-inspector__actions'),
          ),
          buttons: [
            ...document.querySelectorAll(
              '.fleet-map-inspector__actions .map-action-icon',
            ),
          ].map(rect),
          dutyTextRight: textBounds.length
            ? Math.max(...textBounds.map(bounds => bounds.right))
            : null,
          columnGap: parseFloat(getComputedStyle(element).columnGap),
          paddingRight: parseFloat(getComputedStyle(element).paddingRight),
          rows: [...(dutyNode?.children ?? [])].map(row => ({
            ...rect(row),
            clientWidth: row.clientWidth,
            scrollWidth: row.scrollWidth,
          })),
        };
      });
      check(
        headerGeometry.scrollWidth <= headerGeometry.clientWidth + 1,
        `${name}: selected truck header has horizontal overflow`,
      );
      // The vehicle line is the truck's own readings and where it is. The
      // clocks read in the card's head, as text, and there is no duty line
      // anywhere on the map.
      check(
        headerGeometry.dials.length === 0 && headerGeometry.duty === null,
        `${name}: the vehicle line draws no clocks or duty of its own`,
      );
      check(
        headerGeometry.telemetryIcons.length === 0,
        `${name}: a reading is a word and a value, with no icon`,
      );
      check(
        headerGeometry.weather !== null &&
          Math.abs(
            headerGeometry.weather.width - headerGeometry.weather.height,
          ) <= 1,
        `${name}: the weather keeps the square icon that is its reading`,
      );
      for (const reading of headerGeometry.readings)
        check(
          reading.left >= headerGeometry.header.left - 1 &&
            reading.right <= headerGeometry.header.right + 1,
          `${name}: a reading leaves the vehicle's line`,
        );
      if (width >= 1440) {
        check(
          headerGeometry.identity !== null &&
            headerGeometry.telemetry !== null &&
            headerGeometry.identity.bottom <= headerGeometry.telemetry.top + 1,
          `${name}: the unit names the card above the readings`,
        );
        check(
          headerGeometry.location !== null &&
            headerGeometry.telemetry !== null &&
            headerGeometry.location.left >= headerGeometry.telemetry.right - 1,
          `${name}: the address ends the vehicle's line without an elastic spacer`,
        );
        check(
          headerGeometry.actions !== null &&
            headerGeometry.actions.bottom <= headerGeometry.header.top + 1,
          `${name}: truck actions belong in the header, not a separate body column`,
        );
      }
      for (const button of headerGeometry.buttons) {
        check(
          button.left >= headerGeometry.header.left &&
            button.right <= headerGeometry.header.right,
          `${name}: truck action leaves the selected header`,
        );
        if (width === 390)
          check(
            button.width >= 44 && button.height >= 44,
            `${name}: mobile truck actions retain touch targets`,
          );
      }
      await truckHeader.screenshot({
        path: resolve(output, `${name}-current-status.png`),
      });
      const routeInfo = page.locator(
        '.fleet-map-route-info[aria-label="Current dispatch route"]',
      );
      const adjacentRoute = await routeInfo.evaluate(element => {
        const style = getComputedStyle(element);
        const rootSize = parseFloat(
          getComputedStyle(document.documentElement).fontSize,
        );
        return {
          wide: element.getBoundingClientRect().width >= 52 * rootSize,
          gap: parseFloat(style.columnGap),
          groups: ['visit', 'distances', 'timing'].map(group => {
            const bounds = element
              .querySelector(`:scope > .fleet-map-route-info__${group}`)
              .getBoundingClientRect();
            return { group, x: bounds.x, y: bounds.y, width: bounds.width };
          }),
        };
      });
      // Two columns of equal weight: the stop on the left, and the facts
      // about it stacked on the right in the order they are read.
      if (adjacentRoute.wide) {
        const [visit, distances, timing] = adjacentRoute.groups;
        check(
          Math.abs(distances.y - visit.y) <= 1 &&
            Math.abs(distances.x - visit.x - visit.width - adjacentRoute.gap) <=
              1,
          `${name}: the run's total stands beside the stop, not below it`,
        );
        check(
          Math.abs(timing.x - distances.x) <= 1 && timing.y >= distances.y - 1,
          `${name}: the cycle follows the total in the same column`,
        );
      }
      (report.adjacentRouteGroups ??= []).push({ name, ...adjacentRoute });
      // The stop and the facts are two columns with one hairline between
      // them, drawn on the stop's own inline end. A card too narrow for two
      // columns turns that line under the stop instead.
      const routeDividers = await routeInfo.evaluate(element => {
        const visit = element.querySelector(
          ':scope > .fleet-map-route-info__visit',
        );
        const style = getComputedStyle(visit);
        return {
          columns:
            getComputedStyle(element).gridTemplateColumns.split(' ').length,
          inlineEnd: parseFloat(style.borderInlineEndWidth),
          blockEnd: parseFloat(style.borderBlockEndWidth),
          separators: [
            ...element.querySelectorAll(
              ':scope > .fleet-map-route-info__distances,' +
                ':scope > .fleet-map-route-info__timing',
            ),
          ].map(node => ({
            name: node.className,
            inlineStart: parseFloat(
              getComputedStyle(node).borderInlineStartWidth,
            ),
          })),
        };
      });
      check(
        routeDividers.columns === 2
          ? routeDividers.inlineEnd === 1 && routeDividers.blockEnd === 0
          : routeDividers.blockEnd === 1 && routeDividers.inlineEnd === 0,
        `${name}: one hairline divides the stop from the facts ` +
          `(${JSON.stringify(routeDividers)})`,
      );
      check(
        routeDividers.separators.every(group => group.inlineStart === 0),
        `${name}: the facts are not divided again from their own side`,
      );
      (report.routeDividers ??= []).push({ name, ...routeDividers });
      const routeGroups = await routeInfo.evaluate(element => {
        const bounds = [...element.children]
          .filter(
            child =>
              !child.classList.contains('fleet-map-route-info__messages'),
          )
          .map(child => {
            const r = child.getBoundingClientRect();
            return {
              left: r.left,
              right: r.right,
              top: r.top,
              bottom: r.bottom,
            };
          });
        return {
          bounds,
          gap: parseFloat(getComputedStyle(element).columnGap),
          display: getComputedStyle(element).display,
        };
      });
      if (routeGroups.display === 'flex')
        for (let i = 1; i < routeGroups.bounds.length; i++) {
          const previous = routeGroups.bounds[i - 1],
            group = routeGroups.bounds[i];
          check(
            Math.abs(group.top - previous.top) <= 1
              ? Math.abs(group.left - previous.right - routeGroups.gap) <= 1
              : Math.abs(group.left - routeGroups.bounds[0].left) <= 1,
            `${name}: address, appointment and ETA must follow compact left-packed route groups`,
          );
        }
      assert.deepEqual(
        await routeInfo
          .locator(
            ':scope > .fleet-map-route-info__distances > .fleet-map-route-info__metric .fleet-map-route-info__label',
          )
          .allTextContents(),
        ['Run'],
        `${name}: only the run's total occupies the distance column`,
      );
      // The stop and the window it has to make read together; the run's
      // total and the cycle are the facts beside them. The load and the
      // miles left are said once, in the head.
      check(
        (await routeInfo
          .locator(':scope > .fleet-map-route-info__load')
          .count()) === 0 &&
          (await page
            .locator('.fleet-map-mobile-summary__remaining')
            .count()) === 1 &&
          (await routeInfo
            .locator(
              ':scope > .fleet-map-route-info__visit > ' +
                '.fleet-map-route-info__next + ' +
                '.fleet-map-route-info__delivery',
            )
            .count()) === 1 &&
          (await routeInfo
            .locator(':scope > .fleet-map-route-info__visit > *')
            .count()) === 2 &&
          (await routeInfo
            .locator(':scope > .fleet-map-route-info__timing > *')
            .count()) === 2 &&
          normalize(
            await routeInfo
              .locator(':scope > .fleet-map-route-info__distances')
              .textContent(),
          ).replace(/\s*·\s*/, ' · ') ===
            (units.distanceUnit === 'both'
              ? 'Run 500 mi · 805 km'
              : units.distanceUnit === 'miles'
                ? 'Run 500 mi'
                : 'Run 805 km'),
        `${name}: the stop, its window and the run's total are distinct ` +
          'groups without losing the total distance (' +
          normalize(
            await routeInfo
              .locator(':scope > .fleet-map-route-info__distances')
              .textContent(),
          ) +
          `, visit ${await routeInfo
            .locator(':scope > .fleet-map-route-info__visit > *')
            .count()}, timing ${await routeInfo
            .locator(':scope > .fleet-map-route-info__timing > *')
            .count()})`,
      );
      await checkIndependentRouteGroups(page, name);
      await checkNextStopDistanceLayout(page, name);
      await checkRouteFactRows(page, name, units.distanceUnit);
      const address = routeInfo.locator('.fleet-map-route-info__copy-address');
      check(
        (await address.locator('svg').count()) === 0 &&
          (await address.locator('[title]').getAttribute('title')) ===
            stops[0].address,
        `${name}: the address uses the pin's space and retains its full tooltip`,
      );
      const longStreet = await address
        .locator('.fleet-map-route-info__street')
        .evaluate(element => {
          const original = element.textContent;
          const column = element.closest('.fleet-map-route-info__visit');
          const originalWidth = column.clientWidth;
          element.textContent = (
            '13077 SW Anthony F. Sansone Sr. Boulevard, ' +
            'receiving entrance and secondary security checkpoint, '
          ).repeat(4);
          const style = getComputedStyle(element);
          const result = {
            truncated: element.scrollWidth > element.clientWidth,
            ellipsis: style.textOverflow,
            whiteSpace: style.whiteSpace,
            columnOverflow: column.scrollWidth > column.clientWidth + 1,
            columnWidthChanged: Math.abs(column.clientWidth - originalWidth),
          };
          element.textContent = original;
          return result;
        });
      check(
        longStreet.truncated &&
          longStreet.ellipsis === 'ellipsis' &&
          longStreet.whiteSpace === 'nowrap' &&
          !longStreet.columnOverflow &&
          longStreet.columnWidthChanged <= 1,
        `${name}: a genuinely overflowing street uses ellipsis ` +
          `without stretching its column (${JSON.stringify(longStreet)})`,
      );
      check(
        (await address
          .locator(
            '.fleet-map-route-info__address-lines > .fleet-map-route-info__facility:first-child',
          )
          .textContent()) === stops[0].name &&
          (await address
            .locator(
              '.fleet-map-route-info__address-lines > span:not(.fleet-map-route-info__facility)',
            )
            .textContent()) === '100 Current Street' &&
          (await address
            .locator('strong.fleet-map-route-info__address')
            .textContent()) === 'Toronto, ON, Canada',
        `${name}: current route address places facility above street and locality`,
      );
      check(
        (await routeInfo
          .locator(
            '.fleet-map-route-info__visit-heading > .fleet-map-route-info__label',
          )
          .textContent()) === 'Delivery',
        `${name}: stop kind stays separate from its address`,
      );
      const addressGeometry = await address.evaluate(element => {
        const facilityElement = element.querySelector(
          '.fleet-map-route-info__facility',
        );
        const streetElement = element.querySelector(
          '.fleet-map-route-info__address-lines > span:not(.fleet-map-route-info__facility)',
        );
        const localityElement = element.querySelector(
          'strong.fleet-map-route-info__address',
        );
        const facility = facilityElement.getBoundingClientRect();
        const street = streetElement.getBoundingClientRect();
        const locality = localityElement.getBoundingClientRect();
        return {
          facilityBottom: facility.bottom,
          facilityLeft: facility.left,
          streetTop: street.top,
          streetBottom: street.bottom,
          localityTop: locality.top,
          streetLeft: street.left,
          localityLeft: locality.left,
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
          streetWeight: Number(getComputedStyle(streetElement).fontWeight),
          localityWeight: Number(getComputedStyle(localityElement).fontWeight),
        };
      });
      // The facility names the stop on a row of its own; the street and the
      // city share the row under it, so the stop takes the same four lines
      // as the facts beside it.
      check(
        addressGeometry.streetTop >= addressGeometry.facilityBottom - 1 &&
          Math.abs(addressGeometry.facilityLeft - addressGeometry.streetLeft) <=
            1 &&
          addressGeometry.localityTop < addressGeometry.streetBottom - 1 &&
          addressGeometry.localityLeft >= addressGeometry.streetLeft &&
          addressGeometry.scrollWidth <= addressGeometry.clientWidth + 1,
        `${name}: current route address lines overlap or clip ` +
          `(${JSON.stringify(addressGeometry)})`,
      );
      check(
        addressGeometry.streetWeight < addressGeometry.localityWeight,
        `${name}: current route locality is emphasized instead of the street`,
      );
      await address.click();
      check(
        (await page.evaluate(() => window.hoursFixtureCopiedText)) ===
          stops[0].address,
        `${name}: copying the compact route address preserves its full original value`,
      );
      await routeInfo.screenshot({
        path: resolve(output, `${name}-current-route.png`),
      });
      await page.screenshot({
        path: resolve(output, `${name}-selected-map.png`),
      });
      assert.equal(
        await page.evaluate(() => window.hoursFixture.routeFits),
        0,
        `${name}: selecting a truck never fits the full route`,
      );
      const showRoute = page.getByRole('button', {
        name: 'Show route',
        exact: true,
      });
      assert.equal(await showRoute.isEnabled(), true);
      await showRoute.click();
      assert.equal(
        await page.evaluate(() => window.hoursFixture.shownRoute),
        truckId,
        `${name}: the explicit route action targets the selected truck`,
      );
      const retainedPlan = await page.evaluate(() => window.hoursFixture.plan);
      const backgroundReads = apiReads;
      const cameraState = () =>
        page.evaluate(() => ({
          routeFits: window.hoursFixture.routeFits,
          shownRoute: window.hoursFixture.shownRoute,
          focusCalls: window.hoursFixture.focusCalls ?? 0,
        }));
      const beforeBackgroundCamera = await cameraState();
      await page.locator('.fleet-map-inspector').evaluate(element => {
        element.scrollTop = 0;
        window.hoursFixtureRetainedTruck = element.querySelector(
          '.fleet-map-truck-info',
        );
      });
      const beforeBackgroundFacts = await primaryGeometry();
      for (let click = 0; click < 2; click++) {
        await page.evaluate(() => window.hoursFixture.background());
        assert.equal(
          await page.locator('.fleet-map-inspector').isVisible(),
          true,
        );
        assert.equal(
          await page.locator('#fleet-map-telemetry-details').isVisible(),
          true,
          `${name}: background clicks retain visible readings and HOS`,
        );
        assert.equal(
          await page.locator('#fleet-map-route-details').isVisible(),
          true,
          `${name}: background retains all route facts on phone and desktop`,
        );
        assert.deepEqual(
          await page.evaluate(() => window.hoursFixture.plan),
          retainedPlan,
          `${name}: repeated background clicks retain the road`,
        );
        assert.deepEqual(
          await cameraState(),
          beforeBackgroundCamera,
          `${name}: background clicks do not issue route-fit or camera actions`,
        );
        assert.equal(
          apiReads,
          backgroundReads,
          `${name}: background clicks do not reread truck data`,
        );
        assert.equal(
          await page
            .locator('.fleet-map-truck-info')
            .evaluate(element => element === window.hoursFixtureRetainedTruck),
          true,
          `${name}: background retains the mounted truck information`,
        );
        const afterBackgroundFacts = await primaryGeometry();
        check(
          afterBackgroundFacts.length === beforeBackgroundFacts.length &&
            afterBackgroundFacts.every((bounds, index) =>
              Object.keys(bounds).every(
                key =>
                  Math.abs(bounds[key] - beforeBackgroundFacts[index][key]) <=
                  1,
              ),
            ),
          `${name}: repeated background clicks do not shift truck content`,
        );
        await stableMapRect(page, mapRect, `${name}-background-${click}`);
      }
      await checkHeaderLoadingSpace(page, name);
      await checkInspectorLargeText(page, name);
      await stableMapRect(page, mapRect, `${name}-restored-text-size`);
      const mapKey = page.getByRole('complementary', { name: 'Map key' });
      check(
        normalize(await mapKey.textContent()).includes('Current route') &&
          normalize(await mapKey.textContent()).includes('Pickup / delivery') &&
          (await mapKey.locator('.fleet-map-key__line--next').count()) === 0 &&
          (await mapKey.locator('.fleet-map-key__fuel').count()) === 0,
        `${name}: map key distinguishes current stops and does not advertise disabled layers`,
      );
      check(
        (await mapKey.evaluate(
          element => getComputedStyle(element).position,
        )) === 'absolute',
        `${name}: map key must not change the map's available height`,
      );
      await page.waitForFunction(
        () => window.hoursFixture?.plan?.fuelPlan?.stops?.length === 1,
      );
      await checkRemovedDisplays(page.locator('body'), `${name}-map`);
      const fuelBefore = await savedFuelPayload(page);
      const fuelVisit = fuelBefore.stops[0];
      check(
        fuelVisit.name === 'Kingston travel stop' &&
          fuelVisit.dispatchId === futureId &&
          fuelVisit.buyGallons === 35,
        `${name}: future-trip station or server purchase quantity missing from map metadata`,
      );
      check(
        fuelVisit.milesAhead === 100 &&
          fuelVisit.arrivalGallons === 45 &&
          fuelVisit.departureGallons === 80,
        `${name}: map metadata changed server fuel distance or gallons using current-route progress`,
      );
      check(
        fuelBefore.scheduleImpact.calculatedAt === '2026-09-08T10:15:00Z' &&
          fuelBefore.scheduleImpact.addedLateMinutes === 30 &&
          fuelBefore.scheduleImpact.cycleShort === true,
        `${name}: historical fuel schedule metadata was lost`,
      );
      if (width === 390)
        await page
          .getByRole('button', { name: 'Filters', exact: true })
          .click();
      await page
        .getByRole('checkbox', { name: 'Next loads', exact: true })
        .locator('..')
        .click();
      assert.equal(
        await page
          .getByRole('checkbox', { name: 'Next loads', exact: true })
          .isChecked(),
        true,
      );
      await page.waitForFunction(
        () => window.hoursFixture?.next?.routes?.length === 1,
      );
      check(
        normalize(await mapKey.textContent()).includes(
          'Next loads · color by load',
        ),
        `${name}: future route key explains per-load colors`,
      );
      if (width === 390)
        await page
          .getByRole('button', { name: 'Filters', exact: true })
          .click();
      const card = page.getByRole('region', {
        name: 'Selected next load',
        exact: true,
      });
      await page.evaluate(() => window.hoursFixture.selectStop(0));
      await card.locator('.stop-hours').waitFor();
      // Closing belongs to every card: Back goes somewhere, Close leaves
      // the map clear, so a stop inspector offers both.
      assert.equal(
        await page.locator('.fleet-map-inspector__close').isVisible(),
        true,
        `${name}: a stop inspector keeps its own Close`,
      );
      assert.equal(
        await page.locator('.fleet-map-inspector__back').isVisible(),
        true,
        `${name}: Back to truck joins Close in a selected-truck stop inspector`,
      );
      const nextDisclosure = page.locator(
        'button[aria-controls="fleet-map-next-load-details"]',
      );
      await nextDisclosure.click();
      check(
        (await card
          .locator('xpath=ancestor::*[contains(@class,"fleet-map-inspector")]')
          .count()) === 1 &&
          (await page.locator('.fleet-map-details-card').count()) === 0,
        `${name}: future details must replace the shared inspector content, not open a lower popup`,
      );
      check(
        (await page
          .locator('#fleet-map-details')
          .evaluate(element => getComputedStyle(element).display)) === 'none',
        `${name}: selected future details must hide, not duplicate, the retained truck and route information`,
      );
      await stableMapRect(page, mapRect, `${name}-future-inspection`);
      check(
        normalize(await card.innerText()).includes('Cycle short'),
        `${name}: selected pickup shortage missing`,
      );
      check(
        normalize(await card.locator('.stop-hours__recap').innerText()) ===
          'Next recap Sep 9 +3h 05m',
        `${name}: selected pickup baseline recap shows only date and amount`,
      );
      check(
        (await card.locator('[data-kind="recap"]').count()) === 1 &&
          (await card
            .locator('[data-kind="recap"] .stop-hours__label')
            .textContent()) === 'With recap',
        `${name}: selected pickup recap alternative missing`,
      );
      await checkRemovedDisplays(
        page.locator('body'),
        `${name}-selected-pickup`,
      );
      const selected = await measure(card, `${name}-selected-pickup`);
      await checkCycleAlignment(card, `${name}-selected-pickup`);
      await card.screenshot({
        path: resolve(output, `${name}-selected-pickup.png`),
      });
      await page.screenshot({
        path: resolve(output, `${name}-selected-inspector.png`),
      });
      await page.evaluate(() => window.hoursFixture.selectStop(1));
      await nextDisclosure.click();
      await page.waitForFunction(() =>
        document
          .querySelector('.fleet-map-next-load-card .stop-hours')
          ?.textContent.includes('Cycle unknown'),
      );
      check(
        normalize(await card.innerText()).includes('Late by 1h 05m'),
        `${name}: selected delivery known lateness missing`,
      );
      const selectedUnknown = await measure(card, `${name}-selected-delivery`);
      await checkCycleAlignment(card, `${name}-selected-delivery`);
      const selectedAppearance = await forecastAppearance(card);
      await card.screenshot({
        path: resolve(output, `${name}-selected-delivery.png`),
      });
      const selectedTimes = await card
        .locator('.stop-hours time')
        .evaluateAll(nodes => nodes.map(node => node.dateTime));
      pending = true;
      const initialPlanningReads = planningReads;
      await page.clock.fastForward(65_000);
      await page.waitForFunction(
        () =>
          window.hoursFixture?.etas?.eta?.routeUpdatePending === true &&
          window.hoursFixture?.etas?.refreshing === false,
      );
      check(
        planningReads > initialPlanningReads,
        `${name}: pending planning response was not polled`,
      );
      // The clocks read in the truck card's head, and a selected next-load
      // stop takes that head over, so they are compared only when the truck
      // still owns it.
      if (
        (await page
          .locator('.fleet-map-inspector[data-inspector-mode="truck"]')
          .count()) === 1
      )
        assert.deepEqual(
          await headClocks(),
          clocksBefore,
          `${name}: pending refresh changed the head's HOS clocks`,
        );
      assert.deepEqual(
        await card
          .locator('.stop-hours time')
          .evaluateAll(nodes => nodes.map(node => node.dateTime)),
        selectedTimes,
        `${name}: pending selected stop lost its ETA/recap`,
      );
      assert.deepEqual(
        await forecastAppearance(card),
        selectedAppearance,
        `${name}: pending changed selected-stop ETA text, status, cycle, recap or colors`,
      );
      const selectedPending = await measure(card, `${name}-selected-pending`);
      const pendingMapRect = await stableMapRect(
        page,
        mapRect,
        `${name}-pending-forecast`,
      );
      await card.screenshot({
        path: resolve(output, `${name}-selected-pending.png`),
      });
      await checkRemovedDisplays(page.locator('body'), `${name}-map-pending`);
      const fuelPending = await savedFuelPayload(page);
      assert.deepEqual(
        fuelPending,
        fuelBefore,
        `${name}: pending ETA refresh changed saved fuel metadata`,
      );

      pending = false;
      const mapTime = await page.evaluate(() => Date.now());
      timing = {
        calculatedAt: new Date(mapTime).toISOString(),
        validUntil: new Date(mapTime + 120_000).toISOString(),
      };
      await page.clock.fastForward(15_000);
      await page.waitForFunction(
        calculatedAt =>
          Date.parse(window.hoursFixture?.etas?.eta?.calculatedAt) ===
            Date.parse(calculatedAt) &&
          window.hoursFixture?.etas?.refreshing === false,
        timing.calculatedAt,
      );
      const mapScope = page.locator('.fleet-map-page');
      const mapHeldAppearance = await forecastAppearance(mapScope);
      await watchQuietReplacement(mapScope);
      const heldMap = (holdPlanning = heldResponse());
      await page.clock.fastForward(15_000);
      await waitForHeld(heldMap, `${name}-map-deadline`);
      await page.clock.fastForward(100_000);
      assert.ok(
        (await page.evaluate(() => Date.now())) > Date.parse(timing.validUntil),
        `${name}: Fleet HTTP did not cross ValidUntil`,
      );
      assert.deepEqual(
        await forecastAppearance(mapScope),
        mapHeldAppearance,
        `${name}: held Fleet HTTP crossing ValidUntil changed current/future ETA text, times, status or colors`,
      );
      assert.deepEqual(
        await savedFuelPayload(page),
        fuelBefore,
        `${name}: held ETA request altered the saved fuel metadata`,
      );
      await checkRemovedDisplays(page.locator('body'), `${name}-map-held`);
      const mapReplacementTime = await page.evaluate(() => Date.now());
      timing = {
        calculatedAt: new Date(mapReplacementTime).toISOString(),
        validUntil: new Date(mapReplacementTime + 120_000).toISOString(),
        shiftMinutes: 5,
      };
      heldMap.release();
      await page.waitForFunction(
        calculatedAt =>
          Date.parse(window.hoursFixture?.etas?.eta?.calculatedAt) ===
            Date.parse(calculatedAt) &&
          window.hoursFixture?.etas?.refreshing === false,
        timing.calculatedAt,
      );
      await page.waitForFunction(() =>
        document
          .querySelector('.fleet-map-next-load-card .stop-hours__road')
          ?.textContent.includes('07:10 PM'),
      );
      const mapQuiet = await checkQuietReplacement(
        mapScope,
        `${name}-map-deadline`,
      );
      assert.deepEqual(
        await savedFuelPayload(page),
        fuelBefore,
        `${name}: complete ETA replacement altered the saved fuel metadata`,
      );
      await checkRemovedDisplays(page.locator('body'), `${name}-map-replaced`);
      const replacedMapRect = await stableMapRect(
        page,
        mapRect,
        `${name}-replaced-forecast`,
      );
      const returnTruckReads = apiReads;
      await page
        .getByRole('button', { name: 'Back to truck', exact: true })
        .click();
      await measureTruckControls(page, `${name}-back-from-next-load`);
      assert.equal(
        apiReads,
        returnTruckReads,
        `${name}: Back to truck restores retained facts without API reads`,
      );
      await stableMapRect(page, mapRect, `${name}-back-from-next-load`);
      await page
        .getByRole('button', { name: 'Close map information', exact: true })
        .click();
      await page
        .locator('.fleet-map-info-reserved.has-selection')
        .waitFor({ state: 'detached' });
      const clearedMapRect = await stableMapRect(
        page,
        mapRect,
        `${name}-cleared-selection`,
      );
      report.cases.push({
        name,
        dispatchBefore,
        dispatchPending,
        selected,
        selectedUnknown,
        selectedPending,
        fuelBefore,
        fuelPending,
        headerGeometry,
        dispatchHeaderGeometry,
        addressGeometry,
        dispatchQuiet,
        mapQuiet,
        boardReads,
        planningReads,
        summaryReads,
        apiReads,
        mapRects: {
          unselected: mapRect,
          loading: loadingMapRect,
          selected: selectedMapRect,
          pending: pendingMapRect,
          replaced: replacedMapRect,
          cleared: clearedMapRect,
        },
        overlayGeometry,
      });
      if (lifecycle) {
        const cdp = await context.newCDPSession(page);
        const samples = [];
        // Timings are measured on the host clock, which page.clock does not
        // move. They are the staged Client's own cost with instant fixture
        // replies: the first cycle is the cold one, the rest are repeats.
        // They say nothing about server or provider time.
        for (let cycle = 0; cycle < 16; cycle++) {
          const openedDispatch = performance.now();
          await page
            .getByRole('link', { name: 'Dispatch', exact: true })
            .click();
          await page.locator('#dispatch-search').waitFor();
          await page.waitForFunction(() => !window.hoursFixture);
          const dispatchMs = performance.now() - openedDispatch;
          const routeReads = planningReads;
          await page.clock.fastForward(30_000);
          await page.locator('.dispatch-load').first().waitFor();
          assert.equal(
            planningReads,
            routeReads,
            'Disposed map must stop its route polling',
          );
          await cdp.send('HeapProfiler.collectGarbage');
          const heap = await cdp.send('Runtime.getHeapUsage');
          const dom = await cdp.send('Memory.getDOMCounters');
          const openedMap = performance.now();
          await page
            .getByRole('link', { name: 'Fleet Map', exact: true })
            .click();
          await page.locator('[data-hours-fixture]').waitFor();
          const mapMs = performance.now() - openedMap;
          const selectedTruck = performance.now();
          await page.evaluate(
            id => window.hoursFixture.selectTruck(id),
            truckId,
          );
          await page
            .locator('.fleet-map-inspector__hours .driver-hours')
            .waitFor();
          samples.push({
            cycle,
            jsHeapBytes: heap.usedSize,
            backingStorageBytes: heap.backingStorageSize,
            documents: dom.documents,
            nodes: dom.nodes,
            listeners: dom.jsEventListeners,
            dispatchMs: Math.round(dispatchMs),
            mapMs: Math.round(mapMs),
            selectionMs: Math.round(performance.now() - selectedTruck),
            apiReads,
          });
        }
        report.lifecycle = {
          scope:
            '16 SPA navigation cycles; synthetic API/map; JS heap after GC, not managed .NET or GPU retention; host-clock timings are Client render cost only',
          samples,
        };
        assert.ok(
          samples.at(-1).documents <= samples[4].documents + 1,
          'Settled document count must remain bounded',
        );
        assert.ok(
          samples.at(-1).listeners <= samples[4].listeners + 10,
          'Settled listener count must remain bounded',
        );
        await cdp.detach();
      }
      await context.close();
      await browser.close();
      browser = null;
    }
} catch (error) {
  report.failures.push(error.stack ?? String(error));
} finally {
  await browser?.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
}
console.log(
  JSON.stringify(
    {
      cases: report.cases.length,
      failures: report.failures,
      browserErrors: report.browserErrors,
      unexpectedRequests: report.unexpectedRequests,
      output,
    },
    null,
    2,
  ),
);
if (
  report.cases.length !== widths.length * themes.length ||
  report.failures.length ||
  report.browserErrors.length ||
  report.unexpectedRequests.length
)
  process.exitCode = 1;
