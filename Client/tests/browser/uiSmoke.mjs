import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';
import { chromium } from 'playwright';
import { installReleaseArtifact } from './releaseArtifact.mjs';
import {
  checkWorkspaceLoads,
  returnToDispatch,
  workspaceFinancials,
  workspacePage,
  workspaceReadModel,
  workspaceStop,
} from './uiSmokeWorkspace.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'MAP_TEST_ARTIFACT_DIR must identify a verified staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui', process.env.UI_TEST_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const id = '11111111-1111-1111-1111-111111111111';
const truckId = '22222222-2222-2222-2222-222222222222';
const currentLoadId = '33333333-3333-3333-3333-333333333333';
const futureLoadId = '44444444-4444-4444-4444-444444444444';
const stopIds = [1, 2, 3, 4, 5].map(
  number => `55555555-5555-5555-5555-${String(number).padStart(12, '0')}`,
);
const fixtureDay = new Date();
const dateOnly = days =>
  new Date(
    Date.UTC(
      fixtureDay.getUTCFullYear(),
      fixtureDay.getUTCMonth(),
      fixtureDay.getUTCDate() + days,
    ),
  )
    .toISOString()
    .slice(0, 10);
const stop = (index, sequence, job, name, city, day, hour) => ({
  id: stopIds[index],
  sequence,
  job,
  name,
  city,
  province: 'ON',
  country: 'Canada',
  scheduledDate: dateOnly(day),
  scheduledTime: `${hour}:00:00`,
});
const stops = [
  stop(0, 1, 'Pickup', 'North distribution centre', 'Windsor', 0, '08'),
  stop(
    1,
    2,
    'Delivery',
    'Intermediate consolidation warehouse',
    'London',
    0,
    '10',
  ),
  stop(2, 3, 'Delivery', 'Lakeshore receiving facility', 'Toronto', 0, '16'),
  stop(3, 1, 'Pickup', 'East logistics terminal', 'Kingston', 1, '08'),
  stop(4, 2, 'Delivery', 'Capital distribution centre', 'Ottawa', 1, '18'),
];
stops[0].pickedUpAt = `${dateOnly(0)}T08:15:00Z`;
stops[2].isWindow = true;
stops[2].scheduledTime2 = '18:00:00';
for (const [index, value] of stops.entries()) {
  const load = index < 3 ? 1441 : 1442;
  value.notes = `Shipper appointment confirmation number: PU${load}. Receiver appointment confirmation number: DL${load}.`;
  value.stopNo = `${value.job === 'Pickup' ? 'PU' : 'DEL'}-REF${load}`;
}
Object.assign(stops[0], {
  commodity: 'Refrigerated produce',
  weight: 43063,
  weightUnit: 'lbs',
  pallets: 26,
});
stops[0].notes +=
  '\nReport detention within 48 hours. Food-grade trailer required. Have load locks ready. ' +
  'Check the seal before departure and follow the receiving facility instructions. ' +
  'Verify the shipment temperature against the bill of lading before loading.';
const stopForecast = (dispatchId, stop, time, lateMinutes) => ({
  dispatchId,
  stopId: stop.id,
  arrival: `${stop.scheduledDate}T${time}:00-04:00`,
  timeZoneId: 'America/Toronto',
  appointment: `${stop.scheduledDate}T${stop.scheduledTime}-04:00`,
  lateMinutes,
  drivingMinutes: 120,
  restMinutes: 0,
});
const cycleForecast = remainingMinutes => ({
  remainingMinutes,
  nextRecapAt: `${dateOnly(4)}T00:00:00-04:00`,
  nextRecapMinutes: 798,
  homeTimeZoneId: 'America/New_York',
  recapVerified: true,
});
const cycleAtCalculation = () => ({
  ...cycleForecast(1400),
  nextRecapAt: `${dateOnly(2)}T00:00:00-04:00`,
  nextRecapMinutes: 185,
});
const recapLabel = new Date(dateOnly(2)).toLocaleDateString('en-US', {
  month: 'short',
  day: 'numeric',
  timeZone: 'UTC',
});
const forecast = values => ({
  calculatedAt: new Date().toISOString(),
  validUntil: new Date(Date.now() + 120_000).toISOString(),
  stops: values,
  assumptions: [],
  unavailableReason: null,
  cycleAtCalculation: cycleAtCalculation(),
});
const dispatches = () => [
  {
    id: currentLoadId,
    truckId,
    loadNumber: 1441,
    orderNumber: 'CURRENT-ORD-1441',
    status: 'in_transit',
    customerName: 'Fixture Current Customer',
    truckNumber: '11006',
    driverName: 'Fixture Driver',
    trailerNumber: 'TR-100',
    loadedMiles: 450,
    emptyMiles: 50,
    totalMiles: 500,
    price: 2000,
    currency: 'CAD',
    loadedRatePerMile: 4.44,
    totalRatePerMile: 4,
    emptyMilesStatus: 'ready',
    stops: stops.slice(0, 3),
    eta: forecast([
      {
        ...stopForecast(currentLoadId, stops[1], '10:25', 25),
        drivingMinutes: 135,
        restMinutes: 600,
        cycleAfterDeparture: cycleForecast(1255),
      },
      {
        ...stopForecast(currentLoadId, stops[2], '16:00', 0),
        cycleAfterDeparture: cycleForecast(1100),
      },
    ]),
  },
  {
    id: futureLoadId,
    truckId,
    loadNumber: 1442,
    orderNumber: 'FUTURE-ORD-1442',
    status: 'planned',
    customerName: 'Fixture Future Customer',
    truckNumber: '11006',
    driverName: 'Fixture Driver',
    trailerNumber: 'TR-100',
    loadedMiles: 300,
    emptyMiles: 40,
    totalMiles: 340,
    price: 1500,
    currency: 'CAD',
    loadedRatePerMile: 5,
    totalRatePerMile: 4.41,
    emptyMilesStatus: 'ready',
    stops: stops.slice(3),
    eta: forecast([
      {
        ...stopForecast(futureLoadId, stops[3], '08:00', 0),
        cycleAfterDeparture: cycleForecast(1005),
      },
      {
        ...stopForecast(futureLoadId, stops[4], '19:05', 65),
        drivingMinutes: 780,
        restMinutes: 1800,
        cycleAfterDeparture: cycleForecast(750),
      },
    ]),
  },
];
const completedDispatches = () =>
  dispatches().map(load => ({
    ...load,
    status: 'completed',
    eta: null,
    stops: load.stops.map(value => ({
      ...value,
      departedAt: `${dateOnly(-1)}T18:00:00Z`,
    })),
  }));
const repeatedDispatches = () => {
  const loads = dispatches();
  const webster = {
    job: 'Pickup',
    name: 'FAIRLIFE WEBSTER',
    address: '1886 Tebor Rd',
    city: 'Webster',
    province: 'NY',
    country: 'US',
    zipCode: '14580',
    scheduledDate: dateOnly(0),
  };
  loads[0].stops = [
    {
      ...webster,
      id: stopIds[0],
      sequence: 1,
      scheduledTime: '02:00:00',
      pickedUpAt: `${dateOnly(0)}T06:15:00Z`,
    },
    {
      id: stopIds[1],
      sequence: 2,
      job: 'Pickup',
      name: 'Target DC #3802',
      address: '1730 NY-5S',
      city: 'Amsterdam',
      province: 'NY',
      country: 'US',
      zipCode: '12010',
      scheduledDate: dateOnly(0),
      scheduledTime: '11:00:00',
    },
    {
      ...webster,
      id: '55555555-5555-5555-5555-000000000006',
      sequence: 3,
      scheduledTime: '13:00:00',
    },
    {
      ...webster,
      id: '55555555-5555-5555-5555-000000000007',
      sequence: 4,
      scheduledTime: '14:00:00',
    },
    {
      id: stopIds[2],
      sequence: 5,
      job: 'Delivery',
      name: 'COSTCO SE DEPOT 174',
      address: '13077 SW Anthony F. Sansone Sr. Blvd',
      city: 'Port St. Lucie',
      province: 'FL',
      country: 'US',
      zipCode: '34987',
      scheduledDate: dateOnly(3),
      scheduledTime: '05:00:00',
    },
  ];
  loads[0].eta = null;
  return loads;
};
const planning = () => ({
  truckId,
  dispatchId: currentLoadId,
  loadNumber: 1441,
  message: null,
  state: {
    profile: {},
    apiConfigured: false,
    fuelPercent: 28,
    fuelUpdatedAt: new Date().toISOString(),
    plan: {
      id: '66666666-6666-6666-6666-666666666666',
      truckId,
      dispatchId: currentLoadId,
      version: 1,
      originalPlannedMiles: 2509,
      fromCurrentPosition: false,
      stops: stops.slice(0, 3).map(value => ({
        ...value,
        address: `${value.city}, ON`,
        point: { latitude: 42, longitude: -80 },
      })),
      route: {
        legs: [
          { miles: 2509, seconds: 140000, points: [] },
          { miles: 0, seconds: 0, points: [] },
        ],
        warnings: [],
      },
      tracking: {
        nextStopId: stopIds[1],
        nextStopLabel: 'London, ON',
        passedStopIds: [stopIds[0]],
        allStopsPassed: false,
      },
    },
    progress: {
      progressMiles: 2465,
      remainingMiles: 44,
      remainingSeconds: 2600,
      offRoute: false,
      locationStale: false,
    },
    eta: forecast(dispatches().flatMap(load => load.eta.stops)),
  },
});
const success = response => ({ success: true, response, errors: [] });
const paginated = items => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount: items.length,
  totalPages: 1,
  hasPreviousPage: false,
  hasNextPage: false,
});
const truck = {
  truckId,
  unitNumber: '11006',
  driverName: 'Fixture Driver',
  trailerNumber: 'TR-100',
  latitude: 41.8,
  longitude: -87.6,
  speed: 65,
  heading: 0,
  updatedAt: new Date().toISOString(),
  engineState: 'Running',
};
const fixtures = new Map([
  [
    '/api/auth/me',
    {
      id,
      name: 'Fixture Administrator',
      email: 'fixture@example.invalid',
      isAdmin: true,
    },
  ],
  [
    '/api/users',
    success(
      paginated([
        {
          id,
          name: 'Fixture Dispatcher',
          email: 'fixture@example.invalid',
          isActive: true,
          role: 'Dispatch',
        },
      ]),
    ),
  ],
  [
    '/api/settings/planning',
    success({
      preferences: {
        useIfta: true,
        maxDetourMinutes: 15,
        reserveGallons: 25,
        fillPercent: 100,
        stopCostUsd: 20,
        driverHourlyCostUsd: 35,
      },
      revision: 1,
      updatedAt: null,
    }),
  ],
  [
    '/api/settings/dispatch',
    success({ loadNumberPrefix: 'AMF', revision: 1, updatedAt: null }),
  ],
  [
    '/api/settings/mileage-policy',
    success({
      revision: 1,
      yardReturn: 'unallocated',
      home: 'unallocated',
      maintenance: 'unallocated',
      reposition: 'unallocated',
      updatedAt: null,
    }),
  ],
  [
    '/api/settings/integrations',
    success(
      ['torqueai', 'samsara', 'google-email'].map(provider => ({
        provider,
        configured: true,
        usesSavedSettings: false,
        canRestoreDeployment: false,
        revision: 0,
        updatedAt: null,
        fields: (provider === 'google-email'
          ? ['clientId', 'clientSecret', 'refreshToken']
          : ['apiKey']
        ).map(name => ({ name, configured: true })),
      })),
    ),
  ],
  [
    '/api/dispatch/board',
    () =>
      success(
        paginated([
          {
            key: truckId,
            truckId,
            truckNumber: '11006',
            driverName: truck.driverName,
            trailerNumber: truck.trailerNumber,
            speed: 65,
            engineState: 'Running',
            currentCycle: {
              calculatedAt: new Date().toISOString(),
              validUntil: new Date(Date.now() + 120_000).toISOString(),
              cycle: cycleAtCalculation(),
            },
            dispatches: dispatches(),
          },
        ]),
      ),
  ],
  ['/api/dispatch', () => success(paginated(completedDispatches()))],
  [
    '/api/dispatch/board/telemetry',
    success([
      {
        truckId,
        speed: truck.speed,
        engineState: truck.engineState,
        trailerNumber: truck.trailerNumber,
      },
    ]),
  ],
  ['/api/dispatch/board/enrichment', success([])],
  ['/api/fleet/hos', success({})],
  ['/api/fleet/locations', success({ trucks: [truck], points: [truck] })],
  ['/api/fleet/planning/previews', success([])],
]);
const mapStub = `export async function createFleetMap(element) {
  element.dataset.uiFixture = 'map-provider-not-tested';
  return {setOptions(){},setTrucks(){},setStationsVisible(){},setTrafficVisible(){},
    setIfta(){},clearSelection(){},clearNextLoads(){},setNextLoads(){},setNextLoadsVisible(){},setDistanceUnit(){},
    focusTruck(){return false;},dispose(){delete element.dataset.uiFixture;}};
}`;
const stubIntegrity = `sha256-${createHash('sha256').update(mapStub).digest('base64')}`;
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g,
  (_all, open, json, close) => {
    const map = JSON.parse(json);
    for (const name of Object.keys(map.integrity ?? {})) {
      if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(name))
        map.integrity[name] = stubIntegrity;
    }
    return open + JSON.stringify(map) + close;
  },
);
let browser;
const report = {
  artifact,
  scope:
    'Actual staged Blazor UI with deterministic APIs; map provider/GPU module stubbed; no network or business writes.',
  cases: [],
  failures: [],
  unexpectedRequests: [],
  browserErrors: [],
};
const check = (condition, message) => {
  if (!condition) report.failures.push(message);
};
async function dispatchTop(page) {
  return page.evaluate(() =>
    [
      '.dispatch-page > .page-header',
      '.dispatch-board__filters',
      '#dispatch-search',
      '.dispatch-board__body',
    ].map(selector => {
      const element = document.querySelector(selector);
      if (!element) return null;
      const box = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return {
        x: box.x + window.scrollX,
        y: box.y + window.scrollY,
        width: box.width,
        height: box.height,
        frame: [
          'paddingTop',
          'paddingRight',
          'paddingBottom',
          'paddingLeft',
          'borderTopWidth',
          'borderRightWidth',
          'borderBottomWidth',
          'borderLeftWidth',
          'borderTopLeftRadius',
          'backgroundColor',
        ].map(key => style[key]),
      };
    }),
  );
}
async function checkDispatchTop(page, baseline, name) {
  const current = await dispatchTop(page);
  check(
    current.every(
      (box, index) =>
        box &&
        baseline[index] &&
        (index === 3
          ? ['x', 'y', 'width']
          : ['x', 'y', 'width', 'height']
        ).every(key => Math.abs(box[key] - baseline[index][key]) <= 1) &&
        JSON.stringify(box.frame) === JSON.stringify(baseline[index].frame),
    ),
    name +
      ' keeps the shared heading, toolbar, search and body frame in the same place',
  );
}
async function checkDispatchLoading(page, baseline, name) {
  const status = page.locator('.dispatch-board__body > [role="status"]');
  await status.waitFor();
  check(
    (await page.locator('.dispatch-truck__available--next').count()) === 0,
    name + ' loading does not imply a missing next assignment',
  );
  if (baseline) await checkDispatchTop(page, baseline, name + ' while loading');
  const placement = await status.evaluate(element => {
    const body = element.parentElement,
      parentBox = body.getBoundingClientRect(),
      box = element.getBoundingClientRect();
    const style = getComputedStyle(body);
    return {
      left: box.left - parentBox.left,
      top: box.top - parentBox.top,
      expectedLeft:
        parseFloat(style.paddingLeft) + parseFloat(style.borderLeftWidth),
      expectedTop:
        parseFloat(style.paddingTop) + parseFloat(style.borderTopWidth),
    };
  });
  check(
    Math.abs(placement.left - placement.expectedLeft) <= 1 &&
      Math.abs(placement.top - placement.expectedTop) <= 1,
    name +
      ' loading status shares the body origin without view-specific inner spacing',
  );
  return dispatchTop(page);
}
async function checkCardAlignment(page, name) {
  const lanes = await page
    .locator('.dispatch-truck__loads')
    .evaluateAll(elements =>
      elements.map(lane => ({
        horizontal: getComputedStyle(lane).gridAutoFlow === 'column',
        cards: [...lane.querySelectorAll(':scope > .dispatch-load')].map(
          card => ({
            height: card.getBoundingClientRect().height,
            bottom: card.getBoundingClientRect().bottom,
            footerBottom: card
              .querySelector('.dispatch-load__footer')
              .getBoundingClientRect().bottom,
          }),
        ),
      })),
    );
  for (const lane of lanes.filter(
    value => value.horizontal && value.cards.length > 1,
  )) {
    for (const key of ['height', 'bottom', 'footerBottom']) {
      const values = lane.cards.map(card => card[key]);
      check(
        Math.max(...values) - Math.min(...values) <= 1,
        name + ` horizontal cards align ${key} with different stop counts`,
      );
    }
  }
}
async function checkRepeatedVisits(page, name, screenshots) {
  const expected = repeatedDispatches()[0].stops;
  const card = page.locator('.dispatch-load').first();
  await card.locator('.dispatch-load__stop').nth(4).waitFor();
  const items = card.locator('.dispatch-load__stop');
  check(
    (await items.count()) === 5,
    name + ' expanded load retains all five stops',
  );
  check(
    (await card.locator('.dispatch-load__facility').count()) === 0,
    name +
      ' compact repeated visits do not duplicate the facility beneath a present street',
  );
  const stopColumns = await card.evaluate(element => {
    const font = parseFloat(
      getComputedStyle(document.documentElement).fontSize,
    );
    const style = getComputedStyle(element);
    const width =
      element.clientWidth -
      parseFloat(style.paddingLeft) -
      parseFloat(style.paddingRight);
    return {
      wide:
        width >= 32 * font &&
        !(width >= 48 * font && element.querySelector('.has-history')),
      stops: [
        ...element.querySelectorAll(
          '.dispatch-load__stop:not(.dispatch-load__stop--completed)',
        ),
      ].map(stop => {
        const heading = stop
          .querySelector('.dispatch-load__stop-heading')
          .getBoundingClientRect();
        const location = stop
          .querySelector('.dispatch-load__location')
          .getBoundingClientRect();
        const times = stop
          .querySelector('.dispatch-load__stop-times')
          .getBoundingClientRect();
        return {
          beside:
            times.left >= location.right &&
            Math.abs(times.top - heading.top) <= 1,
          below: times.top >= location.bottom - 1,
        };
      }),
    };
  });
  check(
    stopColumns.stops.every(stop =>
      stopColumns.wide ? stop.beside : stop.below,
    ),
    name +
      ' stop facts use compact columns only when the card has room at the current font size',
  );
  check(
    /5 stops.*4 pickups.*1 delivery/s.test(await card.innerText()),
    name + ' expanded load shows the pickup/delivery count',
  );
  for (const [index, item] of (await items.all()).entries()) {
    check(
      (await item.getAttribute('data-stop-id')) === expected[index].id,
      name + ' repeated site keeps ordered distinct identities',
    );
  }
  for (const [position, visit, time] of [
    [0, 1, '02:00 AM'],
    [2, 2, '01:00 PM'],
    [3, 3, '02:00 PM'],
  ]) {
    const text = await items.nth(position).innerText();
    const historyTitle = (await items
      .nth(position)
      .locator('.dispatch-load__history-stop')
      .count())
      ? await items
          .nth(position)
          .locator('.dispatch-load__history-stop')
          .getAttribute('title')
      : '';
    check(
      (text + historyTitle).includes(`Visit ${visit} of 3`) &&
        text.includes(time),
      name + ` repeated visit ${visit} retains its own appointment`,
    );
  }
  check(
    (await items.first().locator('[aria-label="Completed"]').count()) === 1,
    name + ' completed first visit remains in the timeline',
  );
  await checkCardAlignment(page, name + ' five-stop');
  for (const item of await items.all()) {
    const layout = await item.evaluate(element => {
      const row = element.getBoundingClientRect();
      return [
        ...element.querySelectorAll(
          'span:not(.dispatch-load__stop-number), strong',
        ),
      ]
        .filter(child => child.getClientRects().length)
        .every(child => {
          const box = child.getBoundingClientRect();
          return box.left >= row.left - 1 && box.right <= row.right + 1;
        });
    });
    check(layout, name + ' five-stop labels fit their timeline row');
  }
  await card.scrollIntoViewIfNeeded();
  const cardImage = resolve(output, `${name}-repeated-visits.png`);
  await page.screenshot({ path: cardImage, fullPage: true });
  screenshots.push(cardImage);
  await card.locator('.dispatch-load__details').click();
  const workspace = await workspacePage(page);
  const details = workspace.locator('.stop-workspace__stop');
  check(
    (await details.count()) === 5,
    name + ' Details preserves all repeated stops',
  );
  for (const [index, item] of (await details.all()).entries()) {
    check(
      (await item.getAttribute('data-stop-id')) === expected[index].id,
      name + ' Details keeps the same stop order as Cards',
    );
  }
  const mobilePanels = workspace.locator('.stop-workspace__mobile-tabs');
  if (await mobilePanels.isVisible())
    await mobilePanels
      .getByRole('button', { name: 'Stops', exact: true })
      .click();
  await details.nth(3).scrollIntoViewIfNeeded();
  check(
    (await details.nth(3).getAttribute('data-stop-id')) === expected[3].id &&
      !(await details.nth(3).innerText()).includes('Visit 3 of 3'),
    name +
      ' third visit retains its identity without the removed visit counter',
  );
  const workspaceImage = resolve(output, `${name}-repeated-visits-details.png`);
  await page.screenshot({ path: workspaceImage, fullPage: true });
  screenshots.push(workspaceImage);
  const selectedEditor = await workspaceStop(page, expected[2].id);
  check(
    await selectedEditor.locator('.stop-completion').isVisible(),
    name + ' selected stop completion is available directly in Overview',
  );
  const editor = workspace.locator('.stop-completion');
  await editor
    .getByRole('button', { name: 'Mark completed', exact: true })
    .click();
  await editor.locator('input[type=text]').fill('not a time');
  await editor
    .getByRole('button', { name: 'Confirm completed', exact: true })
    .click();
  check(
    (await editor.getByRole('alert').innerText()).includes('02:00 PM'),
    name + ' invalid actual time does not write',
  );
  const fits = await editor.evaluate(element => {
    const box = element.getBoundingClientRect();
    return [...element.querySelectorAll('input, button')].every(child => {
      const rect = child.getBoundingClientRect();
      return rect.left >= box.left - 1 && rect.right <= box.right + 1;
    });
  });
  check(
    fits,
    name + ' manual completion controls fit at the active width and font scale',
  );
  await editor.scrollIntoViewIfNeeded();
  const completionImage = resolve(output, `${name}-manual-completion.png`);
  await page.screenshot({ path: completionImage, fullPage: true });
  screenshots.push(completionImage);
  await editor.getByRole('button', { name: 'Cancel', exact: true }).click();
  check(
    (await editor.locator('input').count()) === 0,
    name + ' cancelling completion discards the form',
  );
  await returnToDispatch(page);
  const table = page.getByRole('button', { name: 'Table', exact: true });
  if (await table.isVisible()) {
    await table.click();
    const row = page.locator('.dispatch-table tbody tr').first();
    await row.locator('.dispatch-table__stop-entry').nth(1).waitFor();
    check(
      (await row.locator('.dispatch-table__stop-entry').count()) === 2,
      name + ' Table bounds each group to one summary',
    );
    check(
      (await row.locator('.dispatch-table__stops-summary').innerText()) ===
        '4 pickups · 1 completed',
      name + ' Table keeps pickup and completion counts',
    );
    check(
      (
        await row.locator(`[data-stop-id="${expected[1].id}"]`).innerText()
      ).includes('Stop 2'),
      name +
        ' Table shows the next unfinished pickup with its original position',
    );
    const density = await row.evaluate(element => ({
      height: element.getBoundingClientRect().height,
      font: parseFloat(getComputedStyle(document.documentElement).fontSize),
    }));
    check(
      density.height <= density.font * 20,
      name + ' five stops do not create a screen-height table row',
    );
    const tableImage = resolve(output, `${name}-five-stop-table.png`);
    await page.screenshot({ path: tableImage, fullPage: true });
    screenshots.push(tableImage);
    await row.locator('.dispatch-table__stops-summary').focus();
    await row.locator('.dispatch-table__stops-summary').press('Enter');
    await workspacePage(page);
    check(
      (await workspace.locator('.stop-workspace__stop').count()) === 5,
      name + ' Table summary opens every original visit',
    );
    check(
      (await workspace
        .locator(`[data-stop-id="${expected[3].id}"]`)
        .count()) === 1,
      name + ' Table workspace preserves the third visit',
    );
    await returnToDispatch(page);
    await page.locator('.dispatch-load').nth(1).waitFor();
  }
}
async function checkLoadWorkspaces(page, name, screenshots) {
  await checkWorkspaceLoads(page, name, screenshots, {
    loads: dispatches(),
    cycles: [null, '~20h 55m', '~18h 20m', '~16h 45m', '~12h 30m'],
    check,
    output,
  });
  await checkCardAlignment(page, name + ' after workspace navigation');
}
async function checkToolbar(page, name) {
  await page.mouse.move(0, 0);
  const metrics = await page.locator('.filter-toolbar').evaluate(toolbar => {
    const visible = element =>
      element.getBoundingClientRect().width > 0 &&
      element.getBoundingClientRect().height > 0;
    const controls = [
      ...toolbar.querySelectorAll(
        'input:not([type=checkbox]), select, .filter-toolbar__views button, .filter-toolbar__toggle, .btn',
      ),
    ]
      .filter(visible)
      .map(element => {
        const { x, y, width, height } = element.getBoundingClientRect();
        return {
          x,
          y,
          width,
          height,
          name: element.id || element.textContent.trim(),
          shadow: getComputedStyle(element).boxShadow,
        };
      });
    const toggles = [...toolbar.querySelectorAll('.filter-toolbar__toggle')]
      .filter(visible)
      .map(label => {
        const input = label.querySelector('input');
        return {
          name: label.textContent.trim(),
          type: input.type,
          tabIndex: input.tabIndex,
          width: input.getBoundingClientRect().width,
          background: getComputedStyle(label).backgroundColor,
          color: getComputedStyle(label).color,
          checked: input.checked,
        };
      });
    return {
      controls,
      toggles,
      fleet: toolbar.classList.contains('fleet-map-toolbar'),
      rootFont: parseFloat(getComputedStyle(document.documentElement).fontSize),
      viewport: document.documentElement.clientWidth,
      gap: parseFloat(getComputedStyle(toolbar).gap),
    };
  });
  const expectedHeight =
    metrics.rootFont * (metrics.viewport < 800 ? 2.75 : 2.5);
  check(metrics.controls.length >= 2, name + ' toolbar controls are present');
  check(
    Math.abs(metrics.gap - metrics.rootFont / 2) <= 1,
    name + ' toolbar spacing uses the shared rhythm',
  );
  for (const control of metrics.controls) {
    check(
      Math.abs(control.height - expectedHeight) <= 1,
      name + ` aligned toolbar height: ${control.name}`,
    );
    check(
      control.x >= -1 && control.x + control.width <= metrics.viewport + 1,
      name + ` toolbar control stays inside the viewport: ${control.name}`,
    );
  }
  for (const toggle of metrics.toggles) {
    check(
      toggle.type === 'checkbox' && toggle.tabIndex === 0,
      name + ` native keyboard checkbox: ${toggle.name}`,
    );
    check(
      Math.abs(toggle.width - metrics.rootFont * 1.25) <= 1,
      name + ` shared checkbox size: ${toggle.name}`,
    );
    check(
      metrics.fleet
        ? toggle.background !== 'rgba(0, 0, 0, 0)'
        : toggle.background === 'rgba(0, 0, 0, 0)',
      name +
        ` ${metrics.fleet ? 'filled map chip' : 'unframed scope label'}: ${toggle.name}`,
    );
  }
  return metrics;
}
await mkdir(output, { recursive: true });
try {
  for (const width of [1440, 390, 2344])
    for (const theme of ['light', 'dark'])
      for (const scale of [100, 200]) {
        if (
          process.env.UI_TEST_CASE &&
          process.env.UI_TEST_CASE !== `${width}-${theme}-${scale}`
        )
          continue;
        // Isolate native browser resources between responsive scenarios.
        browser = await chromium.launch({
          headless: true,
          ...(process.env.UI_TEST_BROWSER_CHANNEL
            ? { channel: process.env.UI_TEST_BROWSER_CHANNEL }
            : {}),
        });
        const context = await browser.newContext({
          viewport: { width, height: 1000 },
          colorScheme: theme,
          reducedMotion: 'reduce',
          serviceWorkers: 'block',
        });
        await context.addInitScript(
          ({ id, theme, scale }) => {
            localStorage.setItem(
              'auth_session',
              JSON.stringify({
                Id: id,
                AccessToken: 'fixture',
                RefreshToken: 'fixture',
              }),
            );
            window.uiFixtureCopies = [];
            Object.defineProperty(navigator, 'clipboard', {
              value: {
                writeText: async value => window.uiFixtureCopies.push(value),
              },
            });
            document.addEventListener('DOMContentLoaded', () => {
              document.documentElement.style.fontSize = scale + '%';
            });
          },
          { id, theme, scale },
        );
        let boardHold,
          showRepeatedVisits = false,
          showCompletedHistory = false,
          showCompletedScope = false,
          summaryReads = 0,
          telemetryReads = 0;
        const holdBoard = () => {
          assert.ok(!boardHold, 'A board fixture response is already held');
          let release;
          boardHold = new Promise(resolve => {
            release = resolve;
          });
          return () => {
            boardHold = null;
            release();
          };
        };
        await installReleaseArtifact(context, artifact, origin);
        await context.route('**/*', async route => {
          const url = new URL(route.request().url());
          if (
            url.origin === origin &&
            route.request().method() === 'POST' &&
            url.pathname === '/api/dispatch/board/planning'
          ) {
            summaryReads++;
            const scope = route.request().postDataJSON();
            assert.equal(scope.page, 1);
            assert.equal(typeof scope.search, 'string');
            const summary = planning();
            summary.state.plan.geometryOmitted = true;
            await route.fulfill({ status: 200, json: success([summary]) });
          } else if (
            url.origin === origin &&
            route.request().method() === 'POST' &&
            url.pathname === `/api/fleet/trucks/${truckId}/planning`
          ) {
            await route.fulfill({ status: 200, json: success(planning()) });
          } else if (
            url.origin !== origin ||
            !['GET', 'HEAD'].includes(route.request().method())
          ) {
            report.unexpectedRequests.push(
              `${route.request().method()} ${url.origin}${url.pathname}`,
            );
            await route.abort('blockedbyclient');
          } else if (url.pathname.startsWith('/api/')) {
            if (url.pathname === '/api/dispatch/board')
              showCompletedScope = false;
            else if (url.pathname === '/api/dispatch')
              showCompletedScope = true;
            const workspacePath = url.pathname.match(
              /^\/api\/dispatch\/([^/]+)\/(workspace|activity|documents|planning\/map)$/,
            );
            if (workspacePath) {
              const [, loadId, section] = workspacePath;
              const loads = showCompletedScope
                ? completedDispatches()
                : showRepeatedVisits || showCompletedHistory
                  ? repeatedDispatches()
                  : dispatches();
              const load = loads.find(load => load.id === loadId);
              assert.ok(load, 'Workspace fixture must match a known load');
              const response =
                section === 'planning/map'
                  ? { dispatchId: loadId, segments: [], missingSections: 0 }
                  : section === 'workspace'
                    ? workspaceReadModel(load)
                    : section === 'documents'
                      ? []
                      : {
                          dispatchId: loadId,
                          revision: 0,
                          items: [],
                          openItems: [],
                          openCount: 0,
                        };
              await route.fulfill({ status: 200, json: success(response) });
              return;
            }
            if (url.pathname === '/api/dispatch/board/telemetry') {
              telemetryReads++;
              assert.deepEqual(url.searchParams.getAll('truckIds'), [truckId]);
            }
            if (url.pathname === '/api/dispatch/board' && boardHold)
              await boardHold;
            if (
              url.pathname === '/api/dispatch' &&
              url.searchParams.get('status') !== 'completed'
            )
              report.unexpectedRequests.push(
                `Unexpected dispatch scope ${url.search}`,
              );
            const source =
              url.pathname === '/api/settings/appearance'
                ? success({ theme })
                : fixtures.get(url.pathname);
            const fixture = typeof source === 'function' ? source() : source;
            if (url.pathname === '/api/dispatch/board' && showRepeatedVisits)
              fixture.response.items[0].dispatches = repeatedDispatches();
            if (
              url.pathname === '/api/dispatch/board' &&
              showCompletedHistory
            ) {
              const load = repeatedDispatches()[0];
              load.stops.slice(0, 4).forEach(stop => {
                stop.departedAt = `${dateOnly(0)}T18:00:00Z`;
              });
              fixture.response.items[0].dispatches = [load];
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
          ) {
            await route.fulfill({
              status: 200,
              contentType: 'text/javascript',
              body: mapStub,
            });
          } else if (route.request().isNavigationRequest()) {
            await route.fulfill({
              status: 200,
              contentType: 'text/html',
              body: html,
            });
          } else await route.fallback();
        });
        const page = await context.newPage();
        page.on('pageerror', error => report.browserErrors.push(error.message));
        await page.goto(origin + '/login');
        await page.waitForURL(origin + '/fleet/map');
        await page.locator('.fleet-map-page .page-header').waitFor({
          state: width < 768 ? 'attached' : 'visible',
        });
        check(
          (await page.locator('.login-page').count()) === 0,
          'Authenticated Login redirects without showing credentials',
        );
        if (width === 390)
          await page
            .getByRole('button', { name: 'Open menu', exact: true })
            .click();
        const accountButton = page.locator('.sidebar__account');
        check(
          (await accountButton.getAttribute('aria-expanded')) === 'false',
          'Account actions start collapsed',
        );
        check(
          (await page
            .getByRole('button', { name: 'Logout', exact: true })
            .count()) === 0,
          'No standalone Logout row',
        );
        await accountButton.click();
        await page
          .getByRole('button', { name: 'Logout', exact: true })
          .waitFor();
        await page.keyboard.press('Escape');
        check(
          (await accountButton.getAttribute('aria-expanded')) === 'false',
          'Escape closes account actions',
        );
        check(
          await accountButton.evaluate(
            element => document.activeElement === element,
          ),
          'Escape restores account trigger focus',
        );
        if (width === 390) await page.keyboard.press('Escape');
        const measurements = [];
        const pages = [
          ['/dispatch', 'Dispatch', '.dispatch-truck'],
          ['/users', 'Users', '.users-page__user'],
          ['/settings', 'Settings', '#settings-ifta'],
          ['/settings/personal', 'Personal settings', '#personal-distance'],
          ['/fleet/map', 'Fleet Map', '[data-ui-fixture]'],
          ['/users/add', 'Add User', '#name'],
        ];
        for (const [path, title, ready] of width === 2344
          ? pages.slice(0, 1)
          : pages) {
          const releaseInitial = path === '/dispatch' ? holdBoard() : null;
          await page.goto(origin + path);
          const hiddenMapHeading = path === '/fleet/map' && width < 768;
          await page.locator('main h1').waitFor({
            state: hiddenMapHeading ? 'attached' : 'visible',
          });
          assert.equal(
            await page.locator('main h1').isVisible(),
            !hiddenMapHeading,
            `${title}: responsive heading visibility`,
          );
          const initialDispatchTop = releaseInitial
            ? await checkDispatchLoading(
                page,
                null,
                `${width}/${theme}/${scale} initial Dispatch`,
              )
            : null;
          releaseInitial?.();
          await page.locator(ready).waitFor();
          if (path === '/dispatch') {
            await page.locator('.dispatch-load__stop').nth(4).waitFor();
            await page
              .locator('.dispatch-planning__metric')
              .nth(2)
              .waitFor({ state: 'attached' });
            check(
              summaryReads === 1 && telemetryReads >= 1,
              `${width}/${theme}/${scale} initial Dispatch uses one page-summary request and scoped telemetry`,
            );
            await page.waitForFunction(
              () =>
                document
                  .querySelector('.dispatch-load__number')
                  ?.textContent.trim() === 'AMF1441',
            );
            await checkDispatchTop(
              page,
              initialDispatchTop,
              `${width}/${theme}/${scale} initial Dispatch ready`,
            );
          }
          if (path === '/settings')
            await page.locator('#settings-load-prefix').waitFor();
          await page.evaluate(
            () =>
              new Promise(resolve => {
                requestAnimationFrame(() => requestAnimationFrame(resolve));
              }),
          );
          if (path === '/settings/personal') {
            const text = await page.evaluate(() => {
              const selectors = 'main h1, .settings-page__fields label';
              return {
                body: getComputedStyle(document.body).color,
                values: [...document.querySelectorAll(selectors)].map(
                  element => getComputedStyle(element).color,
                ),
              };
            });
            check(
              text.values.length === 3 &&
                text.values.every(value => value === text.body),
              'Personal settings headings and labels inherit themed body text',
            );
          }
          const name = `${width}-${theme}-${scale}-${path.replaceAll('/', '-').slice(1)}`;
          const screenshot = resolve(output, name + '.png');
          await page.screenshot({ path: screenshot, fullPage: true });
          if (path === '/dispatch') {
            await checkCardAlignment(page, name + ' collapsed');
            check(
              (await page
                .locator('.dispatch-load details, .dispatch-planning details')
                .count()) === 0,
              name + ' default cards and driver header do not expand inline',
            );
            check(
              (await page
                .locator(
                  '.dispatch-load__summary, .dispatch-load__metrics, .dispatch-paper__financials',
                )
                .count()) === 0,
              name +
                ' mileage and pricing details stay out of compact cards until opened',
            );
            await page.locator('.dispatch-truck__loads').evaluateAll(lanes =>
              lanes.forEach(lane => {
                lane.scrollLeft = 0;
              }),
            );
            await page.evaluate(() => window.scrollTo(0, 0));
          }
          const metrics = await page.evaluate(() => {
            const rect = element => {
              const { x, y, width, height } = element.getBoundingClientRect();
              return { x, y, width, height };
            };
            const visible = element =>
              element.getBoundingClientRect().width > 0 &&
              element.getBoundingClientRect().height > 0;
            const controls = [
              ...document.querySelectorAll(
                'main button, main input:not([type=checkbox]), main select, main .btn',
              ),
            ]
              .filter(visible)
              .map(element => ({
                name:
                  element.getAttribute('aria-label') ||
                  element.id ||
                  element.textContent.trim(),
                ...rect(element),
                scrollWidth: element.scrollWidth,
                clientWidth: element.clientWidth,
                scrollHeight: element.scrollHeight,
                clientHeight: element.clientHeight,
                scrollableCard: element.closest('.dispatch-truck__loads')
                  ? rect(element.closest('.dispatch-load'))
                  : null,
              }));
            const textBlock = element =>
              element
                ? {
                    text: element.textContent.trim(),
                    ...rect(element),
                    fontSize: parseFloat(getComputedStyle(element).fontSize),
                  }
                : null;
            const inkRect = element => {
              if (!element) return null;
              const range = document.createRange();
              range.selectNodeContents(element);
              const { x, y, width, height } = range.getBoundingClientRect();
              return { x, y, width, height };
            };
            const cycleBlock = element =>
              element
                ? {
                    ...textBlock(element),
                    scrollWidth: element.scrollWidth,
                    clientWidth: element.clientWidth,
                    scrollHeight: element.scrollHeight,
                    clientHeight: element.clientHeight,
                    fields: [...element.querySelectorAll('dl > div')].map(
                      field => ({
                        ...rect(field),
                        label: textBlock(field.querySelector('dt')),
                        value: textBlock(field.querySelector('dd')),
                        scrollWidth: field.scrollWidth,
                        clientWidth: field.clientWidth,
                      }),
                    ),
                    recapTime: element
                      .querySelector('time')
                      ?.getAttribute('datetime'),
                    recap: textBlock(element.querySelector('time')),
                    recapAmount: textBlock(element.querySelector('dd > span')),
                    home: textBlock(element.querySelector('small')),
                    homeZone: element
                      .querySelector('small')
                      ?.getAttribute('title'),
                  }
                : null;
            const dispatchCards = [
              ...document.querySelectorAll('.dispatch-load'),
            ].map(card => ({
              ...rect(card),
              current: card.classList.contains('dispatch-load--current'),
              loadNumber: textBlock(
                card.querySelector('.dispatch-load__number'),
              ),
              order: textBlock(card.querySelector('.dispatch-load__order')),
              cycle: cycleBlock(card.querySelector('.dispatch-load__cycle')),
              overview: rect(card.querySelector('.dispatch-load__overview')),
              footer: rect(card.querySelector('.dispatch-load__footer')),
              stopGrid: rect(card.querySelector('.dispatch-load__route')),
              stops: [...card.querySelectorAll('.dispatch-load__stop')].map(
                stop => ({
                  ...rect(stop),
                  id: stop.dataset.stopId,
                  scrollWidth: stop.scrollWidth,
                  clientWidth: stop.clientWidth,
                  text: stop.textContent.trim(),
                  number: stop
                    .querySelector('.dispatch-load__stop-number')
                    ?.textContent.trim(),
                  location: stop
                    .querySelector('.dispatch-load__location')
                    ?.textContent.trim(),
                  locationText: textBlock(
                    stop.querySelector('.dispatch-load__location'),
                  ),
                  facility: stop
                    .querySelector('.dispatch-load__facility')
                    ?.textContent.trim(),
                  facilityText: textBlock(
                    stop.querySelector('.dispatch-load__facility'),
                  ),
                  appointmentReference: textBlock(
                    stop.querySelector('.dispatch-load__appointment-reference'),
                  ),
                  referenceNumber: textBlock(
                    stop.querySelector('.dispatch-load__reference-number'),
                  ),
                  appointment: textBlock(
                    stop.querySelector(
                      '.arrival-estimate__appointment, .dispatch-load__history-time',
                    ),
                  ),
                  estimate: textBlock(stop.querySelector('.stop-hours__road')),
                  cycle: textBlock(
                    stop.querySelector('.dispatch-load__stop-cycle'),
                  ),
                  cycleValue: textBlock(
                    stop.querySelector('.dispatch-load__stop-cycle strong'),
                  ),
                  timing: textBlock(
                    stop.querySelector('.dispatch-load__timing'),
                  ),
                  late:
                    stop
                      .querySelector('.arrival-estimate__late')
                      ?.textContent.trim() ?? null,
                  actual:
                    stop
                      .querySelector('.dispatch-load__actual')
                      ?.textContent.trim() ?? null,
                  completed: !!stop.querySelector(
                    '.dispatch-load__completed, [aria-label="Completed"]',
                  ),
                }),
              ),
            }));
            const summary = document.querySelector(
              '.dispatch-planning--compact',
            );
            const routeSummary = summary
              ? {
                  ...rect(summary),
                  heading: textBlock(
                    summary.querySelector('.dispatch-planning__heading'),
                  ),
                  identity: textBlock(
                    summary.querySelector('.dispatch-planning__identity'),
                  ),
                  fuel: textBlock(summary.querySelector('.fuel-reading')),
                  recap: textBlock(summary.querySelector('.driver-next-recap')),
                  recapTime: summary
                    .querySelector('.driver-next-recap time')
                    ?.getAttribute('datetime'),
                  nextStop: textBlock(
                    summary.querySelector('.dispatch-planning__next'),
                  ),
                  nextStopInk: inkRect(
                    summary.querySelector('.dispatch-planning__next > strong'),
                  ),
                  appointment: textBlock(
                    summary.querySelector(
                      '.dispatch-planning__details .arrival-estimate__appointment',
                    ),
                  ),
                  metrics: [
                    ...summary.querySelectorAll('.dispatch-planning__metric'),
                  ].map(metric => ({
                    ...rect(metric),
                    label: textBlock(metric.querySelector(':scope > span')),
                    miles: textBlock(
                      metric.querySelector('.dispatch-planning__miles'),
                    ),
                    kilometres: textBlock(metric.querySelector('small')),
                    scrollWidth: metric.scrollWidth,
                    clientWidth: metric.clientWidth,
                  })),
                }
              : null;
            const truck = document.querySelector('.dispatch-truck');
            const headerZone = selector => {
              const element = truck?.querySelector(selector);
              return element
                ? {
                    ...rect(element),
                    scrollWidth: element.scrollWidth,
                    clientWidth: element.clientWidth,
                    scrollHeight: element.scrollHeight,
                    clientHeight: element.clientHeight,
                  }
                : null;
            };
            const truckHeader = truck
              ? {
                  frame: rect(truck),
                  identity: headerZone(':scope > .dispatch-truck__header'),
                  content: headerZone('.dispatch-planning__content'),
                  hos: headerZone('.driver-hours-panel'),
                  driver: headerZone('.dispatch-planning__driver'),
                  identityGroup: headerZone('.dispatch-truck__identity'),
                  mapAction: headerZone('.dispatch-truck__map'),
                  identityGap: parseFloat(
                    getComputedStyle(
                      truck.querySelector('.dispatch-truck__header'),
                    ).columnGap,
                  ),
                }
              : null;
            const prefixInput = document.querySelector('#settings-load-prefix');
            const prefixForm = prefixInput?.closest('form');
            const fuelForm = document
              .querySelector('#settings-ifta')
              ?.closest('form');
            const prefixSettings = prefixInput
              ? {
                  ...rect(prefixInput),
                  value: prefixInput.value,
                  label: textBlock(
                    document.querySelector('label[for="settings-load-prefix"]'),
                  ),
                  independentForm:
                    !!prefixForm &&
                    !!fuelForm &&
                    prefixForm !== fuelForm &&
                    !fuelForm.contains(prefixInput) &&
                    !prefixForm.contains(
                      document.querySelector('#settings-ifta'),
                    ),
                }
              : null;
            return {
              heading: rect(document.querySelector('main h1')),
              fleetToolbar: document.querySelector('.fleet-map-toolbar')
                ? rect(document.querySelector('.fleet-map-toolbar'))
                : null,
              controls,
              dispatchCards,
              routeSummary,
              truckHeader,
              prefixSettings,
              truckWidth:
                document.querySelector('.dispatch-truck')?.clientWidth,
              viewport: document.documentElement.clientWidth,
              documentWidth: document.documentElement.scrollWidth,
              rootFont: getComputedStyle(document.documentElement).fontSize,
              background: getComputedStyle(document.body).backgroundColor,
            };
          });
          const detailScreenshots = [];
          if (path === '/dispatch') {
            const expanded = resolve(output, `${name}-expanded.png`);
            await page.screenshot({ path: expanded, fullPage: true });
            detailScreenshots.push(expanded);
          }
          if (path === '/dispatch' || path === '/fleet/map') {
            metrics.toolbar = await checkToolbar(page, name);
            const detail = resolve(output, `${name}-toolbar.png`);
            await page.locator('.filter-toolbar').screenshot({ path: detail });
            detailScreenshots.push(detail);
          }
          if (path === '/dispatch') {
            const future = page.locator('.dispatch-load').last();
            const panels = [
              ['late-stop', future.locator('.dispatch-load__stop').last()],
            ];
            if (width === 390)
              panels.push(
                ['stop', future.locator('.dispatch-load__stop').first()],
                ['footer', future.locator('.dispatch-load__footer')],
              );
            for (const [panel, locator] of panels) {
              const detail = resolve(output, `${name}-${panel}.png`);
              await locator.screenshot({ path: detail });
              detailScreenshots.push(detail);
            }
          }
          measurements.push({
            path,
            title,
            screenshot,
            detailScreenshots,
            ...metrics,
          });
          if (path === '/settings/personal') {
            const choices = [
              ['#personal-temperature', ['fahrenheit', 'celsius'], 'celsius'],
              ['#personal-distance', ['miles', 'kilometers', 'both'], 'both'],
            ];
            for (const [selector, options, defaultValue] of choices) {
              assert.deepEqual(
                await page
                  .locator(`${selector} option`)
                  .evaluateAll(nodes => nodes.map(node => node.value)),
                options,
              );
              assert.equal(
                await page.locator(selector).inputValue(),
                defaultValue,
              );
              const bounds = await page.locator(selector).boundingBox();
              assert.ok(
                bounds.width > 0 &&
                  bounds.x >= 0 &&
                  bounds.x + bounds.width <= width + 1,
              );
            }
            assert.equal(await page.locator('#settings-ifta').count(), 0);
            assert.equal(
              await page.locator('#settings-load-prefix').count(),
              0,
            );
          }
          if (path === '/settings') {
            assert.equal(
              await page
                .locator(
                  '#settings-temperature-unit, #settings-distance-unit, ' +
                    '#personal-temperature, #personal-distance',
                )
                .count(),
              0,
              name + ' units belong only to personal settings',
            );
            assert.deepEqual(
              await page
                .locator('.integration-settings [data-provider]')
                .evaluateAll(cards => cards.map(card => card.dataset.provider)),
              ['torqueai', 'samsara', 'google-email'],
              name + ' only the three approved integrations',
            );
            assert.equal(
              await page.locator('.integration-settings input').count(),
              0,
              name + ' credentials are not read back',
            );
            const google = page.locator('[data-provider="google-email"]');
            await google
              .getByRole('button', { name: 'Edit credentials', exact: true })
              .click();
            assert.equal(
              await google
                .locator('input[type=password][autocomplete=new-password]')
                .count(),
              3,
              name + ' Google email uses three empty OAuth credential inputs',
            );
            assert.deepEqual(
              await google
                .locator('input')
                .evaluateAll(fields => fields.map(field => field.value)),
              ['', '', ''],
              name + ' replacement inputs contain no saved credentials',
            );
            const integrationLayout = await page
              .locator('.integration-settings')
              .evaluate(section => {
                const bounds = element => {
                  const rect = element.getBoundingClientRect();
                  return {
                    left: rect.left,
                    right: rect.right,
                    width: rect.width,
                    height: rect.height,
                    scrollWidth: element.scrollWidth,
                    clientWidth: element.clientWidth,
                  };
                };
                return {
                  viewport: document.documentElement.clientWidth,
                  cards: [...section.querySelectorAll('[data-provider]')].map(
                    bounds,
                  ),
                  fields: [...section.querySelectorAll('input')].map(bounds),
                  controls: [...section.querySelectorAll('button')].map(bounds),
                };
              });
            for (const [index, field] of integrationLayout.fields.entries()) {
              check(
                field.left >= 0 &&
                  field.right <= integrationLayout.viewport + 1,
                `${name} integration replacement field ${index} remains inside the viewport`,
              );
              check(
                field.height >= (width <= 799 ? 44 : 40) - 1,
                `${name} integration replacement field ${index} retains shared control height`,
              );
            }
            for (const [index, card] of integrationLayout.cards.entries())
              check(
                card.scrollWidth <= card.clientWidth + 1 &&
                  card.left >= 0 &&
                  card.right <= integrationLayout.viewport + 1,
                `${name} integration card ${index} contains its editable content`,
              );
            const integrationScreenshot = resolve(
              output,
              `${name}-integrations-editing.png`,
            );
            await page
              .locator('.integration-settings')
              .screenshot({ path: integrationScreenshot });
            detailScreenshots.push(integrationScreenshot);
            await google
              .locator('#integration-google-email-refreshToken')
              .fill('offline-discarded-draft');
            await google
              .getByRole('button', { name: 'Cancel', exact: true })
              .click();
            await google
              .getByRole('button', { name: 'Edit credentials', exact: true })
              .click();
            assert.equal(
              await google
                .locator('#integration-google-email-refreshToken')
                .inputValue(),
              '',
              name + ' cancel clears entered credentials',
            );
            await google
              .getByRole('button', { name: 'Cancel', exact: true })
              .click();
            check(
              metrics.prefixSettings?.value === 'AMF' &&
                metrics.prefixSettings.independentForm,
              name +
                ' saved load prefix is editable independently from fuel preferences',
            );
            check(
              metrics.prefixSettings?.label?.text.includes('prefix'),
              name + ' load prefix has a visible field label',
            );
            await page.locator('#settings-load-prefix').fill('TMS-');
            assert.equal(
              await page.locator('#settings-detour').count(),
              0,
              name + ' no hard detour limit control',
            );
            assert.equal(
              await page
                .locator(
                  '#settings-driving-cost, #settings-reserve, #settings-fill',
                )
                .count(),
              0,
              name + ' retired fleet defaults have no editable controls',
            );
            assert.equal(
              await page
                .getByRole('heading', { name: 'Fuel stops', exact: true })
                .count(),
              0,
              name + ' retired fuel stops section is absent',
            );
            assert.equal(
              await page
                .getByRole('button', { name: 'Restore defaults', exact: true })
                .count(),
              0,
              name + ' retired restore defaults action is absent',
            );
            const iftaBefore = await page.locator('#settings-ifta').isChecked();
            await page.locator('#settings-ifta').setChecked(!iftaBefore);
            assert.equal(
              await page.locator('#settings-load-prefix').inputValue(),
              'TMS-',
              name + ' price basis does not reset the dispatch prefix draft',
            );
            check(
              await page
                .getByRole('button', { name: 'Save settings', exact: true })
                .isEnabled(),
              name +
                ' price basis remains an explicit save, not an automatic write',
            );
            const prefixExample = page.locator(
              '.dispatch-number-settings__form .settings-page__hint strong',
            );
            await page.waitForFunction(
              () =>
                document.querySelector(
                  '.dispatch-number-settings__form .settings-page__hint strong',
                )?.textContent === 'TMS-1373',
            );
            assert.equal(
              await prefixExample.textContent(),
              'TMS-1373',
              name + ' custom prefix preview',
            );
            await page.locator('#settings-load-prefix').fill('');
            await page.locator('#settings-ifta').focus();
            await page.waitForFunction(
              () =>
                document.querySelector(
                  '.dispatch-number-settings__form .settings-page__hint strong',
                )?.textContent === '1373',
            );
            assert.equal(
              await prefixExample.textContent(),
              '1373',
              name + ' blank prefix preview keeps only the number',
            );
            assert.equal(
              await page.locator('#settings-ifta').isChecked(),
              !iftaBefore,
              name +
                ' prefix changes preserve the independent price basis draft',
            );
          }
          if (path === '/dispatch') {
            const cards = metrics.dispatchCards;
            check(
              cards.length === 2 && cards[0].current && !cards[1].current,
              name + ' current and upcoming cards',
            );
            check(
              (await page
                .locator('.dispatch-truck__available--next')
                .count()) === 0,
              name + ' an assigned next load has no empty placeholder',
            );
            if (width > 550) {
              check(
                cards[1].x >= cards[0].x + cards[0].width - 1 &&
                  Math.abs(cards[1].y - cards[0].y) <= 1,
                name +
                  ' assigned loads occupy a horizontal lane in dispatch order',
              );
            } else {
              check(
                cards[1].y >= cards[0].y + cards[0].height - 1,
                name +
                  ' mobile load cards stack without shrinking the route timeline',
              );
            }
            check(
              cards[0]?.loadNumber?.text === 'AMF1441' &&
                cards[1]?.loadNumber?.text === 'AMF1442',
              name + ' current and future loads use the same configured prefix',
            );
            check(
              cards.every(card => card.cycle === null),
              name + ' detailed cycle forecasts belong to the load workspace',
            );
            const expectedOrders = ['CURRENT-ORD-1441', 'FUTURE-ORD-1442'];
            check(
              cards.every(
                (card, index) =>
                  card.order?.text === `Order ${expectedOrders[index]}`,
              ),
              name + ' visible current and future order numbers',
            );
            for (const [index, button] of (
              await page
                .getByRole('button', { name: 'Copy order number', exact: true })
                .all()
            ).entries()) {
              await button.click();
              check(
                await page.evaluate(
                  value => window.uiFixtureCopies.at(-1) === value,
                  expectedOrders[index],
                ),
                name + ` exact order ${index + 1} copied`,
              );
              check(
                (await button.locator('.is-copied').count()) === 1 &&
                  !(await page
                    .getByText('Order number copied.', { exact: true })
                    .count()),
                name + ' copy confirmation stays inside the existing button',
              );
            }
            const rootFont = parseFloat(metrics.rootFont);
            const header = metrics.truckHeader;
            const zones = ['identity', 'content', 'hos', 'driver'].map(key => [
              key,
              header?.[key],
            ]);
            for (const [key, zone] of zones) {
              check(
                zone &&
                  zone.width > 0 &&
                  zone.height > 0 &&
                  zone.x >= header.frame.x - 1 &&
                  zone.x + zone.width <=
                    header.frame.x + header.frame.width + 1 &&
                  zone.scrollWidth <= zone.clientWidth + 2 &&
                  zone.scrollHeight <= zone.clientHeight + 2,
                name + ` truck header ${key} zone stays visible and unclipped`,
              );
            }
            for (const [index, [firstKey, first]] of zones.entries()) {
              for (const [secondKey, second] of zones.slice(index + 1)) {
                check(
                  first &&
                    second &&
                    (Math.min(first.x + first.width, second.x + second.width) -
                      Math.max(first.x, second.x) <=
                      1 ||
                      Math.min(
                        first.y + first.height,
                        second.y + second.height,
                      ) -
                        Math.max(first.y, second.y) <=
                        1),
                  name +
                    ` truck header ${firstKey} and ${secondKey} zones do not overlap`,
                );
              }
            }
            const hosDials = await page
              .locator(
                '.dispatch-truck .driver-hours__clock .driver-hours__dial',
              )
              .evaluateAll(nodes =>
                nodes.map(node => {
                  const dial = node.getBoundingClientRect(),
                    text = node.querySelector('strong').getBoundingClientRect();
                  return {
                    diameter: dial.width,
                    height: dial.height,
                    corner: Math.hypot(text.width / 2, text.height / 2),
                    centered: Math.abs(
                      (text.left + text.right - dial.left - dial.right) / 2,
                    ),
                    fontSize: parseFloat(
                      getComputedStyle(node.querySelector('strong')).fontSize,
                    ),
                  };
                }),
              );
            check(
              hosDials.length >= 4 &&
                hosDials.every(
                  dial =>
                    Math.abs(dial.height - dial.diameter) <= 1 &&
                    dial.centered <= 1 &&
                    dial.corner < (dial.diameter * 26) / 64 - 1 &&
                    dial.fontSize <= dial.diameter * 0.24 + 0.02,
                ),
              name +
                ' Dispatch HOS values share the responsive diameter and stay inside their rings',
            );
            if (width === 2344 && scale === 100) {
              check(
                header?.identity &&
                  header?.content &&
                  header?.hos &&
                  Math.abs(
                    header.content.x -
                      header.identity.x -
                      header.identity.width,
                  ) <= 1 &&
                  Math.abs(
                    header.hos.x - header.content.x - header.content.width,
                  ) <= 1 &&
                  Math.max(header.identity.y, header.content.y, header.hos.y) <
                    Math.min(
                      header.identity.y + header.identity.height,
                      header.content.y + header.content.height,
                      header.hos.y + header.hos.height,
                    ),
                name +
                  ' wide truck header left-packs adjacent content-sized identity, telemetry and HOS groups',
              );
              check(
                header?.hos &&
                  header.hos.x + header.hos.width <
                    header.frame.x + header.frame.width - rootFont,
                name +
                  ' unused wide-header space remains after HOS, not between header groups',
              );
              check(
                header?.identityGroup &&
                  header?.mapAction &&
                  Math.abs(
                    header.mapAction.x -
                      header.identityGroup.x -
                      header.identityGroup.width -
                      header.identityGap,
                  ) <= 1,
                name +
                  ' truck map action stays beside identity with its named control gap',
              );
            }
            for (const card of cards) {
              const { overview, stopGrid, footer } = card;
              check(
                overview.x >= card.x - 1 &&
                  overview.x + overview.width <= card.x + card.width + 1 &&
                  footer.x >= overview.x - 1 &&
                  footer.x + footer.width <= overview.x + overview.width + 1,
                name + ' load overview and footer fit the card',
              );
              check(
                footer.y >= stopGrid.y + stopGrid.height - 1 &&
                  Math.abs(footer.x - stopGrid.x) <= 1,
                name + ' compact operational footer follows its stop timeline',
              );
              check(
                card.stops.every(
                  (stop, index) =>
                    index === 0 ||
                    stop.y >=
                      card.stops[index - 1].y +
                        card.stops[index - 1].height -
                        1,
                ),
                name + ' pickups and deliveries form a vertical timeline',
              );
              for (const stop of card.stops) {
                check(
                  stop.timing === null,
                  name +
                    ` stop ${stop.id} does not present cumulative driving/rest as a delay explanation`,
                );
                check(
                  stop.cycle === null,
                  name + ' summary stops leave cycle details in the workspace',
                );
                check(
                  stop.locationText?.fontSize >=
                    rootFont * (stop.completed ? 14 / 16 : 1) - 0.01 &&
                    (stop.completed ||
                      stop.facilityText?.fontSize >=
                        (rootFont * 14) / 16 - 0.01) &&
                    stop.appointment?.fontSize >= (rootFont * 14) / 16 - 0.01 &&
                    (!stop.estimate ||
                      stop.estimate.fontSize >= (rootFont * 14) / 16 - 0.01),
                  name +
                    ` readable location, facility, appointment and ETA for stop ${stop.id}`,
                );
              }
            }
            const summary = metrics.routeSummary;
            check(
              summary?.recap?.text.replace(/\s+/g, ' ') ===
                `Next recap ${recapLabel} +3h 05m` &&
                Date.parse(summary.recapTime) ===
                  Date.parse(cycleAtCalculation().nextRecapAt),
              name +
                ' truck header shows the current recap date and credited hours without delivery-derived data',
            );
            check(
              summary?.heading?.text.includes('Route') &&
                summary.heading.text.includes('Load AMF1441') &&
                summary.fuel?.text === 'Fuel 28%',
              name + ' route/load/fuel summary identity',
            );
            check(
              JSON.stringify(
                summary?.metrics.map(metric => metric.label.text),
              ) ===
                JSON.stringify(['Total Distance', 'Remaining', 'Next Stop']),
              name + ' separate route distance labels',
            );
            check(
              JSON.stringify(
                summary?.metrics.map(metric => metric.miles.text),
              ) === JSON.stringify(['2,509 mi', '44 mi', '44 mi']) &&
                JSON.stringify(
                  summary?.metrics.map(metric => metric.kilometres.text),
                ) === JSON.stringify(['4,038 km', '71 km', '71 km']),
              name + ' unchanged total/remaining/next-stop distances',
            );
            for (const metric of summary?.metrics ?? []) {
              check(
                metric.x >= summary.x - 1 &&
                  metric.x + metric.width <= summary.x + summary.width + 1 &&
                  metric.scrollWidth <= metric.clientWidth + 2,
                name + ` route metric ${metric.label.text} is not clipped`,
              );
              if (width > 390)
                check(
                  metric.label.fontSize >= (rootFont * 11) / 16 - 0.01 &&
                    metric.miles.fontSize >= (rootFont * 14) / 16 - 0.01,
                  name + ` readable desktop route metric ${metric.label.text}`,
                );
              if (width === 390)
                check(
                  metric.miles.y >= metric.label.y + metric.label.height - 1 &&
                    metric.kilometres.y >=
                      metric.miles.y + metric.miles.height - 1,
                  name +
                    ` mobile ${metric.label.text} label/miles/km hierarchy`,
                );
            }
            if (
              summary?.nextStop &&
              summary.appointment &&
              summary.appointment.x >=
                summary.nextStop.x + summary.nextStop.width - 1
            ) {
              check(
                summary.appointment.x -
                  summary.nextStop.x -
                  summary.nextStop.width <=
                  2 * rootFont + 1 &&
                  summary.appointment.x -
                    summary.nextStopInk.x -
                    summary.nextStopInk.width <=
                    4 * rootFont + 1,
                name + ' next-stop appointment stays close to its destination',
              );
            }
            if (width === 390 && scale === 100) {
              check(
                summary.metrics.every(
                  metric =>
                    Math.abs(metric.miles.y - summary.metrics[0].miles.y) <= 1,
                ),
                name + ' aligned mobile route values',
              );
              check(
                summary.metrics[0].x + summary.metrics[0].width <
                  summary.metrics[1].x &&
                  summary.metrics[1].x + summary.metrics[1].width <
                    summary.metrics[2].x,
                name + ' three distinct compact mobile distance columns',
              );
            }
            check(
              cards[0]?.stops.length === 3 && cards[1]?.stops.length === 2,
              name + ' every stop is visible',
            );
            const renderedStops = cards.flatMap(card => card.stops);
            check(
              JSON.stringify(renderedStops.map(stop => stop.id)) ===
                JSON.stringify(stopIds),
              name + ' stop order and identity',
            );
            check(
              JSON.stringify(renderedStops.map(stop => stop.number)) ===
                JSON.stringify(['1', '2', '3', '1', '2']),
              name + ' pickup and delivery stop numbers remain visible',
            );
            check(
              renderedStops.every(
                stop =>
                  !stop.actual &&
                  !/\bActual (?:pickup|delivery)\b/i.test(stop.text),
              ),
              name + ' actual pickup and delivery date rows are omitted',
            );
            check(
              renderedStops.every(
                stop =>
                  stop.appointment &&
                  stop.location &&
                  (stop.completed || stop.facility),
              ),
              name +
                ' pending stops retain full facts; completed stops retain city and appointment',
            );
            const completed = renderedStops.find(
              stop => stop.id === stopIds[0],
            );
            check(
              completed?.completed && !completed.estimate && !completed.late,
              name +
                ' completed pickup retains status without a stale forecast',
            );
            const estimates = renderedStops.filter(stop => stop.estimate);
            check(
              estimates.length === 4,
              name + ' four local per-stop ETAs remain in the summary',
            );
            check(
              estimates.every(
                stop =>
                  stop.appointment &&
                  stop.estimate.y >=
                    stop.appointment.y + stop.appointment.height - 1,
              ),
              name + ' appointments and forecasts occupy separate rows',
            );
            const late = renderedStops.filter(stop => stop.late);
            check(
              late.length === 2 &&
                late[0].id === stopIds[1] &&
                late[0].late === 'Late by 25m' &&
                late[0].appointment?.text.includes('10:00') &&
                late[0].estimate?.text.includes('10:25') &&
                late[1].id === stopIds[4] &&
                late[1].late === 'Late by 1h 05m' &&
                late[1].appointment?.text.includes('06:00 PM') &&
                late[1].estimate?.text.includes('07:05 PM'),
              name + ' lateness belongs to its current or future stop',
            );
            for (const card of cards)
              for (const stop of card.stops) {
                check(
                  stop.width > 0 &&
                    stop.x >= card.x - 1 &&
                    stop.x + stop.width <= card.x + card.width + 1 &&
                    stop.scrollWidth <= stop.clientWidth + 2,
                  name + ` stop ${stop.id} fits inside its card`,
                );
                for (const other of card.stops)
                  if (other.id !== stop.id)
                    check(
                      stop.x + stop.width <= other.x + 1 ||
                        other.x + other.width <= stop.x + 1 ||
                        stop.y + stop.height <= other.y + 1 ||
                        other.y + other.height <= stop.y + 1,
                      name + ` stops ${stop.id} and ${other.id} do not overlap`,
                    );
              }
            await checkLoadWorkspaces(page, name, detailScreenshots);
            const dispatchTopBaseline = await dispatchTop(page);
            const papers = page.getByRole('button', {
              name: 'Papers',
              exact: true,
            });
            const table = page.getByRole('button', {
              name: 'Table',
              exact: true,
            });
            if (await table.isVisible()) {
              const releaseTable = holdBoard();
              await table.click();
              await checkDispatchLoading(
                page,
                dispatchTopBaseline,
                name + ' Table',
              );
              releaseTable();
              const firstRow = page.locator('.dispatch-table tbody tr').first();
              await firstRow.waitFor();
              await checkDispatchTop(
                page,
                dispatchTopBaseline,
                name + ' Table',
              );
              await table.evaluate(button =>
                Promise.all(
                  button.getAnimations().map(animation => animation.finished),
                ),
              );
              check(
                await table.evaluate(
                  button =>
                    getComputedStyle(button).color === 'rgb(255, 255, 255)',
                ),
                name +
                  ' selected Table remains filled with white text under the pointer',
              );
              check(
                (await firstRow.innerText()).includes('2,000.00 CAD') &&
                  (await firstRow.innerText()).includes('4.44 CAD') &&
                  (await firstRow.innerText()).includes('4.00 CAD'),
                name + ' table keeps all server financial values',
              );
              check(
                (await firstRow.innerText()).includes('10:00 AM'),
                name + ' table shows the next delivery appointment',
              );
              check(
                (await firstRow
                  .locator('.dispatch-table__stop-entry')
                  .count()) === 2,
                name + ' table summarizes intermediate delivery stops',
              );
              check(
                (await firstRow.innerText()).includes(
                  '2 deliveries · 0 completed',
                ),
                name + ' table retains the delivery count',
              );
              const tableImage = resolve(output, `${name}-table.png`);
              await page.screenshot({ path: tableImage, fullPage: true });
              detailScreenshots.push(tableImage);
              const mapLink = firstRow.locator('.dispatch-table__map');
              await mapLink.evaluate(link =>
                link.addEventListener(
                  'click',
                  event => event.preventDefault(),
                  { once: true },
                ),
              );
              await mapLink.click();
              check(
                !(await page.locator('.dispatch-details').count()),
                name + ' Map link does not also open the load workspace',
              );
              await firstRow.locator('.dispatch-table__open').focus();
              await firstRow.locator('.dispatch-table__open').press('Enter');
              const tableWorkspace = await workspacePage(page);
              check(
                (await tableWorkspace.locator('h1').innerText()).includes(
                  'AMF1441',
                ),
                name + ' Table opens the same load with the keyboard',
              );
              const tableMobileTabs = page.locator(
                '.stop-workspace__mobile-tabs',
              );
              if (await tableMobileTabs.isVisible())
                await tableMobileTabs
                  .getByRole('button', { name: 'Stops', exact: true })
                  .click();
              check(
                (await tableWorkspace.innerText()).includes(
                  '04:00 PM – 06:00 PM',
                ),
                name +
                  ' Table workspace retains the delivery appointment window',
              );
              await returnToDispatch(page, 'Table');
            }
            const releasePapers = holdBoard();
            await papers.click();
            await checkDispatchLoading(
              page,
              dispatchTopBaseline,
              name + ' Papers',
            );
            releasePapers();
            assert.equal(
              await papers.getAttribute('aria-pressed'),
              'true',
              name + ' view interaction',
            );
            await checkDispatchTop(page, dispatchTopBaseline, name + ' Papers');
            const upcoming = page.locator('.dispatch-paper-column--2');
            check(
              (await upcoming.locator('h2').innerText()) ===
                'Delivery / pickup today and tomorrow',
              name + ' two-day heading',
            );
            check(
              await upcoming.evaluate(column => {
                const heading = column.querySelector('h2');
                const bounds = column.getBoundingClientRect();
                const title = heading.getBoundingClientRect();
                return (
                  title.left >= bounds.left &&
                  title.right <= bounds.right &&
                  column.scrollWidth <= column.clientWidth + 1
                );
              }),
              name + ' two-day heading fits without horizontal overflow',
            );
            await page.screenshot({
              path: resolve(output, `${name}-papers-columns.png`),
              fullPage: true,
            });
            await page
              .locator('.dispatch-paper__tab')
              .filter({ hasText: 'AMF1441' })
              .click();
            const sheet = await workspacePage(page);
            await workspaceFinancials(
              page,
              check,
              name + ' Papers',
              dispatches()[0],
              { output, screenshots: detailScreenshots },
            );
            const papersMobileTabs = page.locator(
              '.stop-workspace__mobile-tabs',
            );
            if (await papersMobileTabs.isVisible())
              await papersMobileTabs
                .getByRole('button', { name: 'Stops', exact: true })
                .click();
            check(
              (await sheet.innerText()).includes('Windsor') &&
                (await sheet.innerText()).includes('Completed'),
              name + ' papers retain the completed pickup',
            );
            check(
              (await sheet.innerText()).includes('04:00 PM – 06:00 PM'),
              name + ' papers retain the complete delivery appointment window',
            );
            const papersImage = resolve(output, `${name}-papers.png`);
            await page.screenshot({ path: papersImage, fullPage: true });
            detailScreenshots.push(papersImage);
            await returnToDispatch(page, 'Papers');
            await page.locator('#dispatch-completed').click();
            await page.locator('.dispatch-papers--completed').waitFor();
            check(
              (
                await page
                  .locator('.dispatch-paper-column__heading')
                  .innerText()
              ).includes('Completed'),
              name + ' completed papers have no active-phase folders',
            );
            await page
              .getByRole('button', { name: 'Cards', exact: true })
              .click();
            await page.locator('.dispatch-load').nth(1).waitFor();
            await checkDispatchTop(
              page,
              dispatchTopBaseline,
              name + ' completed Cards',
            );
            check(
              (await page
                .locator('.dispatch-planning, .dispatch-truck__equipment')
                .count()) === 0,
              name + ' archive does not show live telemetry or route planning',
            );
            check(
              (await page
                .locator('.dispatch-truck__available--next')
                .count()) === 0,
              name + ' archive does not imply a missing next assignment',
            );
            check(
              (await page.locator('.dispatch-load__stop').count()) === 5,
              name + ' archive preserves all pickup/delivery stops',
            );
            check(
              (await page
                .locator('.dispatch-load__metrics, .dispatch-paper__financials')
                .count()) === 0,
              name + ' archive cards keep financial details collapsed',
            );
            await page
              .locator('.dispatch-load')
              .first()
              .locator('.dispatch-load__details')
              .click();
            await workspacePage(page);
            await workspaceFinancials(
              page,
              check,
              name + ' archive',
              completedDispatches()[0],
              { output, screenshots: detailScreenshots },
            );
            await returnToDispatch(page, 'Cards', true);
            check(
              !/CURRENT LOAD|NEXT LOAD/.test(
                await page.locator('.dispatch-board').innerText(),
              ),
              name + ' archive is not mislabeled as current or next work',
            );
            check(
              (await page
                .getByRole('button', { name: /New load|Assign next load/ })
                .count()) === 0,
              name + ' unsupported load creation is not offered',
            );
            const completedImage = resolve(output, `${name}-completed.png`);
            await page.screenshot({ path: completedImage, fullPage: true });
            detailScreenshots.push(completedImage);
            showRepeatedVisits = true;
            await page.locator('#dispatch-active').click();
            await checkRepeatedVisits(page, name, detailScreenshots);
            showRepeatedVisits = false;
            showCompletedHistory = true;
            await page.reload();
            await page.locator('.dispatch-load__history-stop').nth(3).waitFor();
            const single = page.locator('.dispatch-load');
            check(
              (await single.count()) === 1 &&
                (await single
                  .locator('.dispatch-load__history-stop')
                  .count()) === 4,
              name + ' one load retains four short completed visits',
            );
            const historyLayout = await single.evaluate(element => {
              const rect = element.getBoundingClientRect(),
                lane = element.parentElement;
              const style = getComputedStyle(lane),
                cardStyle = getComputedStyle(element);
              const font = parseFloat(
                getComputedStyle(document.documentElement).fontSize,
              );
              const history = element
                .querySelector('.is-history')
                .getBoundingClientRect();
              const next = element
                .querySelector('.is-remaining')
                .getBoundingClientRect();
              const placeholder = lane.querySelector(
                '.dispatch-truck__available--next',
              );
              const empty = placeholder?.getBoundingClientRect();
              const horizontal = style.gridAutoFlow === 'column';
              return {
                rowHeights: [...element.querySelectorAll('.is-history')].map(
                  row => row.getBoundingClientRect().height,
                ),
                font,
                cardWidth: rect.width,
                emptyVisible:
                  !!empty &&
                  (horizontal
                    ? empty.left >= rect.right &&
                      Math.abs(empty.top - rect.top) < 2
                    : empty.top >= rect.bottom &&
                      Math.abs(empty.left - rect.left) < 2),
                emptyCompact: !!empty && empty.height < rect.height,
                emptyText: placeholder?.querySelector('strong')?.textContent,
                wide:
                  element.clientWidth -
                    parseFloat(cardStyle.paddingLeft) -
                    parseFloat(cardStyle.paddingRight) >=
                  48 * font,
                beside: next.left >= history.right,
                below: next.top >= history.bottom,
                short: [...element.querySelectorAll('.is-history')].every(
                  row => {
                    const line = parseFloat(
                      getComputedStyle(
                        row.querySelector('.dispatch-load__history-stop'),
                      ).lineHeight,
                    );
                    // Enlarged text may wrap the check, city and appointment across five lines.
                    const limit = font > 16 ? 5 * line + font : 5 * font;
                    return row.getBoundingClientRect().height < limit;
                  },
                ),
              };
            });
            report.historyLayouts ??= [];
            report.historyLayouts.push({ name, ...historyLayout });
            check(
              Math.abs(historyLayout.cardWidth - cards[0].width) < 2 &&
                historyLayout.emptyVisible &&
                historyLayout.emptyCompact &&
                historyLayout.emptyText === 'No next load',
              name +
                ' single current load keeps the standard card width and a compact empty next lane',
            );
            check(
              historyLayout.short &&
                (historyLayout.wide
                  ? historyLayout.beside
                  : historyLayout.below),
              name +
                ' normal-width card preserves short history and remaining work',
            );
            const historyImage = resolve(
              output,
              `${name}-completed-history.png`,
            );
            await page.screenshot({ path: historyImage, fullPage: true });
            detailScreenshots.push(historyImage);
            showCompletedHistory = false;
          }
          if (path === '/fleet/map' && width === 390) {
            const filters = page.getByRole('button', {
              name: 'Filters',
              exact: true,
            });
            await filters.click();
            assert.equal(
              await filters.getAttribute('aria-expanded'),
              'true',
              name + ' mobile filters interaction',
            );
            const expanded = await checkToolbar(page, name + ' expanded');
            check(
              expanded.toggles.length === 4,
              name +
                ' mobile filters retain IFTA, fuel, traffic and next loads',
            );
            const detail = resolve(output, `${name}-toolbar-filters.png`);
            await page
              .locator('#fleet-map-filters')
              .screenshot({ path: detail });
            detailScreenshots.push(detail);
          }
          if (path === '/fleet/map') {
            const ifta = page.getByRole('checkbox', {
              name: 'IFTA',
              exact: true,
            });
            const before = await ifta.isChecked();
            await ifta.focus();
            await ifta.press('Space');
            check(
              (await ifta.isChecked()) !== before,
              name + ' native checkbox toggles with Space',
            );
            await ifta.press('Space');
            check(
              (await ifta.isChecked()) === before,
              name + ' native checkbox restores with Space',
            );
          }
        }
        report.cases.push({ width, theme, scale, pages: measurements });
        await writeFile(
          resolve(output, 'report.json'),
          JSON.stringify(report, null, 2),
        );
        const mainPages = measurements.filter(page => page.path !== '/login');
        for (const page of measurements) {
          check(
            page.documentWidth <= page.viewport + 1,
            `${width}/${theme}/${scale} ${page.path}: horizontal overflow ${page.documentWidth} > ${page.viewport}`,
          );
          for (const control of page.controls) {
            const bounds = control.scrollableCard;
            check(
              bounds
                ? control.x >= bounds.x - 1 &&
                    control.x + control.width <= bounds.x + bounds.width + 1
                : control.x >= -1 &&
                    control.x + control.width <= page.viewport + 1,
              `${width}/${theme}/${scale} ${page.path}: control outside viewport (${control.name})`,
            );
            check(
              control.height >= 24 &&
                control.scrollWidth <= control.clientWidth + 2 &&
                control.scrollHeight <= control.clientHeight + 2,
              `${width}/${theme}/${scale} ${page.path}: clipped or undersized control (${control.name})`,
            );
          }
        }
        for (const page of mainPages.slice(1)) {
          if (page.path === '/fleet/map' && width < 768) continue;
          check(
            Math.abs(page.heading.x - mainPages[0].heading.x) <= 1,
            `${width}/${theme}/${scale}: ${page.title} heading left edge differs from Dispatch`,
          );
          const inlineToolbar =
            page.fleetToolbar &&
            page.fleetToolbar.x >= page.heading.x + page.heading.width;
          check(
            inlineToolbar
              ? Math.abs(
                  page.heading.y +
                    page.heading.height / 2 -
                    page.fleetToolbar.y -
                    page.fleetToolbar.height / 2,
                ) <= 1
              : Math.abs(page.heading.y - mainPages[0].heading.y) <= 1,
            `${width}/${theme}/${scale}: ${page.title} heading aligns with its row`,
          );
        }
        await context.close();
        await browser.close();
        browser = undefined;
        console.log(
          `UI smoke ${width}px ${theme} ${scale}%: ${measurements.length} pages checked.`,
        );
      }
  assert.ok(report.cases.length > 0, 'At least one UI case must run');
  assert.deepEqual(
    report.unexpectedRequests,
    [],
    'Offline smoke attempted unexpected network or writes',
  );
  assert.deepEqual(
    report.browserErrors,
    [],
    'Staged UI emitted browser errors',
  );
  assert.deepEqual(report.failures, [], 'Staged UI geometry regressions');
} finally {
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  await browser?.close();
}
console.log(`Offline UI smoke report: ${resolve(output, 'report.json')}`);
