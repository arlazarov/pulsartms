import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { installReleaseArtifact } from './releaseArtifact.mjs';

// The staged Fleet Map's Send plan window over a stubbed map canvas: what a
// dispatcher sees of fuel stops already given to a driver. The page, its
// panels and styles are the compiled Client; the API answers are
// synthetic and in memory, and the map provider is not loaded (the canvas
// is a flat stub, so this says nothing about map drawing).

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'MAP_TEST_ARTIFACT_DIR must identify staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('fuel-send', process.env.FUEL_SEND_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const uuid = number =>
  `22222222-2222-2222-2222-${String(number).padStart(12, '0')}`;
const userId = uuid(1);
const at = '2026-09-24T09:00:00Z';
const trucks = {
  a: { id: uuid(2), unit: '54777', driver: 'Alex Fixture', dispatch: uuid(3) },
  b: { id: uuid(4), unit: '61200', driver: 'Blair Fixture', dispatch: uuid(5) },
};
const stations = {
  kept: { id: uuid(6), name: 'LOVES #706' },
  changed: { id: uuid(7), name: 'PILOT #412' },
  withdrawn: { id: uuid(8), name: 'LOVES #306' },
  fresh: { id: uuid(9), name: 'TA #221' },
};
const point = { latitude: 39.2, longitude: -79.1 };
const success = response => ({ success: true, response, errors: [] });

const mapStub = `export async function createFleetMap(element, _key, callbacks) {
  element.style.background = 'var(--ui-surface-muted)';
  window.sendFixture = {
    routes: [],
    selectTruck(id) { return callbacks.invokeMethodAsync('OnTruckSelected', id); },
  };
  return {setOptions(){},setTrucks(){},setStationsVisible(){},
    setTrafficVisible(){},setIfta(){},clearSelection(){},clearNextLoads(){},
    setNextLoadsVisible(){},clearNextLoadSelection(){},closeStationPopup(){},
    setStopEtas(){},setLoadReference(){},setDistanceUnit(){},setFollow(){},
    finishInitialView(){},setInspectorMode(){},clearMapInspection(){},
    setInspectionSuspended(){},setFuelEditorTruck(){},focusFuelStation(){},
    clearFuelStationFocus(){},setRouteBytes(bytes){window.sendFixture.routes.push(JSON.parse(new TextDecoder().decode(bytes)));return true;},
    setNextLoadsBytes(){},focusTruck(){return true;},
    dispose(){delete window.sendFixture;}};
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
    'Compiled Fleet Map page and Send plan window, synthetic in-memory API, stubbed map canvas. No database, provider, message or map drawing.',
  cases: [],
  errors: [],
  unexpectedRequests: [],
};
await mkdir(output, { recursive: true });

// What the server says per truck: its work, its plan's stops and their
// hand-over state, and what the Send plan window shows.
function fixture() {
  const state = {
    a: { revision: 3, withdrawn: true, delayMs: 0, reference: null },
    b: { revision: 1, withdrawn: false, delayMs: 0, reference: null },
    previews: [],
  };
  const stop = key => ({
    id: uuid(key === 'a' ? 20 : 21),
    sequence: 1,
    job: 'Delivery',
    name: key === 'a' ? 'Fort Mill receiving' : 'Greensboro receiving',
    city: key === 'a' ? 'Fort Mill' : 'Greensboro',
    address: '308 Springhill Farm Rd, Fort Mill, SC 29715, US',
    province: key === 'a' ? 'SC' : 'NC',
    country: 'US',
    scheduledDate: '2026-09-24',
    scheduledTime: '16:00:00',
    ...point,
  });
  const fuelStop = (station, number, sent) => ({
    stationId: station.id,
    beforeStopId: uuid(30 + number),
    number,
    dispatchId: null,
    name: station.name,
    point,
    address: '3499 Lee Jackson Hwy, Staunton, VA 24401, USA',
    arrivalGallons: 40,
    departureGallons: 140,
    buyGallons: 100,
    fillToTarget: false,
    milesAhead: number * 120,
    purchaseCostUsd: 400,
    yourPrice: 4.1,
    cashUsdPerGallon: 4.1,
    economicUsdPerGallon: 3.9,
    currency: 'USD',
    unit: 'US gal',
    sent,
  });
  // An acknowledged plan (the page sends the id and version it holds) is
  // answered with metadata only, as the server does: no points.
  const planning = (key, omitted = false) => {
    const truck = trucks[key];
    const stops =
      key === 'a'
        ? [
            fuelStop(stations.kept, 1, {
              sentAt: at,
              sentBy: 'Dispatcher',
              channel: 'manual',
              changed: false,
            }),
            fuelStop(stations.changed, 2, {
              sentAt: at,
              sentBy: 'Dispatcher',
              channel: 'manual',
              changed: true,
            }),
          ]
        : [fuelStop(stations.fresh, 1, null)];
    for (const each of stops) each.dispatchId = truck.dispatch;
    return {
      truckId: truck.id,
      dispatchId: truck.dispatch,
      loadNumber: key === 'a' ? 1375 : 1376,
      hos: null,
      state: {
        profile: { tankGallons: 211.3 },
        apiConfigured: false,
        fuelPercent: 46,
        fuelUpdatedAt: at,
        progress: {
          progressMiles: 120,
          remainingMiles: 500,
          remainingSeconds: 7200,
          position: point,
        },
        plan: {
          id: uuid(key === 'a' ? 40 : 41),
          dispatchId: truck.dispatch,
          executionLegId: null,
          assignmentRevision: state[key].revision,
          truckId: truck.id,
          version: 1,
          calculatedAt: at,
          originalPlannedMiles: 620,
          fromCurrentPosition: true,
          profile: {},
          stops: [stop(key)],
          tracking: {
            nextStopId: stop(key).id,
            passedStopIds: [],
            visitedStops: {},
            allStopsPassed: false,
          },
          route: {
            miles: 500,
            seconds: 7200,
            warnings: [],
            points: [],
            legs: [
              {
                miles: 500,
                seconds: 7200,
                points: omitted
                  ? []
                  : [point, { latitude: 35.1, longitude: -80.9 }],
              },
            ],
          },
          geometryOmitted: omitted,
          ...(state[key].reference
            ? {
                referenceSource: state[key].reference,
                referenceStops: [stop(key)],
                referenceRoute: {
                  miles: 620,
                  seconds: 9000,
                  warnings: [],
                  points: [],
                  legs: [
                    {
                      miles: 620,
                      seconds: 9000,
                      points: omitted
                        ? []
                        : [
                            { latitude: 38.9, longitude: -79.4 },
                            { latitude: 35.1, longitude: -80.9 },
                          ],
                    },
                  ],
                },
              }
            : {}),
          fuelPlan: {
            truckId: truck.id,
            calculatedAt: at,
            pricingDate: '2026-09-24',
            manuallyEdited: false,
            dispatchIds: [truck.dispatch],
            needsRefresh: false,
            purchaseGallons: 100 * stops.length,
            purchaseCostUsd: 400 * stops.length,
            arrivalGallons: 60,
            startingGallons: 97,
            remainingMiles: 500,
            stops,
          },
        },
      },
    };
  };
  const preview = key => {
    const truck = trucks[key];
    const lines =
      key === 'a'
        ? [
            {
              visitKey: 'kept',
              text: `1. ${stations.kept.name} - 100 gal`,
              sent: true,
              changed: false,
            },
            {
              visitKey: 'changed',
              text: `2. ${stations.changed.name} - fill`,
              sent: true,
              changed: true,
            },
          ]
        : [
            {
              visitKey: 'fresh',
              text: `1. ${stations.fresh.name} - 100 gal`,
              sent: false,
              changed: false,
            },
          ];
    return {
      truckId: truck.id,
      planCalculatedAt: at,
      executionLegId: null,
      assignmentRevision: state[key].revision,
      issueState: key === 'a' ? 'changed' : 'notSent',
      horizonEndsAt: null,
      critical: false,
      lines,
      message: lines.map(line => line.text).join('\n'),
      automaticSending: false,
      recipient: {
        driverId: uuid(key === 'a' ? 50 : 51),
        driverName: truck.driver,
        whatsAppPhone: null,
        state: 'noNumber',
        windowEndsAt: null,
      },
      lastMessage: null,
      withdrawn: state[key].withdrawn
        ? [
            {
              stationId: stations.withdrawn.id,
              beforeStopId: uuid(33),
              dispatchId: truck.dispatch,
              stationName: stations.withdrawn.name,
              sentAt: at,
              withdrawnAt: '2026-09-24T09:40:00Z',
            },
          ]
        : [],
    };
  };
  return { state, planning, preview };
}

const keyOf = id =>
  Object.keys(trucks).find(
    key => trucks[key].id === id || trucks[key].dispatch === id,
  );

async function contrast(locator) {
  return locator.evaluate(node => {
    const rgb = value =>
      value
        .match(/[\d.]+/g)
        .slice(0, 4)
        .map(Number);
    const lum = ([r, g, b]) =>
      [r, g, b]
        .map(v => v / 255)
        .map(v => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4))
        .reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0);
    let back = node;
    let background = getComputedStyle(back).backgroundColor;
    while (
      back.parentElement &&
      (background === 'transparent' || rgb(background)[3] === 0)
    ) {
      back = back.parentElement;
      background = getComputedStyle(back).backgroundColor;
    }
    const [a, b] = [
      lum(rgb(getComputedStyle(node).color)),
      lum(rgb(background)),
    ];
    return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
  });
}

async function withinViewport(page, locator, name) {
  const box = await locator.boundingBox();
  const width = page.viewportSize().width;
  assert.ok(box, `${name}: not rendered`);
  assert.ok(
    box.x >= 0 && box.x + box.width <= width + 0.5,
    `${name}: outside the viewport (${box.x}+${box.width} of ${width})`,
  );
  const clipped = await locator.evaluate(
    node => node.scrollWidth > node.clientWidth + 1,
  );
  assert.equal(clipped, false, `${name}: text clipped horizontally`);
}

const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
try {
  for (const [width, height] of [
    [1440, 900],
    [390, 844],
  ])
    for (const theme of ['light', 'dark']) {
      const name = `${width}-${theme}`;
      const { state, planning, preview } = fixture();
      const context = await browser.newContext({
        viewport: { width, height },
        colorScheme: theme,
        hasTouch: width < 768,
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
          if (document.documentElement)
            document.documentElement.dataset.theme = theme;
        },
        { userId, theme },
      );
      await installReleaseArtifact(context, artifact, origin);
      await context.route('**/*', async route => {
        const request = route.request(),
          url = new URL(request.url());
        if (url.origin !== origin) {
          report.unexpectedRequests.push(`${request.method()} ${url.href}`);
          return route.abort('blockedbyclient');
        }
        const path = url.pathname;
        if (path.startsWith('/api/')) {
          let value;
          const truckMatch = path.match(/^\/api\/fleet\/trucks\/([^/]+)\//);
          const loadMatch = path.match(/^\/api\/dispatch\/([^/]+)/);
          if (path === '/api/auth/me')
            value = {
              id: userId,
              name: 'Fixture Administrator',
              email: 'fixture@example.invalid',
              isAdmin: true,
            };
          else if (path === '/api/settings/appearance')
            value = success({ theme });
          else if (path === '/api/settings/dispatch')
            value = success({ loadNumberPrefix: 'AMF', revision: 1 });
          else if (path === '/api/settings/planning')
            value = success({
              preferences: { useIfta: true },
              revision: 1,
              updatedAt: null,
            });
          else if (path === '/api/fleet/locations') {
            const rows = Object.values(trucks).map(truck => ({
              truckId: truck.id,
              unitNumber: truck.unit,
              driverName: truck.driver,
              trailerNumber: 'GG1030',
              ...point,
              speed: 0,
              heading: 90,
              engineState: 'Off',
              fuelPercent: 46,
              updatedAt: at,
            }));
            value = success({ trucks: rows, points: rows });
          } else if (path === '/api/fleet/planning/previews')
            value = success([]);
          else if (path === '/api/fleet/hos') value = success({});
          // The layout's own reads, answered as the UI smoke answers them.
          else if (path === '/api/driver-groups')
            value = success({ selected: null, groups: [] });
          else if (path === '/api/messaging/unread')
            value = success({ conversations: 0, more: false, newest: 0 });
          else if (path === '/api/messaging/changes') {
            // A new mailbox reads everything; a known one, with nothing
            // changing, answers empty after a (shortened) wait.
            const mailbox = url.searchParams.get('mailbox');
            if (mailbox) await new Promise(done => setTimeout(done, 2000));
            return route
              .fulfill({
                status: 200,
                json: success({
                  mailbox: mailbox ?? '00000000-0000-4000-8000-00000000c4a9',
                  resync: !mailbox,
                  conversations: [],
                }),
              })
              .catch(() => {});
          } else if (
            truckMatch &&
            /\/(planning|planning\/preview)$/.test(path) &&
            keyOf(truckMatch[1])
          ) {
            const key = keyOf(truckMatch[1]);
            const plan = planning(key).state.plan;
            value = success(
              planning(
                key,
                url.searchParams.get('knownPlanId') === plan.id &&
                  url.searchParams.get('knownVersion') === String(plan.version),
              ),
            );
          } else if (truckMatch && path.endsWith('/weather'))
            value = success(null);
          else if (
            loadMatch &&
            path.endsWith('/planning/fuel/issue') &&
            keyOf(loadMatch[1])
          ) {
            const key = keyOf(loadMatch[1]);
            state.previews.push(key);
            if (state[key].delayMs)
              await new Promise(done => setTimeout(done, state[key].delayMs));
            value = success(preview(key));
          } else if (loadMatch && path === `/api/dispatch/${loadMatch[1]}`) {
            const key = keyOf(loadMatch[1]);
            if (key)
              value = success({
                id: trucks[key].dispatch,
                truckId: trucks[key].id,
                loadNumber: key === 'a' ? 1375 : 1376,
                status: 'in_transit',
                stops: planning(key).state.plan.stops,
              });
          }
          if (value === undefined) {
            report.unexpectedRequests.push(`${request.method()} ${path}`);
            return route.abort('blockedbyclient');
          }
          return route.fulfill({ status: 200, json: value });
        }
        if (!['GET', 'HEAD'].includes(request.method())) {
          report.unexpectedRequests.push(`${request.method()} ${path}`);
          return route.abort('blockedbyclient');
        }
        if (/\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(path))
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
      const panel = page.getByRole('region', { name: 'Send fuel plan' });
      // Sending lives in the Fuel panel under the card's actions.
      const sendButton = page.locator('.fleet-map-fuel-panel__send');
      // The truck card opens closed; its actions show once a dispatcher
      // opens it, with the same click.
      const openCard = async () => {
        const toggle = page.locator('.fleet-map-mobile-summary__toggle');
        await toggle.waitFor();
        if ((await toggle.getAttribute('aria-expanded')) === 'false')
          await toggle.click();
        const fuel = page.locator(
          'button[aria-controls="fleet-map-fuel-panel"]',
        );
        await fuel.waitFor();
        if ((await fuel.getAttribute('aria-expanded')) !== 'true')
          await fuel.click();
      };
      const openPanel = async () => {
        await openCard();
        await sendButton.first().waitFor({ state: 'visible' });
        await page.waitForFunction(
          () =>
            document.querySelector('.fleet-map-fuel-panel__send')?.disabled ===
            false,
        );
        await sendButton.first().click();
        await panel.waitFor({ state: 'visible' });
        await panel.locator('.fuel-send-plan__state').waitFor();
      };
      const select = async key => {
        await page.waitForFunction(() => window.sendFixture);
        await page.evaluate(
          id => window.sendFixture.selectTruck(id),
          trucks[key].id,
        );
      };
      const shot = async step =>
        page.screenshot({
          path: resolve(output, `${name}-${step}.png`),
          fullPage: false,
        });

      await page.goto(`${origin}/fleet/map?truckId=${trucks.a.id}`);

      // 1. The truck whose driver was given stops that no longer hold.
      await openPanel();
      await assert.doesNotReject(
        panel.getByText(`Truck ${trucks.a.unit}`).waitFor(),
        `${name}: panel names the truck`,
      );
      const withdrawn = panel.locator('.fuel-send-plan__withdrawn');
      await withdrawn.waitFor({ state: 'visible' });
      assert.equal(await withdrawn.getAttribute('role'), 'alert');
      const alertText = (await withdrawn.innerText()).replace(/\s+/g, ' ');
      assert.match(
        alertText,
        /Given to the driver, no longer in the plan: LOVES #306\. Review the plan and send it again\./,
        `${name}: withdrawn alert text`,
      );
      await withdrawn.scrollIntoViewIfNeeded();
      await withinViewport(page, withdrawn, `${name}: withdrawn alert`);
      const alertContrast = await contrast(withdrawn);
      assert.ok(
        alertContrast >= 4.5,
        `${name}: withdrawn alert contrast ${alertContrast.toFixed(2)}`,
      );
      const labels = panel.locator('.fuel-send-plan__lines li');
      assert.equal(await labels.count(), 2);
      assert.equal(
        (
          await labels.nth(0).locator('.fleet-fuel-visit__sent').innerText()
        ).trim(),
        'Sent',
      );
      const changed = labels.nth(1).locator('.fleet-fuel-visit__sent--changed');
      assert.equal((await changed.innerText()).trim(), 'Changed since sent');
      const changedContrast = await contrast(changed);
      assert.ok(
        changedContrast >= 4.5,
        `${name}: changed label contrast ${changedContrast.toFixed(2)}`,
      );
      assert.ok(
        (await page.evaluate(() => document.documentElement.scrollWidth)) <=
          width,
        `${name}: page scrolls sideways`,
      );
      // The toolbar with a truck selected: the search keeps a usable width
      // and the driver group's name is not cut off.
      const toolbar = await page.evaluate(() => {
        const input = document.querySelector('#fleet-truck-search');
        const select = document.querySelector('#fleet-map-driver-group');
        const style = getComputedStyle(select);
        const context = document.createElement('canvas').getContext('2d');
        context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
        const text = select.selectedOptions[0]?.text ?? '';
        return {
          search: input.getBoundingClientRect().width,
          select: select.clientWidth,
          needed:
            context.measureText(text).width +
            parseFloat(style.paddingLeft) +
            parseFloat(style.paddingRight),
        };
      });
      assert.ok(
        toolbar.search >= Math.min(200, width * 0.45),
        `${name}: search squeezed to ${toolbar.search}px`,
      );
      assert.ok(
        toolbar.select >= toolbar.needed,
        `${name}: driver group name cut off (${toolbar.select} < ${toolbar.needed})`,
      );
      await shot('1-withdrawn');

      // 2. Another truck selected with the window open: the window belongs
      // to the work it was opened for, so it closes, and B's opens clean.
      await select('b');
      await panel.waitFor({ state: 'detached', timeout: 5000 });
      await openPanel();
      await panel.getByText(`Truck ${trucks.b.unit}`).waitFor();
      assert.equal(
        await panel.locator('.fuel-send-plan__withdrawn').count(),
        0,
      );
      assert.equal(await panel.getByText(stations.withdrawn.name).count(), 0);
      assert.equal(await panel.locator('.fleet-fuel-visit__sent').count(), 0);
      await shot('2-other-truck');
      await panel.getByRole('button', { name: 'Close send fuel plan' }).click();
      await panel.waitFor({ state: 'detached' });

      // 3. A's answer arrives late, after B was selected: it must not
      // reach B's screen.
      await select('a');
      await page.waitForFunction(
        unit => document.body.innerText.includes(unit),
        trucks.a.unit,
      );
      state.a.delayMs = 1500;
      await openCard();
      await sendButton.first().waitFor({ state: 'visible' });
      await page.waitForFunction(
        () =>
          document.querySelector('.fleet-map-fuel-panel__send')?.disabled ===
          false,
      );
      await sendButton.first().click();
      await select('b');
      await page.waitForTimeout(2500);
      assert.equal(
        await page.locator('.fuel-send-plan__withdrawn').count(),
        0,
        `${name}: a late answer for truck A reached the screen`,
      );
      assert.equal(await page.getByText(`Truck ${trucks.a.unit}`).count(), 0);
      state.a.delayMs = 0;
      await shot('3-late-answer');

      // 4. A's assignment changes under an open window, and the stop
      // given for the old one is no longer reported: the window closes
      // and a new one reads the new answer.
      await select('a');
      await openPanel();
      await withdrawn.waitFor({ state: 'visible' });
      const reads = state.previews.length;
      state.a.revision = 4;
      state.a.withdrawn = false;
      // The page learns of changed work on its ten-second poll.
      await panel.waitFor({ state: 'detached', timeout: 15000 });
      await openPanel();
      assert.ok(
        state.previews.length > reads,
        `${name}: preview not read again`,
      );
      assert.equal(
        await panel.locator('.fuel-send-plan__withdrawn').count(),
        0,
      );
      await shot('4-new-assignment');

      // 5. An already open map: truck A's plan is given its base road as
      // reference at the same version. The page's own poll holds the plan's
      // id and version, so the answer is metadata that names the reference;
      // the page reads the geometry once and the map module receives it.
      // The version the fuel plan follows stays 1.
      await panel.getByRole('button', { name: 'Close send fuel plan' }).click();
      await panel.waitFor({ state: 'detached' });
      // Settled first: closing the window republishes the map in full, so
      // the reference is given only after an ordinary poll has sent the map
      // metadata alone - what an open map receives while nothing changes.
      await page.evaluate(() => (window.sendFixture.routes.length = 0));
      await page.waitForFunction(
        () => window.sendFixture.routes.some(route => route?.geometryOmitted),
        null,
        { timeout: 25000 },
      );
      await page.evaluate(() => (window.sendFixture.routes.length = 0));
      state.a.reference = 'road-a';
      await page.waitForFunction(
        () =>
          window.sendFixture.routes.some(
            route =>
              route?.referenceRoute?.legs?.[0]?.points?.length === 2 &&
              route.geometryOmitted === false,
          ),
        null,
        { timeout: 25000 },
      );
      const drawn = await page.evaluate(() =>
        window.sendFixture.routes.find(
          route => route?.referenceRoute?.legs?.[0]?.points?.length === 2,
        ),
      );
      assert.equal(drawn.version, 1, `${name}: reference changed the version`);
      assert.equal(drawn.fuelPlan.stops.length, 2);
      await shot('5-reference');
      report.cases.push({
        name,
        alertContrast: Number(alertContrast.toFixed(2)),
        changedContrast: Number(changedContrast.toFixed(2)),
        previews: state.previews,
      });
      await context.close();
    }
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
}
assert.deepEqual(report.errors, [], 'page errors');
assert.deepEqual(report.unexpectedRequests, [], 'unexpected requests');
console.log(`Fuel send smoke: ${report.cases.length} cases. ${output}`);
