import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';

// The fuel plan opens from the map tool bar's Fuel (the owner, September
// 27): the plan takes the panel, and its Edit plan button is the one a
// dispatcher presses.
async function fuelPlan(page) {
  await page
    .locator('.fleet-map-controls')
    .getByRole('button', { name: 'Fuel', exact: true })
    .click();
  const plan = page.locator('section[aria-label="Fuel plan"]');
  await plan.waitFor();
  return plan.getByRole('button', { name: 'Edit plan', exact: true });
}
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'MAP_TEST_ARTIFACT_DIR must identify staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('fuel-editor', process.env.FUEL_EDITOR_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const uuid = number =>
  `11111111-1111-1111-1111-${String(number).padStart(12, '0')}`;
const userId = uuid(1),
  truckId = uuid(2),
  dispatchId = uuid(3),
  stopId = uuid(4),
  routeId = uuid(5);
const stations = [6, 7, 8].map(uuid),
  before = [9, 10, 11].map(uuid);
const names = ['LOVES #706', 'LOVES #306', 'LOVES #333'];
const originalToken = '2026-09-10T12:00:00Z';
const point = { latitude: 39.2, longitude: -79.1 };
const truck = {
  truckId,
  unitNumber: '54777',
  driverName: 'Fixture Driver',
  trailerNumber: 'GG1030',
  ...point,
  speed: 0,
  heading: 90,
  engineState: 'Off',
  fuelPercent: 46,
  updatedAt: originalToken,
};
const stop = {
  id: stopId,
  sequence: 1,
  job: 'Delivery',
  name: 'Fixture receiving facility',
  city: 'Fort Mill',
  address: '308 Springhill Farm Rd, Fort Mill, SC 29715, US',
  province: 'SC',
  country: 'US',
  scheduledDate: '2026-09-10',
  scheduledTime: '16:00:00',
  ...point,
};
const initialEdits = [
  {
    stationId: stations[0],
    beforeStopId: before[0],
    buyGallons: 25,
    fillToTarget: false,
  },
  {
    stationId: stations[1],
    beforeStopId: before[1],
    buyGallons: 100,
    fillToTarget: false,
  },
];
const routeStops = before.map((id, index) => ({
  id,
  sequence: index + 1,
  point,
  name: ['Fort Mill, SC', 'Greensboro, NC', 'Amsterdam, NY'][index],
  address: [
    '308 Springhill Farm Rd, Fort Mill, SC',
    '100 Main St, Greensboro, NC',
    '1730 NY-5S, Amsterdam, NY',
  ][index],
  job: index === 1 ? 'Pickup' : 'Delivery',
}));
const segments = routeStops.map((beforeStop, index) => ({
  beforeStopId: beforeStop.id,
  afterStop: routeStops[index - 1] ?? null,
  beforeStop,
  dispatchId,
}));
const success = response => ({ success: true, response, errors: [] });
const luminance = color =>
  color
    .match(/[\d.]+/g)
    .slice(0, 3)
    .map(Number)
    .map(value => value / 255)
    .map(value =>
      value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4,
    )
    .reduce(
      (sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index],
      0,
    );
async function dragStop(
  page,
  context,
  editor,
  stationName,
  target,
  touch,
  screenshot,
) {
  const handle = editor.getByRole('button', {
    name: `Move ${stationName}`,
    exact: true,
  });
  const list = editor.locator('.fuel-plan-editor__stops');
  await list.scrollIntoViewIfNeeded();
  const sourceElement = await handle.elementHandle();
  const targetElement = await target.elementHandle();
  await list.evaluate(
    (surface, { source, target }) => {
      const start = source
        .closest('.fuel-plan-editor__stop')
        .getBoundingClientRect();
      const end = target.getBoundingClientRect();
      const bounds = surface.getBoundingClientRect();
      surface.scrollTop += Math.min(start.top, end.top) - bounds.top - 4;
    },
    { source: sourceElement, target: targetElement },
  );
  const sourceBox = await handle.boundingBox(),
    targetBox = await target.boundingBox(),
    listBox = await list.boundingBox();
  const start = {
    x: sourceBox.x + sourceBox.width / 2,
    y: sourceBox.y + sourceBox.height / 2,
  };
  const end = {
    x: targetBox.x + targetBox.width / 2,
    y: targetBox.y + targetBox.height * 0.75,
  };
  for (const point of [start, end])
    assert.ok(
      point.y > listBox.y && point.y < listBox.y + listBox.height,
      'drag endpoints must be in the visible timeline',
    );
  await list.evaluate(surface =>
    surface.addEventListener(
      'pointerdown',
      event => {
        window.fuelFixture.lastDragInput = {
          input: event.pointerType,
          trusted: event.isTrusted,
        };
      },
      { once: true, capture: true },
    ),
  );
  let cdp;
  if (touch) {
    cdp = await context.newCDPSession(page);
    await cdp.send('Input.dispatchTouchEvent', {
      type: 'touchStart',
      touchPoints: [{ ...start, id: 1 }],
    });
  } else {
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
  }
  for (let step = 1; step <= 12; step++) {
    const point = {
      x: start.x + ((end.x - start.x) * step) / 12,
      y: start.y + ((end.y - start.y) * step) / 12,
    };
    if (touch)
      await cdp.send('Input.dispatchTouchEvent', {
        type: 'touchMove',
        touchPoints: [{ ...point, id: 1 }],
      });
    else await page.mouse.move(point.x, point.y);
  }
  assert.equal(
    await editor.locator('.fuel-plan-editor__stop.is-dragging').count(),
    1,
    'real pointer event starts a drag',
  );
  assert.equal(
    await target.evaluate(element => element.classList.contains('drop-after')),
    true,
    'drop preview follows the fixed anchor or row',
  );
  if (screenshot) await editor.screenshot({ path: screenshot });
  if (touch) {
    await cdp.send('Input.dispatchTouchEvent', {
      type: 'touchEnd',
      touchPoints: [],
    });
    await cdp.detach();
  } else await page.mouse.up();
  await sourceElement.dispose();
  await targetElement.dispose();
  const actualInput = await page.evaluate(
    () => window.fuelFixture.lastDragInput,
  );
  assert.deepEqual(actualInput, {
    input: touch ? 'touch' : 'mouse',
    trusted: true,
  });
  return actualInput;
}
const mapStub = `export async function createFleetMap(element, _key, callbacks) {
  element.style.background = 'var(--ui-surface-muted)';
  const fixture = window.fuelFixture = { selectStation(stationId, name, beforeStopId = null, addNew = false, dispatch = '${dispatchId}') {
    return callbacks.invokeMethodAsync('OnFuelStationEdit', '${truckId}', dispatch, stationId, name, beforeStopId, addNew);
  }};
  return {setOptions(){},setTrucks(){},setStationsVisible(){},
    setTrafficVisible(){},setIfta(){},
    clearSelection(){},clearNextLoads(){},setNextLoadsVisible(){},clearNextLoadSelection(){},closeStationPopup(){},
    setStopEtas(){},setLoadReference(){},setDistanceUnit(){},setFollow(){},finishInitialView(){},setInspectorMode(){},clearMapInspection(){},
    setInspectionSuspended(value){fixture.inspectionSuspended = value;},
    setFuelEditorTruck(id){fixture.editingTruck = id;},
    focusFuelStation(station){fixture.focusedStation = station;},
    clearFuelStationFocus(_focus, identity){
      fixture.focusedStation = null;
      if (identity) fixture.returnToRoute = identity;
    },
    setRouteBytes(bytes){fixture.plan = JSON.parse(new TextDecoder().decode(bytes));return true;},
    setNextLoadsBytes(){},focusTruck(){return true;},
    setRouteEditor(){},openStation(){},selectNextStop(){},fitNextLoad(){},
    setStations(){},setPriceOverview(){},setStopCompletions(){},
    focusRouteStop(){},openRouteStop(){},centerStop(){},showRoute(){},
    dispose(){delete window.fuelFixture;}};
}`;
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
  scope:
    'Actual staged Blazor fuel editor, intercepted deterministic API and map-selection callbacks. No database, live writes, provider, GPU or calculation accuracy checks.',
  cases: [],
  errors: [],
  unexpectedRequests: [],
};
// The truck panel (the owner, September 27): the next stop, the facts and
// the driver's clocks; a phone's panel opens closed and Details opens the
// facts and the clocks. Its actions are the map tool bar's.
const panelSelector = '.fleet-map-inspector[data-inspector-mode="truck"]';
async function assertTruckInformation(page, name) {
  const panel = page.locator(panelSelector);
  const toggle = panel.locator('.fleet-map-mobile-summary__toggle');
  if (
    (await toggle.isVisible()) &&
    (await toggle.getAttribute('aria-expanded')) === 'false'
  )
    await toggle.click();
  for (const part of [
    '.fleet-truck-next',
    '.fleet-truck-facts',
    '.fleet-truck-clocks',
  ])
    assert.equal(
      await panel.locator(part).isVisible(),
      true,
      `${name}: ${part} is shown once the panel is open`,
    );
  assert.equal(
    await page
      .locator('.fleet-map-controls')
      .getByRole('button', { name: 'Fuel', exact: true })
      .isEnabled(),
    true,
    `${name}: the tool bar's Fuel acts on the chosen truck`,
  );
}
// The editor returns to the fuel plan card it was opened from when it is
// saved, reset or cancelled (the owner, September 26); the truck panel
// stays mounted, hidden, behind it.
async function assertFuelPlanCard(page, name) {
  assert.equal(
    await page.locator('section[aria-label="Fuel plan"]').isVisible(),
    true,
    `${name}: the fuel plan card is shown`,
  );
  assert.equal(
    (await page.locator('.fleet-truck-panel').count()) === 1 &&
      !(await page.locator(`${panelSelector} .fleet-truck-facts`).isVisible()),
    true,
    `${name}: the truck panel stays mounted behind the plan`,
  );
}
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
await mkdir(output, { recursive: true });

try {
  for (const { width, height } of [
    { width: 1440, height: 1000 },
    { width: 390, height: 844 },
    { width: 390, height: 667 },
    { width: 320, height: 667 },
  ])
    for (const theme of ['light', 'dark']) {
      const mobile = width < 768;
      const name = `${width}-${height}-${theme}`;
      if (process.env.FUEL_EDITOR_CASE && process.env.FUEL_EDITOR_CASE !== name)
        continue;
      let savedEdits = structuredClone(initialEdits),
        savedToken = originalToken,
        manuallyEdited = false;
      const editsSeen = [],
        writes = [],
        loadReads = [];
      function preview(edits, quantityStopIndex = null) {
        const canonical = edits.map(edit => ({
          ...edit,
          beforeStopId:
            edit.beforeStopId ?? before[stations.indexOf(edit.stationId)],
          purchaseLimitGallons: 177.3,
        }));
        const errors = canonical.some(
          edit =>
            edit.stationId === stations[2] &&
            !edit.fillToTarget &&
            edit.buyGallons === 25,
        )
          ? [
              'Not enough fuel to reach the following stop. Increase the purchase or add a station.',
            ]
          : [];
        const plan = {
          truckId,
          calculatedAt: savedToken,
          pricingDate: '2026-09-10',
          manuallyEdited,
          dispatchIds: [dispatchId],
          needsRefresh: false,
          purchaseGallons: 125,
          purchaseCostUsd: 765.43,
          arrivalGallons: 51,
          startingGallons: 97,
          remainingMiles: 645,
          stops: canonical.map((edit, index) => ({
            ...edit,
            number: index + 1,
            // The server's identity of a planned visit, which the plan's
            // list is keyed by.
            visitKey: `${edit.stationId}:${index}`,
            dispatchId,
            name: names[stations.indexOf(edit.stationId)],
            point,
            address: '3499 Lee Jackson Hwy, Staunton, VA 24401, USA',
            arrivalGallons: 34,
            departureGallons: edit.fillToTarget ? 211.3 : 34 + edit.buyGallons,
            buyGallons: edit.fillToTarget ? 177.3 : edit.buyGallons,
            milesAhead: (index + 1) * 100,
            purchaseCostUsd: 123.45,
            yourPrice: 5.613,
            cashUsdPerGallon: 5.613,
            economicUsdPerGallon: 5.286,
            currency: 'USD',
            unit: 'US gal',
          })),
        };
        // This intercepted API supplies the complete table. The mounted Client only copies its values.
        const quantityChoices =
          Number.isInteger(quantityStopIndex) &&
          quantityStopIndex >= 0 &&
          quantityStopIndex < canonical.length
            ? {
                stopIndex: quantityStopIndex,
                options: Array.from({ length: 32 }, (_, step) => {
                  const sliderGallons = 25 + step * 5,
                    fillToTarget = sliderGallons === 180;
                  const selectedGallons = fillToTarget ? 177.3 : sliderGallons;
                  let carry = 0;
                  const visits = plan.stops.map((stop, index) => {
                    const arrivalGallons = stop.arrivalGallons + carry;
                    const buyGallons =
                      index < quantityStopIndex
                        ? stop.buyGallons
                        : index === quantityStopIndex
                          ? selectedGallons
                          : Math.max(25, stop.buyGallons - carry);
                    carry += buyGallons - stop.buyGallons;
                    return {
                      arrivalGallons,
                      buyGallons,
                      departureGallons: arrivalGallons + buyGallons,
                      fillToTarget:
                        index < quantityStopIndex
                          ? stop.fillToTarget
                          : index === quantityStopIndex && fillToTarget,
                      purchaseLimitGallons: Math.max(0, 211.3 - arrivalGallons),
                      purchaseCostUsd: 123.45,
                    };
                  });
                  const errors =
                    canonical[quantityStopIndex].stationId === stations[2] &&
                    sliderGallons === 25
                      ? [
                          'Not enough fuel to reach the following stop. Increase the purchase or add a station.',
                        ]
                      : [];
                  return {
                    sliderGallons,
                    fillToTarget,
                    visits,
                    errors,
                    arrivalGallons: 51 + carry,
                    purchaseGallons: 125 + carry,
                    purchaseCostUsd: 765.43,
                    economicCostUsd: 650,
                    expectedFutureFuelCostUsd: 50,
                  };
                }),
              }
            : null;
        return {
          plan,
          stops: canonical,
          expectedCalculatedAt: savedToken,
          tankGallons: 211.3,
          fillLimitGallons: 211.3,
          errors,
          valuesAvailable: true,
          segments,
          quantityChoices,
        };
      }
      function planning() {
        const fuel = preview(savedEdits).plan;
        const plan = {
          id: routeId,
          dispatchId,
          truckId,
          version: 1,
          calculatedAt: originalToken,
          originalPlannedMiles: 871,
          fromCurrentPosition: true,
          profile: {},
          fuelPlan: fuel,
          stops: [{ ...stop, point }],
          tracking: {
            nextStopId: stopId,
            passedStopIds: [],
            visitedStops: {},
            allStopsPassed: false,
          },
          route: {
            miles: 871,
            seconds: 30000,
            warnings: [],
            points: [],
            legs: [
              {
                miles: 871,
                seconds: 30000,
                points: [point, { latitude: 35.1, longitude: -80.9 }],
              },
            ],
          },
        };
        return {
          truckId,
          dispatchId,
          loadNumber: 1375,
          hos: {
            breakMs: 214 * 60000,
            driveMs: 151 * 60000,
            shiftMs: 151 * 60000,
            cycleMs: 3659 * 60000,
            updatedAt: originalToken,
            currentDutyStatus: 'driving',
          },
          state: {
            profile: { tankGallons: 211.3 },
            plan,
            apiConfigured: false,
            fuelPercent: 46,
            fuelUpdatedAt: originalToken,
            progress: {
              progressMiles: 226,
              remainingMiles: 645,
              remainingSeconds: 7200,
              position: point,
            },
          },
        };
      }
      const context = await browser.newContext({
        viewport: { width, height },
        colorScheme: theme,
        hasTouch: mobile,
        locale: 'en-US',
        timezoneId: 'America/Toronto',
        reducedMotion: 'reduce',
        serviceWorkers: 'block',
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
          report.unexpectedRequests.push(
            `${request.method()} ${url.origin}${url.pathname}`,
          );
          return route.abort('blockedbyclient');
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
              name: 'Fixture Administrator',
              email: 'fixture@example.invalid',
              isAdmin: true,
            };
          else if (url.pathname === '/api/settings/appearance')
            value = success({ theme });
          else if (url.pathname === '/api/settings/dispatch')
            value = success({ loadNumberPrefix: 'AMF', revision: 1 });
          else if (url.pathname === '/api/fleet/locations')
            value = success({ trucks: [truck], points: [truck] });
          else if (url.pathname === '/api/fleet/planning/previews')
            value = success([]);
          // The layout's own reads, answered as a read-only account with no
          // driver groups and an empty mailbox; the mailbox's long poll is
          // held briefly, as the server holds it, so it does not spin.
          else if (url.pathname === '/api/fuel/price-overview')
            value = success([]);
          // The trip chain reads the truck's board row; none is needed here.
          else if (url.pathname === '/api/dispatch/board')
            value = success({
              items: [],
              page: 1,
              pageSize: 20,
              totalCount: 0,
              totalPages: 0,
            });
          else if (url.pathname === '/api/driver-groups')
            value = success({ selected: null, groups: [] });
          else if (url.pathname === '/api/messaging/unread')
            value = success({ conversations: 0, more: false, newest: 0 });
          else if (url.pathname === '/api/messaging/changes') {
            const mailbox = url.searchParams.get('mailbox');
            if (mailbox) await new Promise(done => setTimeout(done, 2000));
            value = success({
              mailbox: mailbox ?? '00000000-0000-4000-8000-00000000c4a9',
              resync: !mailbox,
              conversations: [],
            });
          }
          // The map reads the fuel price basis beside itself; it keeps the
          // page's own default, so the answer changes nothing here.
          else if (url.pathname === '/api/settings/planning')
            value = success({
              preferences: { useIfta: true },
              revision: 1,
              updatedAt: null,
            });
          else if (url.pathname === '/api/fleet/hos')
            value = success({
              [truckId]: { driverName: truck.driverName, hos: planning().hos },
            });
          else if (
            url.pathname === `/api/fleet/trucks/${truckId}/weather` &&
            request.method() === 'GET'
          )
            value = success({
              celsius: 22.5,
              condition: 'CLEAR',
              description: 'Clear',
              isDaytime: true,
              updatedAt: new Date().toISOString(),
            });
          else if (
            url.pathname === `/api/fleet/trucks/${truckId}/camera` &&
            request.method() === 'POST'
          )
            value = success(uuid(90));
          else if (
            url.pathname === `/api/fleet/trucks/${truckId}/camera` ||
            url.pathname === `/api/fleet/trucks/${truckId}/camera/${uuid(90)}`
          )
            value = success({
              status: 'unavailable',
              url: null,
              capturedAt: null,
            });
          else if (
            url.pathname === `/api/fleet/trucks/${truckId}/planning/preview` ||
            url.pathname === `/api/fleet/trucks/${truckId}/planning`
          )
            value = success(planning());
          else if (url.pathname === `/api/dispatch/${dispatchId}`)
            value = success({
              id: dispatchId,
              truckId,
              loadNumber: 1375,
              orderNumber: '567086821',
              status: 'in_transit',
              stops: [stop],
            });
          else if (
            url.pathname ===
              `/api/dispatch/${dispatchId}/planning/fuel/edit/preview` &&
            request.method() === 'POST'
          ) {
            const body = request.postDataJSON();
            editsSeen.push(body);
            value = success(
              preview(body.stops ?? savedEdits, body.quantityStopIndex),
            );
          } else if (
            url.pathname === `/api/dispatch/${dispatchId}/planning/fuel/edit` &&
            request.method() === 'PUT'
          ) {
            const body = request.postDataJSON();
            writes.push(body);
            assert.equal(
              body.expectedCalculatedAt,
              savedToken,
              `${name}: changed draft concurrency token`,
            );
            savedEdits = structuredClone(body.stops);
            savedToken = '2026-09-10T12:01:00Z';
            manuallyEdited = true;
            value = success(preview(savedEdits));
          } else if (
            url.pathname ===
              `/api/dispatch/${dispatchId}/planning/fuel/reset` &&
            request.method() === 'POST'
          ) {
            writes.push({ reset: true, ...request.postDataJSON() });
            savedEdits = structuredClone(initialEdits);
            manuallyEdited = false;
            value = success(planning());
          }
          if (value === undefined) {
            report.unexpectedRequests.push(
              `${request.method()} ${url.pathname}`,
            );
            return route.abort('blockedbyclient');
          }
          return route.fulfill({ status: 200, json: value });
        }
        if (!['GET', 'HEAD'].includes(request.method())) {
          report.unexpectedRequests.push(`${request.method()} ${url.pathname}`);
          return route.abort('blockedbyclient');
        }
        if (
          /\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(
            url.pathname,
          )
        )
          return route.fulfill({
            status: 200,
            contentType: 'text/javascript',
            body: mapStub,
          });
        if (request.isNavigationRequest())
          return route.fulfill({
            status: 200,
            contentType: 'text/html',
            body: html,
          });
        return route.fallback();
      });
      const page = await context.newPage();
      page.on('pageerror', error =>
        report.errors.push(`${name}: ${error.message}`),
      );
      page.on('console', message => {
        if (message.type() === 'error')
          report.errors.push(`${name}: ${message.text()}`);
      });
      await page.goto(`${origin}/fleet/map?truckId=${truckId}`);
      await page.evaluate(() => {
        window.fuelHorizontalEvents = [];
        document.addEventListener(
          'scroll',
          () => {
            if (document.documentElement.scrollLeft)
              window.fuelHorizontalEvents.push({
                left: document.documentElement.scrollLeft,
                active: document.activeElement?.className,
                phase: window.fuelTestPhase,
              });
          },
          true,
        );
      });
      await page
        .locator(`${panelSelector} .fleet-truck-next`)
        .waitFor({ state: 'attached' });
      await assertTruckInformation(page, name);
      if (!mobile) {
        await page
          .locator('.driver-hours__clock[aria-label="Cycle: 60:59 remaining"]')
          .waitFor();
        const mapBeforeProbes = await page.locator('#fleet-map').boundingBox();
        for (const probeWidth of [320, 390, 768, 1200, 1440, 2000])
          for (const scale of [100, 200]) {
            await page.setViewportSize({ width: probeWidth, height });
            await page.evaluate(scale => {
              document.documentElement.style.fontSize = scale + '%';
            }, scale);
            await assertTruckInformation(
              page,
              `${name}/${probeWidth}/${scale}`,
            );
            // The panel reads the clocks as text. What the page may
            // ask of the component is a minimum width for a clock, which
            // is the property the component publishes for it.
            const clocks = await page
              .locator('.fleet-truck-clocks .driver-hours__clock')
              .evaluateAll(elements =>
                elements.map(element => {
                  const box = element.getBoundingClientRect();
                  const reading = element.querySelector(
                    '.driver-hours__reading',
                  );
                  return {
                    text: reading?.textContent,
                    width: box.width,
                    clipped: element.scrollWidth > element.clientWidth + 1,
                  };
                }),
              );
            assert.equal(clocks.length, 4);
            assert.equal(
              await page
                .locator('.fleet-truck-clocks .driver-hours__dial')
                .count(),
              0,
              `${name}/${probeWidth}/${scale}: the panel's clocks are text`,
            );
            for (const clock of clocks)
              assert.ok(
                !clock.clipped && clock.text,
                `${name}/${probeWidth}/${scale}: an HOS reading is clipped: ` +
                  JSON.stringify(clock),
              );
            // The clocks' own group is where the page sets what it wants
            // of them, so that is where the property is read from.
            await page
              .locator('.fleet-truck-clocks')
              .evaluate(node =>
                node.style.setProperty('--hos-clock-min-width', '96px'),
              );
            await page.waitForFunction(
              () => {
                const clocks = [
                  ...document.querySelectorAll(
                    '.fleet-truck-clocks .driver-hours__clock',
                  ),
                ];
                return (
                  clocks.length === 4 &&
                  clocks.every(
                    clock => clock.getBoundingClientRect().width >= 96 - 1,
                  )
                );
              },
              null,
              { timeout: 5000 },
            );
            await page
              .locator('.fleet-truck-clocks')
              .evaluate(node =>
                node.style.removeProperty('--hos-clock-min-width'),
              );
          }
        await page.setViewportSize({ width, height });
        await page.evaluate(() => {
          document.documentElement.style.fontSize = '100%';
        });
        await page.waitForFunction(
          expected => {
            const actual = document
              .querySelector('#fleet-map')
              .getBoundingClientRect();
            return ['x', 'y', 'width', 'height'].every(
              key => Math.abs(actual[key] - expected[key]) <= 1,
            );
          },
          mapBeforeProbes,
          { timeout: 5000 },
        );
        // The facts read as one table: the values of a row share one
        // baseline, every value one type, every icon one size.
        const metrics = await page
          .locator(`${panelSelector} .fleet-truck-facts__fact`)
          .evaluateAll(cells =>
            cells.map(cell => {
              const value = cell.querySelector('dd');
              const icon = cell.querySelector(':scope > svg');
              return {
                top: value.getBoundingClientRect().top,
                text: value.textContent.trim().slice(0, 20),
                font: getComputedStyle(value).fontSize,
                lineHeight: getComputedStyle(value).lineHeight,
                iconHeight: icon ? icon.getBoundingClientRect().height : 0,
              };
            }),
          );
        (report.factMetrics ??= []).push({ name, metrics });
        assert.equal(metrics.length, 8);
        for (const [index, metric] of metrics.entries()) {
          if (index % 2)
            assert.ok(
              Math.abs(metric.top - metrics[index - 1].top) <= 1,
              `${name}: the values of a row share one baseline`,
            );
          assert.equal(metric.font, metrics[0].font);
          assert.equal(metric.lineHeight, metrics[0].lineHeight);
          assert.ok(metric.iconHeight > 0);
          assert.equal(metric.iconHeight, metrics[0].iconHeight);
        }
      }
      const mapBefore = await page.locator('#fleet-map').boundingBox();
      const scrollBefore = await page.evaluate(() => ({
        x: scrollX,
        y: scrollY,
      }));
      await page.locator('#fleet-map').evaluate(element => {
        window.fuelFixture.mapElement = element;
      });
      await page.locator('.fleet-truck-panel').evaluate(element => {
        window.fuelFixture.truckPanel = element;
        window.fuelFixture.truckPanels = [...element.children];
      });
      assert.equal(
        await page
          .getByRole('button', { name: 'Edit fuel plan', exact: true })
          .count(),
        0,
      );
      assert.equal(
        await page
          .getByRole('button', { name: 'Calculate Fuel', exact: true })
          .count(),
        0,
      );
      const edit = await fuelPlan(page);
      // Where the plan card stands: the editor takes its place.
      const planCard = await page
        .locator('.fleet-map-info-reserved')
        .evaluate(element => element.getBoundingClientRect().toJSON());
      await edit.click();
      const editor = page.locator('.fuel-plan-editor');
      await editor
        .locator('.fuel-plan-editor__stop')
        .nth(1)
        .waitFor({ state: 'attached' });
      assert.equal(
        await page.locator('.fleet-map-inspector').isVisible(),
        false,
        `${name}: editor replaces the inspector`,
      );
      assert.equal(
        await page.evaluate(() => window.fuelFixture.inspectionSuspended),
        true,
      );
      assert.equal(
        await page
          .locator('.fleet-map-page__background')
          .evaluate(element => element.inert),
        false,
      );
      assert.equal(
        await page
          .locator('.fleet-map-page__background')
          .getAttribute('aria-hidden'),
        null,
      );
      assert.equal(
        await page
          .locator('#fleet-map')
          .evaluate(element => element.closest('[inert]')),
        null,
      );
      await page.locator('.fleet-map-toolbar input').first().focus();
      assert.deepEqual(
        await page.locator('#fleet-map').boundingBox(),
        mapBefore,
        `${name}: toolbar focus must not move the map`,
      );
      assert.equal(
        await page.evaluate(() =>
          Boolean(
            document.activeElement?.closest('.fleet-map-page__background'),
          ),
        ),
        true,
        `${name}: visible background controls remain keyboard accessible beside a nonmodal card`,
      );
      await editor
        .getByRole('button', { name: 'Close fuel plan editor' })
        .focus();
      assert.equal(
        await editor.locator('input[type="range"]').getAttribute('step'),
        '5',
      );
      assert.equal(
        await editor.locator('input[type="range"]').getAttribute('min'),
        '25',
      );
      // The tank around the chosen stop is said in words (the rings went
      // with the old editor): after fueling 59 of 211.3 gallons.
      assert.match(
        await editor
          .locator('.fuel-plan-editor__levels > span:last-child strong')
          .innerText(),
        /\b28%/,
      );
      assert.equal(
        await editor
          .getByRole('slider', { name: 'Gallons to buy' })
          .isEnabled(),
        true,
      );
      assert.equal(
        await editor
          .getByRole('slider', { name: 'Gallons to buy' })
          .getAttribute('max'),
        '180',
      );
      assert.equal(
        (await editor.locator('label[for="fuel-edit-quantity"]').innerText())
          .replace(/\s+/g, ' ')
          .trim(),
        'Quantity 25 US gal',
      );
      assert.equal(
        await editor.locator('.fuel-plan-editor__anchor').count(),
        3,
      );
      assert.equal(
        await editor.getByRole('combobox', { name: 'Fuel stop order' }).count(),
        0,
      );
      assert.ok(
        (await editor.innerText()).includes('$123.45'),
        'server-provided cost is displayed',
      );
      assert.equal(
        await editor.locator('.fuel-plan-editor__price strong').innerText(),
        '5.613 USD / US gal',
      );
      const badgeColors = await editor
        .locator('.fuel-plan-editor__stop-heading .fleet-fuel-visit__number')
        .first()
        .evaluate(element => ({
          color: getComputedStyle(element).color,
          background: getComputedStyle(element).backgroundColor,
        }));
      const badgeLuminance = [
        luminance(badgeColors.color),
        luminance(badgeColors.background),
      ].sort((a, b) => a - b);
      assert.ok(
        (badgeLuminance[1] + 0.05) / (badgeLuminance[0] + 0.05) >= 4.5,
        `${name}: fuel number contrast`,
      );
      await page.waitForFunction(
        station => window.fuelFixture.focusedStation?.stationId === station,
        stations[0],
      );
      assert.equal(writes.length, 0);
      // One list at every width: the Route / Fuel / Map tabs of the phone
      // are gone (the owner, September 26).
      assert.equal(
        await editor.getByRole('group', { name: 'Fuel editor view' }).count(),
        0,
        `${name}: the editor has no view tabs`,
      );
      const initialTimeline = await editor
        .locator('.fuel-plan-editor__stops')
        .evaluate(list => {
          const bounds = list.getBoundingClientRect();
          const entries = [...list.querySelectorAll('[data-reorder-key]')];
          const first = entries[0].getBoundingClientRect();
          const second = entries[1].getBoundingClientRect();
          return {
            height: bounds.height,
            firstPairHeight: second.bottom - first.top,
            firstPairBottom: second.bottom - bounds.top,
            visible: entries
              .filter(entry => {
                const box = entry.getBoundingClientRect();
                return (
                  box.top >= bounds.top - 1 && box.bottom <= bounds.bottom + 1
                );
              })
              .map(entry => entry.dataset.reorderKey),
            total: entries.length,
          };
        });
      await editor.screenshot({ path: resolve(output, `${name}-initial.png`) });
      await page.screenshot({
        path: resolve(output, `${name}-initial-page.png`),
      });
      // The editor has the plan card's height at every width (the owner,
      // September 26), so the route scrolls in one list with the chosen
      // stop opened in place; the old desktop's whole route at once went
      // with its two-column card. Every row stays reachable.
      {
        assert.ok(
          initialTimeline.firstPairHeight <= initialTimeline.height + 1,
          `${name}: the list has room for two complete operational rows`,
        );
        if (initialTimeline.firstPairBottom <= initialTimeline.height + 1)
          assert.ok(
            initialTimeline.visible.length >= 2,
            `${name}: two complete rows are visible with the introduction`,
          );
        const list = editor.locator('.fuel-plan-editor__stops');
        const visiblePair = await list.evaluate(surface => {
          const entries = [...surface.querySelectorAll('[data-reorder-key]')];
          const bounds = surface.getBoundingClientRect();
          surface.scrollTop +=
            entries[0].getBoundingClientRect().top - bounds.top;
          return entries.filter(entry => {
            const row = entry.getBoundingClientRect();
            return row.top >= bounds.top - 1 && row.bottom <= bounds.bottom + 1;
          }).length;
        });
        assert.ok(
          visiblePair >= 2,
          `${name}: scrolling the introduction exposes two complete rows`,
        );
        const rows = list.locator('[data-reorder-key]');
        for (let index = 0; index < initialTimeline.total; index++) {
          const reachable = await rows.nth(index).evaluate(row => {
            const surface = row.closest('.fuel-plan-editor__stops');
            const bounds = surface.getBoundingClientRect();
            surface.scrollTop += row.getBoundingClientRect().top - bounds.top;
            const box = row.getBoundingClientRect();
            // The chosen stop opens in place and may be taller than the
            // list; it is reachable when it starts at the list's top and
            // fills it. Any other row is whole in view.
            const visibleBottom = Math.min(box.bottom, bounds.bottom);
            const hit = document.elementFromPoint(
              box.left + box.width / 2,
              (box.top + visibleBottom) / 2,
            );
            return {
              ok:
                box.top >= bounds.top - 1 &&
                (box.bottom <= bounds.bottom + 1 ||
                  box.height > bounds.height) &&
                hit?.closest('[data-reorder-key]') === row,
              row: { top: box.top, bottom: box.bottom },
              list: { top: bounds.top, bottom: bounds.bottom },
              hit: hit?.className?.baseVal ?? hit?.className,
            };
          });
          assert.ok(
            reachable.ok,
            `${name}: route row ${index + 1} is fully reachable and ` +
              `uncovered (${JSON.stringify(reachable)})`,
          );
        }
        await list.evaluate(surface => {
          surface.scrollTop = 0;
        });
      }
      const purchaseSlider = editor.getByRole('slider', {
        name: 'Gallons to buy',
      });
      await page.evaluate(() => {
        window.fuelTestPhase = 'slider';
      });
      const requestsBeforeQuantity = editsSeen.length;
      await purchaseSlider.evaluate(element => {
        element.value = '35';
        element.dispatchEvent(new Event('input', { bubbles: true }));
      });
      await page.waitForFunction(
        () =>
          document
            .querySelector('.fuel-plan-editor__stop')
            ?.textContent.includes('35 US gal') &&
          [
            ...document.querySelectorAll('.fuel-plan-editor__stop'),
          ][1]?.textContent.includes('90 US gal') &&
          !document.querySelector(
            '.fuel-plan-editor button.fuel-plan-editor__save',
          )?.disabled,
      );
      assert.equal(
        editsSeen.length,
        requestsBeforeQuantity,
        `${name}: 25→35 and 100→90 use prepared server values without an HTTP preview`,
      );
      await purchaseSlider.focus();
      await purchaseSlider.press('End');
      await page.waitForFunction(
        () =>
          document.querySelector('.fuel-plan-editor__full input')?.checked ===
          true,
      );
      assert.match(
        await editor
          .locator('.fuel-plan-editor__levels > span:last-child strong')
          .innerText(),
        /\b100%/,
      );
      await purchaseSlider.press('ArrowLeft');
      await page.waitForFunction(
        () =>
          document.querySelector('.fuel-plan-editor__full input')?.checked ===
            false &&
          !document.querySelector(
            '.fuel-plan-editor button.fuel-plan-editor__save',
          )?.disabled,
      );
      assert.equal(
        await purchaseSlider.inputValue(),
        '175',
        `${name}: one five-gallon tick before Full tank fits the server's 177.3-gallon purchase limit`,
      );
      assert.equal(
        await editor.getByRole('checkbox', { name: 'Full tank' }).isChecked(),
        false,
      );
      await purchaseSlider.press('End');
      await page.waitForFunction(
        () =>
          document.querySelector('.fuel-plan-editor__full input')?.checked ===
            true &&
          !document.querySelector(
            '.fuel-plan-editor button.fuel-plan-editor__save',
          )?.disabled,
      );
      assert.equal(
        editsSeen.length,
        requestsBeforeQuantity,
        `${name}: partial/full quantity changes do not send preview requests`,
      );
      const gestures = [];
      await page.evaluate(() => {
        window.fuelTestPhase = 'drag';
      });
      const anchorInput = await dragStop(
        page,
        context,
        editor,
        names[0],
        editor.locator(`[data-reorder-key="stop:${before[0]}"]`),
        mobile,
        resolve(output, `${name}-drag-anchor.png`),
      );
      await page.waitForFunction(() =>
        document
          .querySelector('.fuel-plan-editor__position')
          ?.textContent.includes('After Delivery'),
      );
      assert.deepEqual(
        editsSeen.at(-1).stops.map(edit => edit.stationId),
        stations.slice(0, 2),
      );
      assert.deepEqual(
        editsSeen.at(-1).stops.map(edit => edit.beforeStopId),
        [before[1], before[1]],
      );
      gestures.push({
        ...anchorInput,
        target: 'fixed delivery anchor',
        beforeStopId: before[1],
      });
      const rowInput = await dragStop(
        page,
        context,
        editor,
        names[0],
        editor.locator('.fuel-plan-editor__stop').filter({ hasText: names[1] }),
        mobile,
      );
      await page.waitForFunction(
        () =>
          document.querySelector('.fuel-plan-editor__stop strong')
            ?.textContent === 'LOVES #306',
      );
      assert.deepEqual(
        editsSeen.at(-1).stops.map(edit => edit.stationId),
        [stations[1], stations[0]],
      );
      assert.deepEqual(
        editsSeen.at(-1).stops.map(edit => edit.beforeStopId),
        [before[1], before[1]],
      );
      assert.equal(editsSeen.at(-1).stops[0].buyGallons, 25);
      assert.equal(editsSeen.at(-1).stops[1].fillToTarget, true);
      assert.equal(
        await editor.locator('.is-dragging, .drop-after, .drop-before').count(),
        0,
      );
      gestures.push({
        ...rowInput,
        target: 'fuel row',
        order: [stations[1], stations[0]],
      });
      await editor.locator('.fuel-plan-editor__cost').scrollIntoViewIfNeeded();
      await page.evaluate(() => {
        window.fuelTestPhase = 'controls';
      });
      await editor.screenshot({
        path: resolve(output, `${name}-selected-controls.png`),
      });
      assert.equal(
        await editor
          .getByRole('slider', { name: 'Gallons to buy' })
          .isVisible(),
        true,
      );
      assert.equal(
        await editor.getByRole('checkbox', { name: 'Full tank' }).isVisible(),
        true,
      );
      for (const selector of [
        '.fuel-plan-editor__levels',
        'input[type="range"]',
        '.fuel-plan-editor__cost',
      ]) {
        const control = editor.locator(selector);
        await control.scrollIntoViewIfNeeded();
        assert.equal(
          await control.evaluate(element => {
            const content = element
              .closest('.fuel-plan-editor__stops')
              .getBoundingClientRect();
            const bounds = element.getBoundingClientRect();
            return (
              bounds.top >= content.top - 1 &&
              bounds.bottom <= content.bottom + 1
            );
          }),
          true,
          `${name}: ${selector} is fully reachable inside the list`,
        );
      }
      assert.equal(
        writes.length,
        0,
        `${name}: dragging only previews the draft`,
      );
      const readsBeforeCancel = [...loadReads];
      await editor.getByRole('button', { name: 'Cancel', exact: true }).click();
      await page.evaluate(() => {
        window.fuelTestPhase = 'reopen';
      });
      await editor.waitFor({ state: 'detached' });
      await assertFuelPlanCard(page, `${name}-cancelled`);
      assert.equal(
        await page
          .locator('.fleet-truck-panel')
          .evaluate(
            element =>
              element === window.fuelFixture.truckPanel &&
              window.fuelFixture.truckPanels.every(
                (panel, index) => panel === element.children[index],
              ),
          ),
        true,
        `${name}: cancelling retains the mounted truck panel`,
      );
      assert.deepEqual(
        loadReads,
        readsBeforeCancel,
        `${name}: restoring truck information does not reload its data`,
      );
      assert.deepEqual(
        await page.evaluate(() => window.fuelFixture.returnToRoute),
        { truckId, dispatchId },
        `${name}: Cancel returns to this truck's route`,
      );
      assert.equal(
        await page
          .locator('.fleet-map-page__background')
          .evaluate(element => element.inert),
        false,
      );
      await page.evaluate(
        ({ station, name }) => window.fuelFixture.selectStation(station, name),
        { station: stations[0], name: names[0] },
      );
      await editor
        .locator('.fuel-plan-editor__stop')
        .nth(1)
        .waitFor({ state: 'attached' });
      assert.deepEqual(
        savedEdits,
        initialEdits,
        `${name}: cancelling drag restores the saved plan`,
      );
      await page.evaluate(
        ({ station, name }) => window.fuelFixture.selectStation(station, name),
        { station: stations[2], name: names[2] },
      );
      await editor
        .locator('.fuel-plan-editor__stop')
        .nth(2)
        .waitFor({ state: 'attached' });
      const handle = editor.getByRole('button', {
        name: `Move ${names[2]}`,
        exact: true,
      });
      await handle.focus();
      await handle.press('ArrowUp');
      await page.waitForFunction(
        () =>
          document.querySelector('.fuel-plan-editor__stop strong')
            ?.textContent === 'LOVES #333',
      );
      assert.equal(editsSeen.at(-1).stops[0].beforeStopId, before[0]);
      await page.waitForFunction(
        station => window.fuelFixture.focusedStation?.stationId === station,
        stations[2],
      );
      const slider = editor.getByRole('slider', { name: 'Gallons to buy' });
      await page.waitForFunction(
        () =>
          !document.querySelector('.fuel-plan-editor input[type="range"]')
            ?.disabled,
      );
      const requestsBeforeValidation = editsSeen.length;
      await slider.evaluate(element => {
        element.value = '25';
        element.dispatchEvent(new Event('input', { bubbles: true }));
      });
      assert.equal(
        await editor.getByRole('checkbox', { name: 'Full tank' }).isChecked(),
        false,
      );
      await editor.getByRole('alert').waitFor();
      assert.equal(
        await editor
          .getByRole('button', { name: 'Save plan', exact: true })
          .isEnabled(),
        false,
      );
      // Arriving with 34 of 211.3 gallons.
      assert.match(
        await editor
          .locator('.fuel-plan-editor__levels > span:first-child strong')
          .innerText(),
        /\b16%/,
      );
      await editor.screenshot({
        path: resolve(output, `${name}-validation.png`),
      });
      await slider.evaluate(element => {
        element.value = '30';
        element.dispatchEvent(new Event('input', { bubbles: true }));
      });
      await page.waitForFunction(
        () =>
          !document.querySelector(
            '.fuel-plan-editor button.fuel-plan-editor__save',
          )?.disabled,
      );
      assert.equal(await slider.inputValue(), '30');
      assert.equal(
        editsSeen.length,
        requestsBeforeValidation,
        `${name}: invalid and corrected prepared choices stay local`,
      );
      assert.equal(editsSeen.at(-1).expectedCalculatedAt, originalToken);
      await slider.evaluate(element => {
        element.value = element.max;
        element.dispatchEvent(new Event('input', { bubbles: true }));
      });
      await page.waitForFunction(
        () =>
          document.querySelector('.fuel-plan-editor__full input')?.checked ===
          true,
      );
      await page.waitForFunction(
        () =>
          !document.querySelector(
            '.fuel-plan-editor button.fuel-plan-editor__save',
          )?.disabled,
      );
      assert.equal(editsSeen.length, requestsBeforeValidation);
      await slider.evaluate(element => {
        element.value = '30';
        element.dispatchEvent(new Event('input', { bubbles: true }));
      });
      await page.waitForFunction(
        () =>
          !document.querySelector(
            '.fuel-plan-editor button.fuel-plan-editor__save',
          )?.disabled,
      );
      const geometry = await editor.evaluate(element => {
        const rect = element.getBoundingClientRect();
        const map = document
          .querySelector('#fleet-map')
          .getBoundingClientRect();
        const list = element.querySelector('.fuel-plan-editor__stops');
        const content = list.getBoundingClientRect();
        const above = element
          .querySelector('.fuel-plan-editor__totals')
          .getBoundingClientRect();
        const footer = element
          .querySelector('.fuel-plan-editor__footer')
          .getBoundingClientRect();
        const requiredControlHeight = Math.max(
          ...[
            '.fuel-plan-editor__levels',
            'input[type="range"]',
            '.fuel-plan-editor__cost',
          ].map(
            selector =>
              element.querySelector(selector).getBoundingClientRect().height,
          ),
        );
        const mapReceivesPointer = [0.1, 0.35, 0.65, 0.9].some(x =>
          [0.1, 0.35, 0.65, 0.9].some(y =>
            document
              .elementFromPoint(
                map.left + map.width * x,
                map.top + map.height * y,
              )
              ?.closest('#fleet-map'),
          ),
        );
        return {
          left: rect.left,
          right: rect.right,
          top: rect.top,
          bottom: rect.bottom,
          width: rect.width,
          map: { x: map.x, y: map.y, width: map.width, height: map.height },
          mapRetained:
            window.fuelFixture.mapElement ===
            document.querySelector('#fleet-map'),
          mapReceivesPointer,
          content: {
            left: content.left,
            top: content.top,
            right: content.right,
            bottom: content.bottom,
          },
          aboveBottom: above.bottom,
          footerTop: footer.top,
          requiredControlHeight,
          mapCount: document.querySelectorAll('#fleet-map').length,
          documentWidth: document.documentElement.scrollWidth,
          viewport: innerWidth,
          contentWidth: list.clientWidth,
          horizontalScrollers: [...document.querySelectorAll('*')]
            .filter(node => node.scrollLeft)
            .map(node => ({
              tag: node.tagName,
              className: node.className,
              left: node.scrollLeft,
              client: node.clientWidth,
              scroll: node.scrollWidth,
            })),
          horizontalEvents: window.fuelHorizontalEvents,
          pageScroll: { x: scrollX, y: scrollY },
          scrollWidth: list.scrollWidth,
        };
      });
      assert.ok(
        geometry.left >= 0 &&
          geometry.right <= width + 1 &&
          geometry.bottom <= height + 1,
        `${name}: editor is not viewport bounded`,
      );
      assert.ok(
        geometry.top >= geometry.map.y &&
          geometry.bottom <= geometry.map.y + geometry.map.height + 1,
        `${name}: editor must remain inside the map without covering the page heading or filters`,
      );
      assert.ok(
        geometry.documentWidth <= width + 1 &&
          geometry.scrollWidth <= geometry.contentWidth + 1,
        `${name}: horizontal overflow`,
      );
      assert.equal(
        geometry.mapCount,
        1,
        `${name}: editor does not create another map`,
      );
      assert.equal(
        geometry.mapRetained,
        true,
        `${name}: editor retains the mounted main map`,
      );
      for (const key of ['x', 'y', 'width', 'height']) {
        const beforeCoordinate = mapBefore[key] + (scrollBefore[key] ?? 0);
        const afterCoordinate =
          geometry.map[key] + (geometry.pageScroll[key] ?? 0);
        assert.ok(
          Math.abs(afterCoordinate - beforeCoordinate) <= 1,
          `${name}: editing must preserve map document bounds while allowing page scrolling: ${key}`,
        );
      }
      // The editor is the plan card in edit (the owner, September 26): it
      // stands where the plan card stood, at the card's width, and the map
      // stays under it so a station can still be picked there. The wide
      // two-column card, its bottom inset and the phone's Map tab are gone.
      assert.ok(
        Math.abs(geometry.left - planCard.left) <= 1 &&
          Math.abs(geometry.width - planCard.width) <= 1 &&
          Math.abs(geometry.top - planCard.top) <= 1,
        `${name}: the editor takes the plan card's place and width ` +
          JSON.stringify({ editor: geometry, planCard }),
      );
      assert.equal(
        geometry.mapReceivesPointer,
        true,
        `${name}: an uncovered part of the main map stays interactive`,
      );
      // One list between the totals and the footer, tall enough for each
      // complete control of the chosen stop.
      assert.ok(
        Math.abs(geometry.content.top - geometry.aboveBottom) <= 1 &&
          Math.abs(geometry.content.bottom - geometry.footerTop) <= 1,
        `${name}: the list uses all space between the totals and the footer`,
      );
      assert.ok(
        geometry.content.bottom - geometry.content.top >=
          geometry.requiredControlHeight,
        `${name}: the list fits each complete operating control`,
      );
      assert.ok(
        geometry.content.bottom <= geometry.footerTop + 1,
        `${name}: fixed footer remains outside the list scroller`,
      );
      await editor
        .getByRole('button', { name: 'Save plan', exact: true })
        .click();
      await editor.waitFor({ state: 'detached' });
      assert.equal(
        await page.evaluate(() => window.fuelFixture.focusedStation),
        null,
      );
      await page.locator('.fleet-map-inspector').waitFor({ state: 'visible' });
      assert.equal(
        await page.evaluate(() => window.fuelFixture.inspectionSuspended),
        false,
      );
      assert.equal(writes.length, 1);
      assert.equal(savedEdits[0].stationId, stations[2]);
      assert.equal(savedEdits[0].buyGallons, 30);
      await page.evaluate(
        ({ station, name }) => window.fuelFixture.selectStation(station, name),
        { station: stations[2], name: names[2] },
      );
      await editor
        .getByRole('button', { name: 'Remove selected fuel stop' })
        .click();
      await page.waitForFunction(
        () => document.querySelectorAll('.fuel-plan-editor__stop').length === 2,
      );
      await editor.getByRole('button', { name: 'Cancel', exact: true }).click();
      await editor.waitFor({ state: 'detached' });
      assert.equal(writes.length, 1, `${name}: Cancel submitted a write`);
      assert.equal(savedEdits.length, 3);
      await page.evaluate(
        ({ station, name }) => window.fuelFixture.selectStation(station, name),
        { station: stations[2], name: names[2] },
      );
      await editor.waitFor();
      await page.keyboard.press('Escape');
      await editor.waitFor({ state: 'detached' });
      assert.equal(writes.length, 1, `${name}: Escape submitted a write`);
      await page.evaluate(
        ({ station, name, wrongDispatch }) =>
          window.fuelFixture.selectStation(
            station,
            name,
            null,
            false,
            wrongDispatch,
          ),
        { station: stations[0], name: names[0], wrongDispatch: uuid(99) },
      );
      assert.equal(
        await editor.count(),
        0,
        `${name}: stale truck/dispatch callback opened editor`,
      );
      await assertFuelPlanCard(page, `${name}-stale-callback`);
      await (await fuelPlan(page)).click();
      assert.equal(
        writes.length,
        1,
        `${name}: opening the editor does not write`,
      );
      await editor
        .getByRole('button', {
          name: 'Calculate automatically',
          exact: true,
        })
        .click();
      await editor.waitFor({ state: 'detached' });
      assert.equal(writes.length, 2, `${name}: one click calculates directly`);
      assert.equal(writes[1].reset, true);
      assert.equal(writes[1].expectedCalculatedAt, savedToken);
      await assertFuelPlanCard(page, `${name}-calculated`);
      assert.equal(await (await fuelPlan(page)).isEnabled(), true);
      await page.getByRole('button', { name: 'Camera', exact: true }).click();
      const camera = page.getByRole('dialog', { name: 'Road-facing camera' });
      await camera.waitFor({ state: 'visible' });
      assert.equal(
        await page.locator('.fleet-map-inspector').isVisible(),
        false,
        `${name}: camera replaces the inspector`,
      );
      assert.equal(
        await page.locator('.fuel-plan-editor, .route-editor').count(),
        0,
      );
      assert.deepEqual(
        await page.locator('#fleet-map').boundingBox(),
        mapBefore,
      );
      await page.screenshot({ path: resolve(output, `${name}-camera.png`) });
      await page.keyboard.press('Escape');
      await camera.waitFor({ state: 'detached' });
      await page.locator('.fleet-map-inspector').waitFor({ state: 'visible' });
      assert.equal(
        await page.evaluate(() => window.fuelFixture.inspectionSuspended),
        false,
      );
      report.cases.push({
        name,
        geometry,
        initialTimeline,
        gestures,
        previews: editsSeen.length,
        writes: writes.length,
        quantityRedistribution: {
          before: [25, 100],
          after: [35, 90],
          previewRequests: 0,
        },
        passed: true,
      });
      await context.close();
    }
} catch (error) {
  report.errors.push(error.stack ?? String(error));
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
}
console.log(JSON.stringify(report, null, 2));
assert.equal(
  report.errors.length + report.unexpectedRequests.length,
  0,
  'Fuel editor smoke failed',
);
assert.equal(report.cases.length, process.env.FUEL_EDITOR_CASE ? 1 : 8);
