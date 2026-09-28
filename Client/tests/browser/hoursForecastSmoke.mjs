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
      // The server's work phase decides which load is current.
      workPhase: 'current',
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
      workPhase: 'next',
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
import {watchInspectorScroll} from './Scripts/fleetMap/ui/inspectorScroll.ts';
export async function createFleetMap(element, _key, callbacks) {
  element.dataset.hoursFixture = 'offline-map-callbacks';
  element.style.background = 'var(--ui-surface-muted)';
  const viewport = createCameraViewport(element, {});
  const stopScrollWatch = watchInspectorScroll(element.parentElement);
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
    finishInitialView(){},setFuelEditorTruck(){},setRouteEditor(){},
    openStation(){},closeStationPopup(){},focusFuelStation(){},
    clearFuelStationFocus(){},selectNextStop(){},fitNextLoad(){},
    setStations(){},setStopCompletions(){},
    focusRouteStop(id){fixture.routeStopFocus = id;},
    openRouteStop(){},centerStop(){},
    dispose(){viewport.dispose();stopScrollWatch();delete window.hoursFixture;}};
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
    const value = row.querySelector('.stop-hours__value');
    const time = row.querySelector('time').getBoundingClientRect();
    const lateRect = late?.getBoundingClientRect();
    return {
      valueLeft: value.getBoundingClientRect().left,
      valueWidth: value.getBoundingClientRect().width,
      columnGap: parseFloat(getComputedStyle(value).columnGap) || 0,
      cycleLeft: cycle.left,
      cycleTop: cycle.top,
      arrivalBottom: arrival.bottom,
      lateTop: lateRect?.top,
      lateLeft: lateRect?.left,
      lateWidth: lateRect?.width,
      timeTop: time.top,
      timeRight: time.right,
      timeBottom: time.bottom,
      timeWidth: time.width,
    };
  });
  // The card sets every fact on one label column, so a word about the
  // cycle belongs in the value column under the ETA, like the lateness.
  check(
    Math.abs(alignment.valueLeft - alignment.cycleLeft) <= 1,
    `${name}: cycle warning left the ETA value column`,
  );
  check(
    alignment.cycleTop >= alignment.arrivalBottom - 1,
    `${name}: cycle warning overlaps the ETA row`,
  );
  // Known lateness is a badge beside the hour where the line has room,
  // and folds directly under the hour, in the value column, only when the
  // line is full (the approved facts and compact readings).
  if (alignment.lateTop !== undefined) {
    const fits =
      alignment.timeWidth + alignment.columnGap + alignment.lateWidth <=
      alignment.valueWidth + 1;
    const beside =
      Math.abs(alignment.lateTop - alignment.timeTop) <= 2 &&
      alignment.lateLeft >= alignment.timeRight - 1;
    const under =
      alignment.lateTop >= alignment.timeBottom - 1 &&
      Math.abs(alignment.lateLeft - alignment.valueLeft) <= 1;
    check(
      fits ? beside : under,
      `${name}: known lateness sits beside the ETA when it fits, and under ` +
        `it otherwise (${JSON.stringify(alignment)})`,
    );
  }
}
// The known lateness or cycle word of a compact forecast stands beside the
// hour where the value column has room for it, and folds directly under the
// hour, at the value column's edge, only where it has not; either way it
// never overlaps the hour.
async function checkBadgeBesideHour(scope, name) {
  const badges = await scope.locator('.stop-hours__road').evaluateAll(rows =>
    rows.flatMap(row => {
      const value = row.querySelector('.stop-hours__value');
      const time = row.querySelector('time').getBoundingClientRect();
      const box = value.getBoundingClientRect();
      const gap = parseFloat(getComputedStyle(value).columnGap) || 0;
      return [...row.querySelectorAll('.stop-hours__status')].map(node => {
        const badge = node.getBoundingClientRect();
        return {
          text: node.textContent.trim(),
          fits: time.width + gap + badge.width <= box.width + 1,
          beside:
            Math.abs(badge.top - time.top) <= 3 && badge.left >= time.right - 1,
          under:
            badge.top >= time.bottom - 1 &&
            Math.abs(badge.left - box.left) <= 1,
          inside: badge.left >= box.left - 1 && badge.right <= box.right + 1,
        };
      });
    }),
  );
  for (const badge of badges)
    check(
      badge.inside && (badge.fits ? badge.beside : badge.under || badge.beside),
      `${name}: "${badge.text}" stands beside the hour when it fits and ` +
        `under it otherwise (${JSON.stringify(badge)})`,
    );
  return badges;
}

// The truck panel (the owner, September 27): the unit and its controls in
// the head, the next stop with the route's one forecast, eight facts and
// the driver's clocks. The old card's rows, route groups and actions are
// gone; the actions are the map tool bar's.
const panelSelector = '.fleet-map-inspector[data-inspector-mode="truck"]';
const factNames = [
  'Driver',
  'Trailer',
  'Motion',
  'Duty',
  'Fuel',
  'Engine',
  'Temperature',
  'Location',
];

// A phone's panel (narrower than map-compact-columns, 40rem) opens closed
// on the next stop; Details opens the facts and the clocks. A wider panel
// shows them whole.
async function isPhonePanel(page) {
  return page
    .locator('.fleet-map-info-reserved')
    .evaluate(
      element =>
        innerWidth < 1100 &&
        element.clientWidth <
          40 * parseFloat(getComputedStyle(document.documentElement).fontSize),
    );
}

async function expandTruckCard(page, name) {
  // The panel is open whole at every width: the phone's Details / Hide
  // details left it (owner decision of 2026-09-28).
  check(
    (await page
      .locator(`${panelSelector} .fleet-map-mobile-summary__toggle`)
      .count()) === 0 &&
      (await page.locator('.fleet-truck-facts').isVisible()) &&
      (await page.locator('.fleet-truck-clocks').isVisible()),
    `${name}: the panel shows its facts and clocks, with no disclosure`,
  );
}

async function measureTruckControls(page, name) {
  const panel = page.locator(panelSelector);
  // No disclosure at any width (owner decision of 2026-09-28).
  check(
    (await panel.locator('.fleet-map-mobile-summary__toggle').count()) === 0 &&
      (await panel
        .getByRole('button', { name: /^(Details|Hide details)$/ })
        .count()) === 0,
    `${name}: the panel has no Details / Hide details`,
  );
  for (const selector of [
    '.fleet-map-inspector__title',
    '.fleet-truck-facts',
    '.fleet-truck-clocks',
  ])
    check(
      await panel.locator(selector).isVisible(),
      `${name}: ${selector} is shown`,
    );
  const result = await page
    .getByRole('button', { name: 'Close map information', exact: true })
    .evaluate(element => {
      const style = getComputedStyle(element),
        bounds = element.getBoundingClientRect();
      const root = parseFloat(
        getComputedStyle(document.documentElement).fontSize,
      );
      const header = element.closest('.fleet-map-inspector__header');
      const title = header.querySelector('.fleet-map-inspector__title');
      const own = title.getBoundingClientRect();
      // The unit is read whole: whatever is on top at points across its
      // text - a control of the head, the map's tool bar - is the unit.
      const text = document.createRange();
      text.selectNodeContents(title);
      const words = text.getBoundingClientRect();
      const covering = [0.1, 0.5, 0.9]
        .map(share => {
          const x = words.left + words.width * share,
            y = words.top + words.height / 2;
          const top = document.elementFromPoint(x, y);
          return top && !title.contains(top)
            ? { x, y, by: top.className?.baseVal ?? top.className }
            : null;
        })
        .filter(Boolean);
      return {
        width: bounds.width,
        height: bounds.height,
        minimumHeight: ((innerWidth < 800 ? 44 : 32) * root) / 16,
        flexGrow: Number(style.flexGrow),
        title: title.textContent.trim(),
        titleVisible: own.width > 0,
        titleClipped: title.scrollWidth > title.clientWidth + 1,
        covering,
      };
    });
  check(
    result.titleVisible &&
      result.flexGrow === 0 &&
      Math.abs(result.width - result.height) <= 1,
    `${name}: Close stays square beside the visible truck identity`,
  );
  check(
    Math.abs(result.height - result.minimumHeight) <= 1,
    `${name}: Close matches compact desktop and touch mobile action heights`,
  );
  check(
    result.covering.length === 0 && !result.titleClipped,
    `${name}: the unit ${result.title} is read whole, under nothing ` +
      JSON.stringify({
        covering: result.covering,
        clipped: result.titleClipped,
      }),
  );
  return result;
}

// The panel's parts arrive with a short one-shot motion (hud-arrive: a
// 3px rise and a fade, staggered; the owner, September 28). A box read
// mid-arrival is off by part of that rise, so geometry is read once every
// finite animation in the inspector has finished. The glass's endless
// sweep is not waited for. Bounded: after 3s the read goes ahead and any
// movement still shows as a failure.
async function settleArrivals(page) {
  await page.evaluate(
    () =>
      new Promise(done => {
        const started = performance.now();
        const wait = () => {
          const moving = document
            .getAnimations()
            .filter(
              animation =>
                animation.playState === 'running' &&
                animation.effect?.getComputedTiming().iterations !== Infinity &&
                animation.effect?.target?.closest?.('.fleet-map-inspector'),
            );
          if (!moving.length || performance.now() - started > 3000) done();
          else
            Promise.race([
              Promise.all(moving.map(animation => animation.finished)),
              new Promise(resume => setTimeout(resume, 3000)),
            ]).then(wait, wait);
        };
        wait();
      }),
  );
}

// Every animation that starts inside the inspector, counted from before
// the first selection: an entrance replayed on retained content shows as
// a start, even one that finishes before anyone looks.
async function countAnimationStarts(page) {
  await page.evaluate(() => {
    window.hoursFixtureStarts = [];
    document.addEventListener(
      'animationstart',
      event => {
        const target = event.target;
        if (target.closest?.('.fleet-map-inspector'))
          window.hoursFixtureStarts.push({
            at: performance.now(),
            name: event.animationName,
            target: String(target.className?.baseVal ?? target.className),
            label: target.localName === 'dt',
            panel: !!target.closest('.fleet-truck-panel'),
          });
      },
      true,
    );
  });
}

// The first arrival is the positive control: the facts, the clocks and
// the labels did arrive. After the mark (before Follow, background clicks
// and the panel's own controls), nothing in the retained panel may start
// again (owner decision of 2026-09-28 on the HUD arrival; release session).
async function checkNoReplayedArrival(page, name, mark) {
  const starts = await page.evaluate(() => window.hoursFixtureStarts);
  const panel = starts.filter(start => start.panel);
  const first = panel.filter(start => start.at < mark);
  const control = {
    facts: first.filter(
      start =>
        start.name === 'hud-arrive' &&
        start.target.includes('fleet-truck-facts__fact'),
    ).length,
    clocks: first.filter(
      start =>
        start.name === 'hud-arrive' &&
        start.target.includes('fleet-truck-clocks'),
    ).length,
    labels: first.filter(start => start.name === 'hud-type' && start.label)
      .length,
  };
  check(
    control.facts >= 8 && control.clocks >= 1 && control.labels >= 8,
    `${name}: the panel's first arrival is seen (facts, clocks, labels) ` +
      JSON.stringify(control),
  );
  const replayed = panel.filter(start => start.at >= mark);
  check(
    replayed.length === 0,
    `${name}: the retained panel starts no animation after the ` +
      `interactions (${replayed.length}: ${JSON.stringify(
        replayed.slice(0, 6).map(start => [start.name, start.target]),
      )})`,
  );
  (report.animationStarts ??= []).push({ name, control, replayed });
}

// Where each part of the panel stands, against the panel's own scrolled
// content, and what the next stop has received so far.
async function truckLoadingGeometry(page) {
  await settleArrivals(page);
  return page.locator(panelSelector).evaluate(host => {
    const rect = node => {
      if (!node || node.getClientRects().length === 0) return null;
      const { x, y, width, height } = node.getBoundingClientRect();
      return { x, y: y + host.scrollTop, width, height };
    };
    const next = host.querySelector('.fleet-truck-next');
    const nextStyle = next && getComputedStyle(next);
    return {
      header: rect(host.querySelector('.fleet-map-inspector__header')),
      title: rect(host.querySelector('.fleet-map-inspector__title')),
      next: rect(next),
      // The most the next stop can move what is under it as it arrives:
      // its box and both its margins.
      nextFlow: next
        ? next.getBoundingClientRect().height +
          parseFloat(nextStyle.marginTop) +
          parseFloat(nextStyle.marginBottom)
        : 0,
      nextHead: rect(host.querySelector('.fleet-truck-next__head')),
      // Content of the next stop that may arrive with the data.
      nextParts: next
        ? {
            roads: next.querySelectorAll('.stop-hours__road').length,
            statuses: next.querySelectorAll('.stop-hours__status').length,
            bookings: next.querySelectorAll('.fleet-truck-next__booking')
              .length,
            place: next.querySelector('.fleet-truck-next__place')?.children
              .length,
            text: next.textContent.replace(/\s+/g, ' ').trim(),
          }
        : null,
      facts: rect(host.querySelector('.fleet-truck-facts')),
      cells: [...host.querySelectorAll('.fleet-truck-facts__fact')].map(rect),
      clocks: rect(host.querySelector('.fleet-truck-clocks')),
    };
  });
}

// Nothing on the panel moves while the selection's data loads, except by
// content arriving for the first time: the next stop (its forecast, its
// booking, its town), which pushes the facts and the clocks down by exactly
// its own growth. The facts and the clocks keep their shape throughout.
function checkLoadingStage(name, stage, at, ready) {
  const same = (one, other, keys) =>
    one && other && keys.every(key => Math.abs(one[key] - other[key]) <= 1);
  check(
    same(at.header, ready.header, ['x', 'y', 'width', 'height']),
    `${name}: ${stage} the panel's head moves or resizes while data loads`,
  );
  let allowed;
  if (!at.next && ready.next) {
    // The whole next stop arrived: what is under it moves by at least its
    // box and at most its box with both margins.
    allowed = [ready.next.height - 1, ready.nextFlow + 1];
  } else if (at.next && ready.next) {
    const grew = ready.next.height - at.next.height;
    const arrived =
      ready.nextParts.roads > at.nextParts.roads ||
      ready.nextParts.statuses > at.nextParts.statuses ||
      ready.nextParts.bookings > at.nextParts.bookings ||
      ready.nextParts.place > at.nextParts.place;
    check(
      Math.abs(grew) <= 1 || (grew > 0 && arrived),
      `${name}: ${stage} the next stop grows ${grew.toFixed(1)}px with no ` +
        'content arriving ' +
        JSON.stringify({ at: at.nextParts, ready: ready.nextParts }),
    );
    check(
      same(at.next, ready.next, ['x', 'y', 'width']) &&
        same(at.nextHead, ready.nextHead, ['x', 'width', 'height']),
      `${name}: ${stage} the next stop's line moves while its data loads`,
    );
    allowed = [grew - 1, grew + 1];
  } else allowed = [-1, 1];
  for (const part of ['facts', 'clocks']) {
    const dy = ready[part] && at[part] ? ready[part].y - at[part].y : 0;
    check(
      same(at[part], ready[part], ['x', 'width', 'height']) &&
        dy >= allowed[0] &&
        dy <= allowed[1],
      `${name}: ${stage} ${part} move ${dy.toFixed(1)}px or resize while ` +
        `data loads (allowed ${allowed.map(v => v.toFixed(1)).join('..')})`,
    );
  }
  check(
    at.cells.length === ready.cells.length &&
      at.cells.every(
        (cell, index) =>
          (cell === null) === (ready.cells[index] === null) &&
          (!cell || Math.abs(cell.height - ready.cells[index].height) <= 1),
      ),
    `${name}: ${stage} a fact cell changes height as its value arrives`,
  );
}

// A longer forecast (more rows in the next stop's ETA) may push the facts
// and the clocks down as a whole, but never reshape them.
async function checkIndependentPanelGroups(page, name) {
  const result = await page.locator(panelSelector).evaluate(source => {
    const host = source.cloneNode(true);
    host.style.visibility = 'hidden';
    host.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
    source.parentElement.append(host);
    try {
      const rect = node => {
        const { x, y, width, height } = node.getBoundingClientRect();
        return { x, y, width, height };
      };
      const measure = () => ({
        eta: host.querySelector('.fleet-truck-next__eta').offsetHeight,
        facts: rect(host.querySelector('.fleet-truck-facts')),
        clocks: rect(host.querySelector('.fleet-truck-clocks')),
        cells: [...host.querySelectorAll('.fleet-truck-facts__fact')].map(
          node => {
            const box = rect(node),
              grid = rect(host.querySelector('.fleet-truck-facts'));
            return { ...box, x: box.x - grid.x, y: box.y - grid.y };
          },
        ),
      });
      const before = measure();
      const road = host.querySelector('.fleet-truck-next .stop-hours__road');
      for (let i = 0; i < 4; i++) road.after(road.cloneNode(true));
      return { before, after: measure() };
    } finally {
      host.remove();
    }
  });
  const grew = result.after.eta - result.before.eta;
  check(
    grew > 20,
    `${name}: extra forecast rows must exercise a taller ETA (${grew}px)`,
  );
  for (const part of ['facts', 'clocks']) {
    const [was, is] = [result.before[part], result.after[part]];
    check(
      ['x', 'width', 'height'].every(
        key => Math.abs(was[key] - is[key]) <= 1,
      ) && Math.abs(is.y - was.y - grew) <= 1,
      `${name}: a longer forecast reshapes ${part} or moves it by other ` +
        `than its growth (${JSON.stringify({ was, is, grew })})`,
    );
  }
  check(
    result.before.cells.every((cell, index) =>
      ['x', 'y', 'width', 'height'].every(
        key => Math.abs(cell[key] - result.after.cells[index][key]) <= 1,
      ),
    ),
    `${name}: a longer forecast rearranges the facts`,
  );
  (report.independentPanelGroups ??= []).push({ name, ...result });
}

// The next stop's line: the stop and its town, the miles still to drive in
// the saved unit on the label's line, the booked window and the one
// forecast on one value column.
async function checkNextStopLine(page, name, units) {
  const next = page.locator(`${panelSelector} .fleet-truck-next`);
  const texts = await next.evaluate(element => ({
    label: element.querySelector('.fleet-truck-next__label').textContent,
    place: [...element.querySelector('.fleet-truck-next__place').childNodes]
      .filter(node => node.nodeType === Node.TEXT_NODE)
      .map(node => node.textContent)
      .join('')
      .trim(),
    town: element.querySelector('.fleet-truck-next__place > span')?.textContent,
    miles: element.querySelector('.fleet-truck-next__left strong').textContent,
    unit: element
      .querySelector('.fleet-truck-next__left small')
      .textContent.replace(/\s+/g, ' ')
      .trim(),
    booking: element
      .querySelector('.fleet-truck-next__hours')
      ?.textContent.replace(/\s+/g, ' ')
      .trim(),
    forecasts: element.querySelectorAll('.stop-hours').length,
    roads: element.querySelectorAll('.stop-hours__road').length,
    time: element.querySelector('.stop-hours__road time')?.dateTime,
    cycle: element
      .querySelector('.stop-hours__cycle-status')
      ?.textContent.trim(),
  }));
  const expectedUnit =
    units.distanceUnit === 'kilometers'
      ? 'km left'
      : units.distanceUnit === 'miles'
        ? 'mi left'
        : 'mi · 193 km left';
  check(
    texts.label === 'Next stop' &&
      texts.place.trim() === `Delivery · ${stops[0].name}` &&
      texts.town === 'Toronto, ON, Canada' &&
      texts.miles === (units.distanceUnit === 'kilometers' ? '193' : '120') &&
      texts.unit === expectedUnit,
    `${name}: the next stop says the stop, its town and the miles left in ` +
      `the saved unit (${JSON.stringify(texts)})`,
  );
  check(
    texts.forecasts === 1 &&
      texts.roads === 1 &&
      texts.time === '2026-09-08T16:00:00.0000000-04:00' &&
      texts.cycle === 'Cycle short by 3h 00m' &&
      texts.booking === 'Sep 8 · 04:00 PM EDT',
    `${name}: the next stop carries one forecast with its cycle word and ` +
      `the booked window (${JSON.stringify(texts)})`,
  );
  await checkBadgeBesideHour(next, `${name}-next-stop`);
  const layout = await next.evaluate(element => {
    const rect = node => node.getBoundingClientRect().toJSON();
    const label = rect(element.querySelector('.fleet-truck-next__label'));
    const left = rect(element.querySelector('.fleet-truck-next__left'));
    const head = element.querySelector('.fleet-truck-next__head');
    const hours = element.querySelector('.fleet-truck-next__hours > span');
    const time = element.querySelector('.stop-hours__road time');
    const term = element.querySelector('.fleet-truck-next__term');
    const eta = element.querySelector('.stop-hours__road .stop-hours__label');
    return {
      label,
      left,
      headOverflow: head.scrollWidth > head.clientWidth + 1,
      hoursLeft: hours.getBoundingClientRect().left,
      timeLeft: time.getBoundingClientRect().left,
      termLeft: term.getBoundingClientRect().left,
      etaLeft: eta.getBoundingClientRect().left,
      termTop: term.getBoundingClientRect().top,
      etaTop: eta.getBoundingClientRect().top,
      overflow: element.scrollWidth > element.clientWidth + 1,
    };
  });
  check(
    !layout.headOverflow &&
      layout.left.left >= layout.label.right - 1 &&
      layout.left.top < layout.label.bottom &&
      layout.left.bottom > layout.label.top,
    `${name}: the miles left share the Next stop line without overflow`,
  );
  // Appointment and ETA share one label column, so both hours start at
  // the same edge, the booking above the forecast.
  check(
    Math.abs(layout.termLeft - layout.etaLeft) <= 1 &&
      Math.abs(layout.hoursLeft - layout.timeLeft) <= 1 &&
      layout.termTop < layout.etaTop &&
      !layout.overflow,
    `${name}: the appointment shares the ETA's value column ` +
      `(${JSON.stringify(layout)})`,
  );
  // A long window, facility and town fold inside the line.
  const long = await page.locator(panelSelector).evaluate(source => {
    const host = source.cloneNode(true);
    host.style.visibility = 'hidden';
    host.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'));
    source.parentElement.append(host);
    try {
      const next = host.querySelector('.fleet-truck-next');
      next.querySelector('.fleet-truck-next__hours > span').textContent =
        'Sep 8 · 04:00 PM – Sep 9 · 06:00 AM EDT';
      next
        .querySelector('.fleet-truck-next__place')
        .prepend('Delivery · COSTCO SOUTHEAST REGIONAL DEPOT 174 RECEIVING ');
      next.querySelector('.fleet-truck-next__place > span').textContent =
        'Port St. Lucie, FL 34987, United States of America';
      return {
        overflow: next.scrollWidth > next.clientWidth + 1,
        panelOverflow: host.scrollWidth > host.clientWidth + 1,
      };
    } finally {
      host.remove();
    }
  });
  check(
    !long.overflow && !long.panelOverflow,
    `${name}: a long window, facility or town overflows the next stop`,
  );
  (report.nextStopLine ??= []).push({ name, texts, layout, long });
}

// The eight facts in two columns, each led by one icon, with the values
// the fixtures give and the saved temperature unit.
async function checkPanelFacts(page, name, units) {
  const facts = page.locator(`${panelSelector} .fleet-truck-facts`);
  await settleArrivals(page);
  const expectedTemperature =
    units.temperatureUnit === 'fahrenheit' ? '72.5 °F' : '22.5 °C';
  const result = await facts.evaluate(element => {
    const rect = node => node.getBoundingClientRect().toJSON();
    const grid = rect(element);
    return {
      columns: getComputedStyle(element).gridTemplateColumns.split(' ').length,
      overflow: element.scrollWidth > element.clientWidth + 1,
      grid,
      cells: [...element.querySelectorAll('.fleet-truck-facts__fact')].map(
        cell => ({
          name: cell.querySelector('dt').textContent.trim(),
          value: cell
            .querySelector('dd')
            .textContent.replace(/\s+/g, ' ')
            .trim(),
          label: cell.getAttribute('aria-label'),
          icons: [...cell.querySelectorAll(':scope > svg')].map(rect),
          box: rect(cell),
        }),
      ),
    };
  });
  const values = Object.fromEntries(
    result.cells.map(cell => [cell.name, cell.value]),
  );
  check(
    JSON.stringify(result.cells.map(cell => cell.name)) ===
      JSON.stringify(factNames),
    `${name}: the panel says its eight facts in order ` +
      `(${result.cells.map(cell => cell.name).join(', ')})`,
  );
  check(
    values.Driver === truck.driverName &&
      values.Trailer === truck.trailerNumber &&
      values.Motion === 'Moving · 45 mph' &&
      values.Duty === 'Sleeper Berth' &&
      values.Fuel === '75%' &&
      values.Engine === 'Driving' &&
      values.Temperature === expectedTemperature &&
      values.Location === 'Chicago, IL 60601, US',
    `${name}: the facts say the fixture's truck (${JSON.stringify(values)})`,
  );
  check(
    result.cells.find(cell => cell.name === 'Temperature')?.label ===
      'Outside temperature',
    `${name}: the temperature is named as the outside temperature`,
  );
  check(
    result.columns === 2 &&
      result.cells.every((cell, index) =>
        index % 2
          ? cell.box.left >= result.cells[index - 1].box.right - 1 &&
            Math.abs(cell.box.top - result.cells[index - 1].box.top) <= 1
          : Math.abs(cell.box.left - result.grid.left - 1) <= 1,
      ),
    `${name}: the facts stand in two columns, in reading order`,
  );
  check(
    result.cells.every(
      cell =>
        cell.icons.length === 1 &&
        cell.icons[0].width > 0 &&
        Math.abs(cell.icons[0].width - cell.icons[0].height) <= 1,
    ),
    `${name}: every fact leads with one square icon`,
  );
  check(
    !result.overflow &&
      result.cells.every(
        cell =>
          cell.box.left >= result.grid.left - 1 &&
          cell.box.right <= result.grid.right + 1,
      ),
    `${name}: the facts stay inside their grid`,
  );
  // Where the truck is reads as its town; the words copy the whole
  // address, and a long one folds in its cell without widening the grid.
  const copy = page.locator(`${panelSelector} .fleet-truck-facts__copy`);
  check(
    (await copy.getAttribute('title')) ===
      `Copy location: ${truck.formattedLocation}`,
    `${name}: the location keeps the full address in its tooltip`,
  );
  const longLocation = await copy.evaluate(element => {
    const original = element.textContent;
    const grid = element.closest('.fleet-truck-facts');
    const width = grid.clientWidth;
    element.textContent =
      '13077 SW Anthony F. Sansone Sr. Boulevard receiving entrance, ' +
      'Port St. Lucie, FL 34987, United States of America';
    const result = {
      overflow: grid.scrollWidth > grid.clientWidth + 1,
      widened: Math.abs(grid.clientWidth - width),
      lines: Math.round(
        element.getBoundingClientRect().height /
          parseFloat(getComputedStyle(element).lineHeight),
      ),
    };
    element.textContent = original;
    return result;
  });
  check(
    !longLocation.overflow && longLocation.widened <= 1,
    `${name}: a long location folds in its cell ` +
      `(${JSON.stringify(longLocation)})`,
  );
  await copy.click();
  check(
    (await page.evaluate(() => window.hoursFixtureCopiedText)) ===
      truck.formattedLocation,
    `${name}: the location copies the whole address`,
  );
  (report.panelFacts ??= []).push({ name, ...result, longLocation });
  return values;
}

// The driver's clocks: four fresh text readings with bars in cells, no
// dials, no duty line and no recap anywhere on the panel.
async function checkPanelClocks(page, name) {
  const result = await page.locator(panelSelector).evaluate(host => {
    const panel = host
      .querySelector('.fleet-truck-clocks')
      .getBoundingClientRect();
    return {
      fresh: host.querySelectorAll(
        '.fleet-truck-clocks .driver-hours__clock:not(.is-unavailable)',
      ).length,
      text: host.querySelectorAll('.fleet-truck-clocks .driver-hours--text')
        .length,
      dials: host.querySelectorAll('.driver-hours__dial').length,
      duty: host.querySelectorAll('.driver-duty').length,
      recap: host.querySelectorAll('.driver-next-recap').length,
      clocks: [...host.querySelectorAll('.driver-hours__clock')].map(clock => {
        const rect = clock.getBoundingClientRect();
        return {
          label: clock.querySelector('.driver-hours__label').textContent,
          reading: clock.querySelector('.driver-hours__reading').textContent,
          contained:
            rect.left >= panel.left - 1 && rect.right <= panel.right + 1,
          clipped: clock.scrollWidth > clock.clientWidth + 1,
        };
      }),
    };
  });
  check(
    result.fresh === 4 &&
      result.text === 1 &&
      result.dials === 0 &&
      result.duty === 0 &&
      result.recap === 0,
    `${name}: the panel reads four fresh clocks as text, with no dials, ` +
      `duty line or recap (${JSON.stringify(result)})`,
  );
  check(
    JSON.stringify(result.clocks.map(clock => clock.label)) ===
      JSON.stringify(['Break', 'Drive', 'Shift', 'Cycle']) &&
      result.clocks.every(clock => clock.contained && !clock.clipped),
    `${name}: Break, Drive, Shift and Cycle stay whole inside the clocks`,
  );
  return result;
}

async function checkPanelTypography(page, name) {
  const result = await page.locator(panelSelector).evaluate(host => {
    const style = node => getComputedStyle(node);
    const nodes = [
      ...host.querySelectorAll(
        '.fleet-map-inspector__title, .fleet-truck-next__label, ' +
          '.fleet-truck-next__place, .fleet-truck-facts dt, ' +
          '.fleet-truck-facts dd, .stop-hours__label, .stop-hours__clock, ' +
          '.driver-hours__label, .driver-hours__reading',
      ),
    ];
    const value = host.querySelector('.fleet-truck-facts dd');
    const label = host.querySelector('.fleet-truck-facts dt');
    const title = host.querySelector('.fleet-map-inspector__title');
    return {
      families: [...new Set(nodes.map(node => style(node).fontFamily))],
      valueWeight: Number(style(value).fontWeight),
      valueSize: parseFloat(style(value).fontSize),
      labelSize: parseFloat(style(label).fontSize),
      titleSize: parseFloat(style(title).fontSize),
    };
  });
  check(
    result.families.length === 1,
    `${name}: the panel uses one font family (${result.families})`,
  );
  // A fact's value is emphasised over its quieter label, and the unit
  // names the panel above both.
  check(
    result.valueWeight >= 600 &&
      result.valueSize >= result.labelSize &&
      result.titleSize > result.valueSize,
    `${name}: the unit, a fact's value and its label keep their hierarchy ` +
      `(${JSON.stringify(result)})`,
  );
  (report.panelTypography ??= []).push({ name, ...result });
}

// Where the panel stands: docked, in its own column of the stage and as
// tall as its content; narrower, floating over the map, bounded and
// scrolling inside. Never over the page's heading.
async function panelGeometry(page) {
  return page.locator('.fleet-map-info-reserved').evaluate(element => {
    const rect = node => node.getBoundingClientRect().toJSON();
    const style = getComputedStyle(element);
    const visible = [...element.querySelectorAll('*')].filter(
      node => node.getClientRects().length > 0,
    );
    const own = rect(element);
    return {
      own,
      map: rect(document.querySelector('#fleet-map')),
      stage: rect(document.querySelector('.fleet-map-stage')),
      chain: document.querySelector('.fleet-trip-chain')
        ? rect(document.querySelector('.fleet-trip-chain'))
        : null,
      rowGap:
        parseFloat(
          getComputedStyle(document.querySelector('.fleet-map-stage')).rowGap,
        ) || 0,
      rem: parseFloat(getComputedStyle(document.documentElement).fontSize),
      viewport: innerWidth,
      heading: rect(document.querySelector('.fleet-map-page h1')),
      position: style.position,
      overflowY: style.overflowY,
      maxHeight: style.maxHeight,
      scrollHeight: element.scrollHeight,
      clientHeight: element.clientHeight,
      horizontalOverflow: element.scrollWidth > element.clientWidth + 1,
    };
  });
}

function checkPanelGeometry(geometry, name, docked) {
  const { own, map } = geometry;
  check(
    own.top >= geometry.heading.bottom &&
      own.left >= map.left - 1 &&
      own.right <= map.right + 1 &&
      own.top >= map.top - 1 &&
      own.bottom <= map.bottom + 1,
    `${name}: the panel stays over the map, under the page heading ` +
      `(${JSON.stringify({ own, map })})`,
  );
  check(
    !geometry.horizontalOverflow &&
      ['auto', 'scroll'].includes(geometry.overflowY),
    `${name}: the panel scrolls up and down inside itself, never sideways`,
  );
  // One box per breakpoint, whatever it shows and whenever its answers
  // arrive (the owner, September 28): docked, its column's width and
  // 36rem tall, or less where the row above the chain is shorter;
  // floating, 56rem at most wide and 55% of the map tall; on a phone, the
  // map's width and half its height.
  const expected = docked
    ? {
        height: Math.min(
          36 * geometry.rem,
          geometry.chain
            ? geometry.chain.top - geometry.rowGap - own.top
            : Infinity,
        ),
      }
    : geometry.viewport < 768
      ? { width: map.width, height: map.height * 0.5 }
      : {
          width: Math.min(map.width, 56 * geometry.rem),
          height: map.height * 0.55,
        };
  check(
    Object.entries(expected).every(
      ([key, value]) => Math.abs(own[key] - value) <= 1,
    ),
    `${name}: the panel is its breakpoint's box ` +
      JSON.stringify({
        own: { width: own.width, height: own.height },
        expected,
      }),
  );
}

// With every value blank, as while it loads, the panel keeps its width and
// the map its place, and nothing on it scrolls sideways.
async function checkLoadingSpace(page, name) {
  const sizes = await page.locator(panelSelector).evaluate(source => {
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
    const measure = () => ({
      map: rect(map),
      width: host.getBoundingClientRect().width,
      overflow: host.scrollWidth > host.clientWidth + 1,
    });
    try {
      const ready = measure();
      host
        .querySelectorAll(
          '.fleet-truck-facts dd, .driver-hours__reading, ' +
            '.fleet-truck-next__left strong, .fleet-truck-next__hours',
        )
        .forEach(node => (node.textContent = '—'));
      host
        .querySelectorAll('.fleet-truck-next__eta .arrival-estimate')
        .forEach(node => node.replaceChildren());
      return { before, ready, loading: measure() };
    } finally {
      host.remove();
    }
  });
  for (const state of [sizes.ready, sizes.loading]) {
    for (const key of ['x', 'y', 'width', 'height'])
      check(
        Math.abs(state.map[key] - sizes.before[key]) <= 1,
        `${name}: ${key} of the map shifts while panel values load`,
      );
    check(
      !state.overflow && Math.abs(state.width - sizes.ready.width) <= 1,
      `${name}: loading values widen the panel or scroll it sideways`,
    );
  }
  (report.loadingSpace ??= []).push({ name, ...sizes });
}

async function checkInspectorLargeText(page, name) {
  const original = await page.evaluate(() => {
    const style = document.documentElement.style;
    const map = document.querySelector('#fleet-map').getBoundingClientRect();
    const saved = {
      value: style.getPropertyValue('font-size'),
      priority: style.getPropertyPriority('font-size'),
      root: parseFloat(getComputedStyle(document.documentElement).fontSize),
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
        expected.root * 2,
      original,
      { polling: 50 },
    );
    await page.screenshot({
      path: resolve(output, `${name}-selected-info-large-text.png`),
    });
    const layout = await page.locator(panelSelector).evaluate(host => {
      const rect = node => node.getBoundingClientRect().toJSON();
      const panel = rect(host);
      const parts = [
        '.fleet-map-inspector__header',
        '.fleet-truck-next',
        '.fleet-truck-facts',
        '.fleet-truck-clocks',
      ]
        .map(selector => host.querySelector(selector))
        .filter(node => node && node.getClientRects().length > 0)
        .map(node => ({
          name: node.className,
          ...rect(node),
          scroll: node.scrollWidth,
          width: node.clientWidth,
          // What does not fold, named.
          past: [...node.querySelectorAll('*')]
            .filter(
              child =>
                child.getBoundingClientRect().right >
                  node.getBoundingClientRect().right + 1 ||
                // An ellipsis is a value cut short on purpose.
                (child.clientWidth > 2 &&
                  getComputedStyle(child).textOverflow !== 'ellipsis' &&
                  child.scrollWidth > child.clientWidth + 1),
            )
            .map(child => ({
              name: child.className,
              text: (child.textContent ?? '').trim().slice(0, 40),
            })),
        }));
      return {
        panel,
        overflow: host.scrollWidth > host.clientWidth + 1,
        parts,
        clocks: [...host.querySelectorAll('.driver-hours__clock')].map(
          clock => {
            const box = clock.getBoundingClientRect();
            return {
              contained:
                box.left >= panel.left - 1 && box.right <= panel.right + 1,
              clipped: clock.scrollWidth > clock.clientWidth + 1,
            };
          },
        ),
      };
    });
    check(!layout.overflow, `${name}: 200% text scrolls the panel sideways`);
    for (const part of layout.parts)
      check(
        part.left >= layout.panel.left - 1 &&
          part.right <= layout.panel.right + 1 &&
          part.scroll <= part.width + 1 &&
          part.past.length === 0,
        `${name}: 200% text clips ${part.name} ` +
          `(${JSON.stringify(part.past)})`,
      );
    for (let i = 0; i < layout.parts.length; i++)
      for (const other of layout.parts.slice(i + 1)) {
        const part = layout.parts[i];
        check(
          !(
            part.left < other.right - 1 &&
            part.right > other.left + 1 &&
            part.top < other.bottom - 1 &&
            part.bottom > other.top + 1
          ),
          `${name}: 200% text overlaps ${part.name} and ${other.name}`,
        );
      }
    check(
      layout.clocks.length === 4 &&
        layout.clocks.every(clock => clock.contained && !clock.clipped),
      `${name}: enlarged clocks stay inside the panel unclipped`,
    );
    (report.largeTextInspector ??= []).push({ name, ...layout });
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

// The truck's actions are the map tool bar's: whole over the map, and
// touch-sized on a phone.
async function checkToolBar(page, name, selected) {
  const bar = await page.locator('.fleet-map-controls').evaluate(element => {
    const map = document.querySelector('#fleet-map').getBoundingClientRect();
    return {
      map: map.toJSON(),
      viewport: innerWidth,
      buttons: [...element.querySelectorAll('button')]
        .filter(button => button.getClientRects().length > 0)
        .map(button => ({
          name: button.getAttribute('aria-label'),
          disabled: button.disabled,
          ...button.getBoundingClientRect().toJSON(),
        })),
    };
  });
  const names = bar.buttons.map(button => button.name);
  check(
    // A phone keeps the layers in its Filters drawer instead.
    ['Follow', 'Show route', 'Camera', 'Route options', 'Fuel']
      .concat(bar.map.width < 768 ? [] : ['Map layers'])
      .every(label => names.includes(label)),
    `${name}: the tool bar keeps Follow, Fit route, Camera, Route options, ` +
      `Fuel and, wider than a phone, Layers (${names.join(', ')})`,
  );
  const outside = bar.buttons.filter(
    button =>
      button.left < bar.map.left - 1 ||
      button.right > bar.map.right + 1 ||
      button.top < bar.map.top - 1 ||
      button.bottom > bar.map.bottom + 1,
  );
  check(
    outside.length === 0,
    `${name}: the tool bar is whole over the map ` +
      `(${JSON.stringify(outside)})`,
  );
  check(
    bar.buttons.find(button => button.name === 'Fuel')?.disabled === !selected,
    `${name}: Fuel is ${selected ? 'enabled with' : 'disabled without'} ` +
      'a chosen truck',
  );
  if (bar.viewport < 800) {
    const small = bar.buttons.filter(
      button => button.width < 44 || button.height < 44,
    );
    check(
      small.length === 0,
      `${name}: phone tool bar buttons keep touch targets ` +
        JSON.stringify(
          small.map(({ name, width, height }) => ({ name, width, height })),
        ),
    );
  }
  (report.toolBar ??= []).push({ name, ...bar });
}

// The map's layers are behind the tool bar's Layers button; a phone keeps
// the same switches behind Filters as well.
async function setNextLoads(page, width) {
  if (width === 390) {
    await page.getByRole('button', { name: 'Filters', exact: true }).click();
    await page
      .locator('#fleet-map-filters')
      .getByRole('checkbox', { name: 'Next loads', exact: true })
      .locator('..')
      .click();
    await page.getByRole('button', { name: 'Filters', exact: true }).click();
    return;
  }
  const layers = page.getByRole('button', { name: 'Map layers', exact: true });
  await layers.click();
  await page
    .locator('#fleet-map-layer-menu')
    .getByText('Next loads', { exact: true })
    .click();
  await layers.click();
}

const shellReads = new Set([
  '/api/driver-groups',
  '/api/messaging/unread',
  '/api/messaging/changes',
]);

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
        // The shell's own reads (driver groups, the mailbox's badge and its
        // long poll) run on their own schedule on every page; every read
        // of fleet, dispatch or planning data is still counted.
        if (url.origin === origin && url.pathname.startsWith('/api/')) {
          if (!shellReads.has(url.pathname)) apiReads++;
          else {
            // Kept out of the duplicate-work count, but never invisible:
            // each shell endpoint's reads are reported per case.
            const counts = ((report.shellReads ??= {})[name] ??= {});
            counts[url.pathname] = (counts[url.pathname] ?? 0) + 1;
          }
        }
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
          // The layout's own reads, answered as a read-only account with no
          // driver groups and an empty mailbox. The mailbox's long poll is
          // held briefly, as the server holds it, so it does not spin.
          else if (url.pathname === '/api/driver-groups')
            fixture = success({ selected: null, groups: [] });
          else if (url.pathname === '/api/messaging/unread')
            fixture = success({ conversations: 0, more: false, newest: 0 });
          else if (url.pathname === '/api/messaging/changes') {
            const mailbox = url.searchParams.get('mailbox');
            if (mailbox) await new Promise(done => setTimeout(done, 2000));
            fixture = success({
              mailbox: mailbox ?? '00000000-0000-4000-8000-00000000c4a9',
              resync: !mailbox,
              conversations: [],
            });
          }
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
              const valueNode = row.querySelector('.stop-hours__value');
              const value = valueNode.getBoundingClientRect();
              return {
                labelLeft: label.left,
                valueLeft: value.left,
                valueWidth: value.width,
                columnGap:
                  parseFloat(getComputedStyle(valueNode).columnGap) || 0,
                arrivalLeft: arrival.left,
                arrivalRight: arrival.right,
                arrivalTop: arrival.top,
                arrivalWidth: arrival.width,
                cycleLeft: cycle.left,
                cycleTop: cycle.top,
                cycleWidth: cycle.width,
                arrivalBottom: arrival.bottom,
              };
            }),
          );
        for (const warning of summaryWarnings) {
          // The compact reading's cycle badge stands beside the hour and
          // its lateness where the value column has room for all of them,
          // and folds under the hour, at the value column's edge, only when
          // it has not; either way it never overlaps them.
          const fits =
            warning.arrivalWidth + warning.columnGap + warning.cycleWidth <=
            warning.valueWidth + 1;
          const beside =
            Math.abs(warning.cycleTop - warning.arrivalTop) <= 2 &&
            warning.cycleLeft >= warning.arrivalRight - 1;
          const under =
            warning.cycleTop >= warning.arrivalBottom - 1 &&
            Math.abs(warning.cycleLeft - warning.valueLeft) <= 1;
          check(
            warning.cycleLeft > warning.labelLeft && (fits ? beside : under),
            `${name}: summary cycle warning stands beside the hour when it ` +
              `fits and under it otherwise (${JSON.stringify(warning)})`,
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
        await futureCard.locator('.dispatch-load__number').focus();
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
        check(
          (await page.locator('.dispatch-planning__toggle').count()) === 0,
          `${name}: Dispatch has no header disclosure`,
        );
        await page
          .locator('.dispatch-planning__duty .driver-duty--row')
          .first()
          .waitFor();
        // The Next recap left the board card (the owner, September 27);
        // the duty row keeps the rest countdowns checked below.
        check(
          (await page.locator('.dispatch-truck .driver-next-recap').count()) ===
            0,
          `${name}: the board card no longer says the next recap`,
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
        const compactHeader = page.locator('.dispatch-planning--board');
        check(
          summaryReads <= boardSummaryReads + 1 && planningReads === 0,
          `${name}: returning to Dispatch uses at most one summary batch ` +
            'and no per-truck planning',
        );
        check(
          (await compactHeader
            .locator('.dispatch-planning__duty .driver-duty')
            .count()) === 1 &&
            (await compactHeader
              .locator('.driver-hours-panel .driver-duty')
              .count()) === 0,
          `${name}: duty status appears once in its own row`,
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
            route: rect(element.querySelector('.truck-readings')),
            driver: rect(element.querySelector('.dispatch-planning__duty')),
            hours: rect(element.querySelector('.dispatch-planning__clocks')),
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
        // The head, then the vehicle beside the clocks, then (opened) the
        // duty and recap, as the rows of the map's truck card.
        {
          const g = dispatchHeaderGeometry;
          check(
            g.route.top >= g.identity.bottom - 1 &&
              g.driver.top >= Math.max(g.route.bottom, g.hours.bottom) - 1,
            `${name}: Dispatch head, readings and duty stand in rows`,
          );
          // Beside the vehicle line while the truck card is at least the
          // dispatch-truck-compact width (1050px, a container query), and
          // under it below that.
          if (g.header.width >= 1050)
            check(
              g.hours.left >= g.route.right - 1 &&
                Math.abs(g.hours.top - g.route.top) <= g.route.height,
              `${name}: HOS clocks stand beside the vehicle line`,
            );
          else
            check(
              g.hours.top >= g.route.bottom - 1,
              `${name}: a truck card under 1050px puts the clocks under ` +
                'the vehicle line',
            );
        }
        await compactHeader.screenshot({
          path: resolve(output, `${name}-dispatch-header.png`),
        });
        check(
          normalize(
            await page
              .locator('.dispatch-planning--board .fuel-reading')
              .innerText(),
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
              .locator('.dispatch-planning--board .dispatch-planning__details')
              .count()) === 0,
          `${name}: each actual stop has one compact summary without inline detailed forecasts or header duplicates`,
        );
        check(
          (
            await page
              .locator('.dispatch-planning__duty .driver-duty__rest')
              .allTextContents()
          ).some(text => text.includes('34h reset in 27h 52m')),
          `${name}: current-driver reset countdown missing`,
        );
        await futureCard.locator('.dispatch-load__number').click();
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
      const docked = width >= 1100;
      const unselected = await page
        .locator('.fleet-map-info-reserved')
        .evaluate(element => ({
          height: element.getBoundingClientRect().height,
          empty:
            element
              .querySelector('.fleet-map-inspector__empty')
              ?.getClientRects().length > 0,
          text: element
            .querySelector('.fleet-map-inspector__empty')
            ?.textContent.replace(/\s+/g, ' ')
            .trim(),
          panel: !!element.querySelector('.fleet-truck-panel'),
        }));
      // Nothing chosen: no truck panel, and nothing reserved over a map
      // narrower than the docked workspace. Whether the docked column shows
      // its empty state is recorded, not asserted: its stylesheets disagree
      // (the workspace draws it, the stage hides it).
      check(
        !unselected.panel &&
          (docked || unselected.height <= (width === 390 ? 48 : 80)) &&
          (await page
            .locator('.fleet-map-info-reserved.has-selection')
            .count()) === 0,
        `${name}: an unselected map shows no truck and reserves no ` +
          `space over the map (${JSON.stringify(unselected)})`,
      );
      (report.unselected ??= []).push({ name, ...unselected });
      await checkToolBar(page, `${name}-unselected`, false);
      await page.screenshot({
        path: resolve(output, `${name}-unselected-map.png`),
      });
      await countAnimationStarts(page);
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
      await page.locator(panelSelector).waitFor();
      const phone = await isPhonePanel(page);
      const titleControls = () =>
        page
          .locator(
            `${panelSelector} .fleet-map-inspector__title, ` +
              `${panelSelector} .fleet-map-inspector__close`,
          )
          .evaluateAll(elements =>
            elements.map(element => {
              const { x, y, width, height } = element.getBoundingClientRect();
              // Against the panel's content: focus moving to a control
              // scrolls the panel without moving anything within it. A
              // sticky head does not scroll with the content.
              const sticky =
                getComputedStyle(
                  element.closest('.fleet-map-inspector__header'),
                ).position === 'sticky';
              const scroll = sticky
                ? 0
                : element.closest('.fleet-map-info-reserved').scrollTop;
              return { x, y: y + scroll + scrollY, width, height };
            }),
          );
      const initialTitleControls = await titleControls();
      const checkTitleControls = async stage => {
        const controls = await titleControls();
        check(
          controls.length === initialTitleControls.length &&
            controls.every((bounds, index) =>
              Object.keys(bounds).every(
                key =>
                  Math.abs(bounds[key] - initialTitleControls[index][key]) <= 1,
              ),
            ),
          `${name}-${stage}: the unit and Close keep their slots ` +
            JSON.stringify({ initial: initialTitleControls, now: controls }),
        );
      };
      // A cold panel is open whole at every width: the phone's Details /
      // Hide details left it, and Follow is the map tool bar's alone
      // (owner decisions of 2026-09-28).
      const toolBarFollow = page
        .locator('.fleet-map-controls')
        .getByRole('button', { name: 'Follow', exact: true });
      const cold = {
        facts: await page.locator('.fleet-truck-facts').isVisible(),
        clocks: await page.locator('.fleet-truck-clocks').isVisible(),
        toggle: await page
          .locator(`${panelSelector} .fleet-map-mobile-summary__toggle`)
          .count(),
        headFollow: await page
          .locator(`${panelSelector} .fleet-map-inspector__header`)
          .getByRole('button', { name: /^Follow/ })
          .count(),
        toolBarFollow:
          (await toolBarFollow.isVisible()) &&
          (await toolBarFollow.isEnabled()) &&
          (await toolBarFollow.evaluate(element => {
            // Reachable: inside the viewport and on top at its centre.
            const box = element.getBoundingClientRect();
            const top = document.elementFromPoint(
              box.left + box.width / 2,
              box.top + box.height / 2,
            );
            return (
              box.left >= 0 &&
              box.right <= innerWidth &&
              box.top >= 0 &&
              box.bottom <= innerHeight &&
              !!top &&
              element.contains(top)
            );
          })),
      };
      check(
        cold.facts &&
          cold.clocks &&
          !cold.toggle &&
          cold.headFollow === 0 &&
          cold.toolBarFollow,
        `${name}: a cold panel is open whole, with no Details and no ` +
          `Follow in its head, Follow in the tool bar (${JSON.stringify(cold)})`,
      );
      await expandTruckCard(page, `${name}-cold`);
      await checkTitleControls('cold-open');
      const loadingMapRect = await stableMapRect(
        page,
        mapRect,
        `${name}-first-selection-loading`,
      );
      const loadingGeometry = await truckLoadingGeometry(page);
      await page.locator('.fleet-truck-facts').evaluate(facts => {
        window.hoursFixtureFacts = facts;
        window.hoursFixtureCells = [...facts.children];
      });
      await page.screenshot({
        path: resolve(output, `${name}-route-loading.png`),
      });
      check(
        await page
          .getByRole('button', { name: 'Route options', exact: true })
          .isDisabled(),
        `${name}: Route options stays disabled before the load is known`,
      );
      const afterRender = () =>
        page.evaluate(
          () =>
            new Promise(done =>
              requestAnimationFrame(() => requestAnimationFrame(done)),
            ),
        );
      previewHeld.release();
      await waitForHeld(detailsHeld, `${name}-load-reference`);
      await afterRender();
      const partialGeometry = await truckLoadingGeometry(page);
      await stableMapRect(page, mapRect, `${name}-preview-ready`);
      await waitForHeld(planningHeld, `${name}-live-planning`);
      await page.screenshot({
        path: resolve(output, `${name}-route-preview.png`),
      });
      const detailsRead = page.waitForResponse(
        response =>
          new URL(response.url()).pathname === `/api/dispatch/${currentId}`,
      );
      detailsHeld.release();
      await detailsRead;
      await afterRender();
      const detailsGeometry = await truckLoadingGeometry(page);
      await stableMapRect(page, mapRect, `${name}-load-reference-ready`);
      await page.screenshot({
        path: resolve(output, `${name}-route-reference.png`),
      });
      planningHeld.release();
      await page.waitForFunction(selector => {
        const route = document.querySelector('.fleet-map-route-info');
        return (
          route?.getAttribute('aria-busy') === 'false' &&
          !route.classList.contains('is-awaiting-route') &&
          !!document.querySelector(
            `${selector} .fleet-truck-next .stop-hours__road time`,
          )
        );
      }, panelSelector);
      await afterRender();
      const selectedMapRect = await stableMapRect(
        page,
        mapRect,
        `${name}-first-selection-ready`,
      );
      const readyGeometry = await truckLoadingGeometry(page);
      for (const [stage, geometry] of Object.entries({
        loading: loadingGeometry,
        partial: partialGeometry,
        details: detailsGeometry,
      }))
        checkLoadingStage(name, stage, geometry, readyGeometry);
      check(
        await page
          .locator('.fleet-truck-facts')
          .evaluate(
            facts =>
              facts === window.hoursFixtureFacts &&
              [...facts.children].every(
                (cell, index) => cell === window.hoursFixtureCells[index],
              ),
          ),
        `${name}: loading fills the retained facts, not a new layout`,
      );
      (report.loadingGeometry ??= []).push({
        name,
        loading: loadingGeometry,
        partial: partialGeometry,
        details: detailsGeometry,
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
      await expandTruckCard(page, `${name}-reselected`);
      await page.screenshot({
        path: resolve(output, `${name}-selected-info-initial.png`),
      });
      await checkNextStopLine(page, name, units);
      check(
        (await page.evaluate(() => window.hoursFixture.distanceUnit)) ===
          units.distanceUnit,
        `${name}: map stop and station formatting receives the same preference`,
      );
      const factValues = await checkPanelFacts(page, name, units);
      await checkPanelClocks(page, name);
      await checkPanelTypography(page, `${name}-ready`);
      await checkToolBar(page, `${name}-selected`, true);
      if (phone) {
        await measureTruckControls(page, `${name}-phone-ready`);
        await stableMapRect(page, mapRect, `${name}-phone-ready`);
        await checkTitleControls('phone-ready');
        await page.screenshot({
          path: resolve(output, `${name}-phone-readings.png`),
        });
        await checkMobileTruckScrolling(page, output, name, check);
        await checkTruckReadingsLayout(page, output, name, check);
      }
      const headClocks = () =>
        page
          .locator('.fleet-truck-clocks .driver-hours__reading')
          .allTextContents();
      const clocksBefore = await headClocks();
      const primaryGeometry = () =>
        page
          .locator(
            [
              '.fleet-map-inspector__header',
              '.fleet-map-inspector__title',
              '.fleet-map-inspector__close',
              '.fleet-truck-next',
              '.fleet-truck-facts',
              '.fleet-truck-facts__fact',
              '.fleet-truck-clocks',
            ]
              .map(selector => `${panelSelector} ${selector}`)
              .join(', '),
          )
          .evaluateAll(elements =>
            elements.map(element => {
              const { x, y, width, height } = element.getBoundingClientRect();
              // Measured against the panel's content: moving focus to a
              // button may scroll it without moving anything within it.
              // The scroll itself is recorded, not hidden.
              const card = document.querySelector('.fleet-map-info-reserved');
              const sticky =
                getComputedStyle(
                  card.querySelector('.fleet-map-inspector__header'),
                ).position === 'sticky' &&
                !!element.closest('.fleet-map-inspector__header');
              const scroll = sticky ? 0 : card.scrollTop;
              return { x, y: y + scroll + scrollY, width, height };
            }),
          );
      const cardScroll = () =>
        page
          .locator('.fleet-map-info-reserved')
          .evaluate(element => element.scrollTop);
      const initialControls = await measureTruckControls(page, `${name}-ready`);
      const initialPrimary = await primaryGeometry();
      const initialScroll = await cardScroll();
      const interactionMark = await page.evaluate(() => performance.now());
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
      await checkTitleControls('following-background');
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
      await stableMapRect(page, mapRect, `${name}-retained-selection`);
      check(
        normalize(
          await page.locator(`${panelSelector} .fleet-truck-next`).innerText(),
        ).includes('Sep 8 · 04:00 PM EDT') &&
          (await page
            .locator(`${panelSelector} .fleet-truck-next__booking`)
            .count()) === 1,
        `${name}: interactions retain the one appointment`,
      );
      const retainedPrimary = await primaryGeometry();
      (report.interactionScroll ??= []).push({
        name,
        before: initialScroll,
        after: await cardScroll(),
      });
      await checkPanelTypography(page, `${name}-retained`);
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
          `or resize truck facts (${JSON.stringify(
            retainedPrimary
              .map((rect, index) => [index, rect, initialPrimary[index]])
              .filter(([, rect, before]) =>
                Object.keys(rect).some(
                  key => Math.abs(rect[key] - (before?.[key] ?? 0)) > 1,
                ),
              ),
          )})`,
      );
      const overlayGeometry = await panelGeometry(page);
      checkPanelGeometry(overlayGeometry, name, docked);
      await checkNoReplayedArrival(page, name, interactionMark);
      await page.screenshot({
        path: resolve(output, `${name}-selected-info-retained.png`),
      });
      await checkIndependentPanelGroups(page, name);
      await page.locator(panelSelector).screenshot({
        path: resolve(output, `${name}-current-status.png`),
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
          routeStopFocus: window.hoursFixture.routeStopFocus,
        }));
      const beforeBackgroundCamera = await cameraState();
      await page.locator('.fleet-map-inspector').evaluate(element => {
        element.scrollTop = 0;
        window.hoursFixtureRetainedTruck =
          element.querySelector('.fleet-truck-panel');
      });
      const beforeBackgroundFacts = await primaryGeometry();
      for (let click = 0; click < 2; click++) {
        await page.evaluate(() => window.hoursFixture.background());
        assert.equal(
          await page.locator(panelSelector).isVisible(),
          true,
          `${name}: background clicks keep the truck panel`,
        );
        assert.equal(
          (await page.locator('.fleet-truck-facts').isVisible()) &&
            (await page.locator('.fleet-truck-clocks').isVisible()) &&
            (await page.locator('.fleet-truck-next').isVisible()),
          true,
          `${name}: background clicks retain the next stop, facts and clocks`,
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
            .locator('.fleet-truck-panel')
            .evaluate(element => element === window.hoursFixtureRetainedTruck),
          true,
          `${name}: background retains the mounted truck panel`,
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
      await checkLoadingSpace(page, name);
      await checkInspectorLargeText(page, name);
      await stableMapRect(page, mapRect, `${name}-restored-text-size`);
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
      await setNextLoads(page, width);
      await page.waitForFunction(
        () => window.hoursFixture?.next?.routes?.length === 1,
      );
      const card = page.getByRole('region', {
        name: 'Selected next load',
        exact: true,
      });
      const panelBox = () =>
        page.locator('.fleet-map-info-reserved').evaluate(element => {
          const { x, y, width, height } = element.getBoundingClientRect();
          return { x, y: y + scrollY, width, height };
        });
      const truckBox = await panelBox();
      await page.evaluate(() => window.hoursFixture.selectStop(0));
      // The next load's stop card reads as the route stop card and may
      // carry more than one forecast; its first is the stop's.
      await card.locator('.stop-hours').first().waitFor();
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
      // The next load stop always shows its details: there is no toggle.
      check(
        (await page
          .locator('button[aria-controls="fleet-map-next-load-details"]')
          .count()) === 0,
        `${name}: the next load stop has no Details toggle`,
      );
      check(
        (await card
          .locator('xpath=ancestor::*[contains(@class,"fleet-map-inspector")]')
          .count()) === 1 &&
          (await page.locator('.fleet-map-details-card').count()) === 0,
        `${name}: future details must replace the shared inspector content, not open a lower popup`,
      );
      // The truck panel stays mounted, hidden, while the stop's card has the
      // inspector, so its readings are not fetched again on the way back.
      check(
        (await page.locator('.fleet-truck-panel').count()) === 1 &&
          !(await page.locator('.fleet-truck-facts').isVisible()) &&
          !(await page.locator('.fleet-truck-next').isVisible()),
        `${name}: selected future details must hide, not duplicate, ` +
          'the retained truck panel',
      );
      await stableMapRect(page, mapRect, `${name}-future-inspection`);
      const nextHead = await card.evaluate(element => {
        const inspector = element.closest('.fleet-map-inspector');
        const sticky = inspector
          .querySelector('.fleet-map-inspector__header')
          .getBoundingClientRect();
        const load = element
          .querySelector('.fleet-map-next-load-card__header')
          .getBoundingClientRect();
        return {
          covered: load.top < sticky.bottom - 1,
          scrollTop: inspector.scrollTop,
        };
      });
      check(
        !nextHead.covered,
        `${name}: the sticky title covers the next load's load/order line ` +
          `(scrolled ${nextHead.scrollTop}px)`,
      );
      check(
        normalize(await card.innerText()).includes('Cycle short'),
        `${name}: selected pickup shortage missing`,
      );
      // The next load's stop card says the cycle shortfall and drops the
      // recap row (the owner, September 26); the truck's recap stays on
      // the truck. Its With-recap alternative is checked below.
      check(
        (await card.locator('.stop-hours__recap').count()) === 0,
        `${name}: the next load's stop card repeats the truck's recap`,
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
      // The hidden panel keeps its clocks while the stop's card is open.
      assert.deepEqual(
        await headClocks(),
        clocksBefore,
        `${name}: pending refresh changed the panel's HOS clocks`,
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
      const stopBox = await panelBox();
      const returnTruckReads = apiReads;
      // Back to truck shows the retained panel again; it must not arrive
      // anew (release session, 2026-09-28).
      const backMark = await page.evaluate(() => performance.now());
      await page
        .getByRole('button', { name: 'Back to truck', exact: true })
        .click();
      await page.locator(panelSelector).waitFor();
      await settleArrivals(page);
      await checkNoReplayedArrival(
        page,
        `${name}-back-from-next-load`,
        backMark,
      );
      const backBox = await panelBox();
      // One box whatever the card shows (the owner, September 28).
      check(
        [stopBox, backBox].every(box =>
          ['x', 'y', 'width', 'height'].every(
            key => Math.abs(box[key] - truckBox[key]) <= 1,
          ),
        ),
        `${name}: the map card keeps one box for the truck, the stop and ` +
          `back again ${JSON.stringify({ truckBox, stopBox, backBox })}`,
      );
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
        factValues,
        dispatchHeaderGeometry,
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
          await page.locator('.fleet-truck-clocks .driver-hours').waitFor();
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
