import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'MAP_TEST_ARTIFACT_DIR must identify a staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput(
  'stop-details',
  process.env.STOP_DETAILS_OUTPUT_DIR,
);
const origin = 'http://localhost:5079';
const userId = '11111111-1111-1111-1111-111111111111';
const truckId = '22222222-2222-2222-2222-222222222222';
const currentLoadId = '33333333-3333-3333-3333-333333333333';
const futureLoadId = '44444444-4444-4444-4444-444444444444';
const stopIds = [1, 2, 3].map(
  index => `55555555-5555-5555-5555-${String(index).padStart(12, '0')}`,
);
const fixedNow = '2026-09-08T12:00:00Z';
const stop = (
  index,
  sequence,
  job,
  name,
  address,
  city,
  day,
  time,
  latitude,
  longitude,
) => ({
  id: stopIds[index],
  sequence,
  job,
  name,
  address,
  city,
  province: 'ON',
  country: 'Canada',
  scheduledDate: `2026-09-${day}`,
  scheduledTime: `${time}:00`,
  latitude,
  longitude,
});
const currentStop = stop(
  0,
  1,
  'Delivery',
  'Current receiving facility',
  '100 Current Street',
  'Toronto',
  '08',
  '16:00',
  43.65,
  -79.38,
);
const commoditySentinel = 'Commodity must stay outside the stop popup';
const notesSentinel = 'Notes must stay outside the stop popup';
const bolSentinel = '42845601';
const serviceSentinel = 'Service for Load sentinel';
const referenceIds = ['PU123456', 'DL654321'];
const unrelatedNotes = `${notesSentinel}. Shipper BOL: ${bolSentinel}. ${serviceSentinel}.`;
const appointmentNotes = `Shipper appointment confirmation number: ${referenceIds[0]}. Receiver appointment confirmation number: ${referenceIds[1]}. ${unrelatedNotes}`;
const futureStops = [
  stop(
    1,
    1,
    'Pickup',
    'East logistics terminal',
    '200 Future Avenue',
    'Kingston',
    '09',
    '08:00',
    44.23,
    -76.48,
  ),
  stop(
    2,
    2,
    'Delivery',
    'Capital distribution centre',
    '300 Receiving Road',
    'Ottawa',
    '09',
    '18:00',
    45.42,
    -75.7,
  ),
].map((stop, index) => ({
  ...stop,
  commodity: commoditySentinel,
  notes: appointmentNotes,
  truckNumber: '11006',
  trailerNumber: `NEXT-${index + 1}`,
  driverName: `Future assignment driver ${index + 1}`,
}));
const forecast = (dispatchId, stops, times, lateMinutes) => ({
  calculatedAt: fixedNow,
  validUntil: '2026-09-08T14:00:00Z',
  unavailableReason: null,
  assumptions: [],
  stops: stops.map((stop, index) => ({
    dispatchId,
    stopId: stop.id,
    arrival: `${stop.scheduledDate}T${times[index]}:00-04:00`,
    timeZoneId: 'America/Toronto',
    appointment: `${stop.scheduledDate}T${stop.scheduledTime}-04:00`,
    lateMinutes: lateMinutes[index],
    drivingMinutes: 120,
    restMinutes: 0,
  })),
});
const currentEta = forecast(currentLoadId, [currentStop], ['16:20'], [20]);
const futureEta = forecast(
  futureLoadId,
  futureStops,
  ['08:10', '18:00'],
  [10, 0],
);
const currentDetails = {
  id: currentLoadId,
  truckId,
  loadNumber: 1441,
  orderNumber: 'CURRENT-1441',
  customerName: 'Current fixture customer',
  stops: [currentStop],
  eta: currentEta,
};
const futureDetails = {
  id: futureLoadId,
  truckId,
  loadNumber: 1442,
  orderNumber: 'FUTURE-1442',
  customerName: 'Future fixture customer',
  stops: futureStops,
  eta: futureEta,
};
const point = (latitude, longitude) => ({ latitude, longitude });
const currentPlan = {
  id: '66666666-6666-6666-6666-666666666666',
  dispatchId: currentLoadId,
  truckId,
  version: 1,
  calculatedAt: fixedNow,
  originalPlannedMiles: 500,
  fromCurrentPosition: true,
  profile: {},
  stops: [
    {
      ...currentStop,
      address: '100 Current Street, Toronto, ON, Canada',
      point: point(43.65, -79.38),
    },
  ],
  tracking: {
    nextStopId: currentStop.id,
    nextStopLabel: currentStop.name,
    passedStopIds: [],
    visitedStops: {},
  },
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
const planning = {
  truckId,
  dispatchId: currentLoadId,
  loadNumber: 1441,
  message: null,
  state: {
    profile: {},
    plan: currentPlan,
    apiConfigured: false,
    fuelPercent: 75,
    fuelUpdatedAt: fixedNow,
    progress: {
      progressMiles: 380,
      remainingMiles: 120,
      remainingSeconds: 7200,
      distanceFromRouteMiles: 0,
      offRoute: false,
      locationStale: false,
      locationTime: fixedNow,
      position: point(41.8, -87.6),
    },
    eta: currentEta,
  },
};
const futureRoute = {
  id: futureLoadId,
  loadNumber: 1442,
  status: 'planned',
  stopCount: 2,
  stops: futureStops.map(stop => ({
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
  updatedAt: fixedNow,
  engineState: 'Driving',
  fuelPercent: 75,
};
const success = response => ({ success: true, response, errors: [] });
const fixtures = new Map([
  [
    '/api/auth/me',
    {
      id: userId,
      name: 'Fixture Administrator',
      email: 'fixture@example.invalid',
      isAdmin: true,
    },
  ],
  [
    '/api/settings/dispatch',
    success({ loadNumberPrefix: 'AMF', revision: 1, updatedAt: null }),
  ],
  ['/api/fleet/locations', success({ trucks: [truck], points: [truck] })],
  ['/api/fleet/hos', success({})],
  ['/api/fuel/price-overview', success([])],
  ['/api/fleet/planning/previews', success([])],
  [`/api/fleet/trucks/${truckId}/planning/preview`, success(planning)],
  [`/api/dispatch/${currentLoadId}`, success(currentDetails)],
  [`/api/dispatch/${futureLoadId}`, success(futureDetails)],
  [`/api/dispatch/truck/${truckId}`, success([currentDetails, futureDetails])],
  [
    `/api/dispatch/truck/${truckId}/next-routes`,
    success({
      revision: 'fixture-v1',
      unchanged: false,
      routes: [futureRoute],
    }),
  ],
]);

const mapStub = `export async function createFleetMap(element, _apiKey, callbacks) {
  element.dataset.stopDetailsFixture = 'offline-map-callbacks';
  element.style.backgroundColor = 'var(--ui-surface-muted)';
  element.style.backgroundImage = 'repeating-linear-gradient(0deg,transparent 0 79px,var(--ui-border-subtle) 79px 80px,transparent 80px 160px),repeating-linear-gradient(90deg,transparent 0 99px,var(--ui-border-subtle) 99px 100px,transparent 100px 200px)';
  const fixture = window.stopDetailsFixture = {next: null, currentReference: null, clearCount: 0, inspectorModes: [],
    selectStop(index) {return callbacks.invokeMethodAsync('OnNextLoadSelected',
      this.next.truckId, this.next.currentDispatchId, this.next.routes[0].id, index);}};
  return {setOptions(){},setTrucks(){},setStationsVisible(){},setTrafficVisible(){},
    setIfta(){},clearSelection(){fixture.clearCount++;},clearNextLoads(){fixture.next = null;},setNextLoadsVisible(){},
    setInspectorMode(mode){fixture.inspectorModes.push(mode);},clearMapInspection(){fixture.clearCount++;},setInspectionSuspended(){},setPriceOverview(){},
    clearNextLoadSelection(){fixture.clearCount++;},setStopEtas(){},setLoadReference(value){fixture.currentReference = value;},setDistanceUnit(){},setFollow(){},
    setRouteBytes(){return true;},setNextLoadsBytes(bytes){fixture.next = JSON.parse(new TextDecoder().decode(bytes));},
    focusTruck(){return true;},dispose(){delete window.stopDetailsFixture;}};
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
const report = {
  artifact,
  scope:
    'Actual staged Blazor future-stop card and current route header; deterministic offline map callbacks and API fixtures. No Google Maps, GPU, authentication backend, database or remote writes.',
  cases: [],
  failures: [],
  unexpectedRequests: [],
  browserErrors: [],
};
const check = (condition, message) => {
  if (!condition) report.failures.push(message);
};
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
await mkdir(output, { recursive: true });
const scenarios = [390, 1440, 2344].flatMap(width =>
  ['light', 'dark'].map(theme => ({ width, theme, withReferences: true })),
);
scenarios.push({ width: 1440, theme: 'light', withReferences: false });
try {
  for (const { width, theme, withReferences } of scenarios) {
    const name = `${width}-${theme}${withReferences ? '' : '-without-reference'}`;
    const context = await browser.newContext({
      viewport: { width, height: 1000 },
      colorScheme: theme,
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
        Object.defineProperty(navigator, 'clipboard', {
          configurable: true,
          value: {
            writeText: async text => {
              window.stopDetailsCopiedAddress = text;
            },
          },
        });
        document.addEventListener('DOMContentLoaded', () => {
          document.documentElement.dataset.theme = theme;
        });
      },
      { userId, theme },
    );
    await installReleaseArtifact(context, artifact, origin);
    await context.route('**/*', async route => {
      const request = route.request();
      const url = new URL(request.url());
      if (
        url.origin === origin &&
        request.method() === 'POST' &&
        url.pathname === `/api/fleet/trucks/${truckId}/planning`
      ) {
        await route.fulfill({ status: 200, json: success(planning) });
      } else if (
        url.origin !== origin ||
        !['GET', 'HEAD'].includes(request.method())
      ) {
        report.unexpectedRequests.push(
          `${request.method()} ${url.origin}${url.pathname}`,
        );
        await route.abort('blockedbyclient');
      } else if (url.pathname.startsWith('/api/')) {
        const fixture =
          url.pathname === `/api/dispatch/${futureLoadId}` && !withReferences
            ? success({
                ...futureDetails,
                stops: futureStops.map(stop => ({
                  ...stop,
                  notes: unrelatedNotes,
                })),
              })
            : url.pathname === '/api/settings/appearance'
              ? success({ theme })
              : fixtures.get(url.pathname);
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
      } else if (request.isNavigationRequest()) {
        await route.fulfill({
          status: 200,
          contentType: 'text/html',
          body: html,
        });
      } else await route.fallback();
    });
    const page = await context.newPage();
    let apiRequests = 0;
    page.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/api/')) apiRequests++;
    });
    page.on('pageerror', error =>
      report.browserErrors.push(`${name}: ${error.message}`),
    );
    page.on('console', message => {
      if (message.type() === 'error')
        report.browserErrors.push(`${name}: ${message.text()}`);
    });
    await page.clock.setFixedTime(new Date(fixedNow));
    await page.goto(`${origin}/fleet/map?truckId=${truckId}`);
    await page.locator('[data-stop-details-fixture]').waitFor();
    const current = page.locator('[aria-label="Current dispatch route"]');
    await current.waitFor({ state: 'attached' });
    await page.waitForFunction(() =>
      document
        .querySelector('[aria-label="Current dispatch route"]')
        ?.textContent.includes('CURRENT-1441'),
    );
    await page.waitForFunction(
      () =>
        window.stopDetailsFixture.currentReference?.orderNumber ===
          'CURRENT-1441' &&
        window.stopDetailsFixture.currentReference?.loadLabel === 'AMF1441',
    );
    assert.deepEqual(
      await page.evaluate(() => window.stopDetailsFixture.currentReference),
      {
        dispatchId: currentLoadId,
        loadNumber: 1441,
        orderNumber: 'CURRENT-1441',
        loadLabel: 'AMF1441',
      },
      'the current popup receives a formatted label without changing its numeric load or order identity',
    );
    if (width === 390)
      await page.getByRole('button', { name: 'Filters', exact: true }).click();
    const nextLoads = page.getByRole('checkbox', {
      name: 'Next loads',
      exact: true,
    });
    await nextLoads.focus();
    await page.keyboard.press('Space');
    assert.equal(await nextLoads.isChecked(), true);
    await page.waitForFunction(
      () => window.stopDetailsFixture?.next?.routes?.length === 1,
    );
    if (width === 390)
      await page.getByRole('button', { name: 'Filters', exact: true }).click();
    const initialMap = await page.locator('#fleet-map').evaluate(element => {
      const { x, y, width, height } = element.getBoundingClientRect();
      return { x, y, width, height };
    });
    const initialCurrent = await current.textContent();
    const caseResult = { width, theme, withReferences, initialMap, stops: [] };
    for (const index of [0, 1]) {
      await page.evaluate(
        index => window.stopDetailsFixture.selectStop(index),
        index,
      );
      const card = page.getByRole('region', {
        name: 'Selected next load',
        exact: true,
      });
      await card.waitFor();
      await page.waitForFunction(
        name =>
          document
            .querySelector('.fleet-map-next-load-card')
            ?.textContent.includes(name),
        futureStops[index].name,
      );
      await page.waitForFunction(() =>
        document
          .querySelector('.fleet-map-next-load-card')
          ?.textContent.includes('FUTURE-1442'),
      );
      const disclosure = page.locator(
        'button[aria-controls="fleet-map-next-load-details"]',
      );
      assert.equal(await disclosure.getAttribute('aria-expanded'), 'false');
      const extraFacts = card.locator('.fleet-map-next-load-card__metrics');
      const stopAssignment = card.locator(
        '.fleet-map-next-load-card__assignment',
      );
      assert.equal(await extraFacts.isVisible(), false);
      assert.equal(await stopAssignment.isVisible(), false);
      assert.equal(
        await card.locator('.fleet-route-popup__fuel').isVisible(),
        true,
      );
      const compactHeight = (await card.boundingBox()).height;
      await page.screenshot({
        path: resolve(output, `${name}-${index}-compact.png`),
        fullPage: true,
      });
      const beforeToggle = apiRequests;
      const buttonBounds = await disclosure.boundingBox();
      const retainedCard = await card.elementHandle();
      await disclosure.focus();
      await page.keyboard.press('Enter');
      assert.equal(await disclosure.getAttribute('aria-expanded'), 'true');
      assert.equal(await extraFacts.isVisible(), true);
      assert.equal(await stopAssignment.isVisible(), true);
      assert.ok((await card.boundingBox()).height > compactHeight);
      assert.equal(await retainedCard.evaluate(node => node.isConnected), true);
      assert.equal(apiRequests, beforeToggle);
      assert.deepEqual(
        await page.locator('#fleet-map').boundingBox(),
        initialMap,
      );
      assert.deepEqual(await disclosure.boundingBox(), buttonBounds);
      await page.keyboard.press('Space');
      assert.equal(await disclosure.getAttribute('aria-expanded'), 'false');
      assert.equal(await extraFacts.isVisible(), false);
      assert.equal(
        await disclosure.evaluate(node => node === document.activeElement),
        true,
      );
      assert.equal(apiRequests, beforeToggle);
      await disclosure.click();
      await retainedCard.dispose();
      const assignment = card.locator('.fleet-map-next-load-card__assignment');
      check(
        JSON.stringify(await assignment.locator('dd').allTextContents()) ===
          JSON.stringify([
            futureStops[index].truckNumber,
            futureStops[index].trailerNumber,
            futureStops[index].driverName,
          ]),
        `${name}: selected stop assignment replaces the current truck driver and trailer`,
      );
      check(
        await assignment.evaluate(element => {
          const bounds = element.getBoundingClientRect();
          return [...element.querySelectorAll('dt, dd')].every(node => {
            const rect = node.getBoundingClientRect();
            return (
              rect.left >= bounds.left - 1 && rect.right <= bounds.right + 1
            );
          });
        }),
        `${name}: stop assignment wraps without horizontal clipping`,
      );
      await page.locator('.fleet-map-inspector').evaluate(element => {
        element.scrollTop = 0;
      });
      const metrics = await card.evaluate(element => {
        const rect = node => {
          const { x, y, width, height } = node.getBoundingClientRect();
          return { x, y, width, height };
        };
        const style = getComputedStyle(element);
        const stage = element.closest('.fleet-map-stage');
        const inspector = element.closest('.fleet-map-inspector');
        const inspectorStyle = getComputedStyle(inspector);
        const text = node => node?.textContent.replace(/\s+/g, ' ').trim();
        const section = node => {
          const style = getComputedStyle(node);
          return {
            ...rect(node),
            text: text(node),
            borderTopWidth: Number.parseFloat(style.borderTopWidth),
            borderTopStyle: style.borderTopStyle,
            borderTopColor: style.borderTopColor,
            paddingTop: Number.parseFloat(style.paddingTop),
            marginTop: Number.parseFloat(style.marginTop),
            borderBottomWidth: Number.parseFloat(style.borderBottomWidth),
            borderBottomStyle: style.borderBottomStyle,
            borderBottomColor: style.borderBottomColor,
            paddingBottom: Number.parseFloat(style.paddingBottom),
            marginBottom: Number.parseFloat(style.marginBottom),
            sectionStart: node.classList.contains(
              'fleet-route-popup__section-start',
            ),
          };
        };
        const location = element.querySelector('.fleet-route-popup__location');
        const information = element.querySelector(
          '.fleet-route-popup__information',
        );
        const loadHeader = location?.querySelector(
          '.fleet-map-next-load-card__header',
        );
        const loadReference = loadHeader?.querySelector(
          '.fleet-map-route-info__load',
        );
        const detailsLink = information?.querySelector(
          '.fleet-route-popup__details-link',
        );
        const detailsLinkStyle = detailsLink && getComputedStyle(detailsLink);
        const job = location?.querySelector('.fleet-route-popup__kind');
        const company = location?.querySelector(':scope > strong');
        const address = location?.querySelector(
          '.fleet-map-route-info__copy-address',
        );
        const appointment = information?.querySelector(
          '.fleet-route-popup__appointment',
        );
        const eta = information?.querySelector('.arrival-estimate');
        const distanceSection = information?.querySelector(
          '.fleet-map-next-load-card__metrics',
        );
        const etaLine = [
          ...(eta?.querySelectorAll(':scope > span') ?? []),
        ].find(line => /^ETA\b/.test(text(line)));
        const etaTime = etaLine?.querySelector('strong');
        let etaLabel;
        if (etaLine?.firstChild?.nodeType === Node.TEXT_NODE) {
          const range = document.createRange();
          range.selectNodeContents(etaLine.firstChild);
          etaLabel = rect(range);
        }
        const addressLines = [...(address?.querySelectorAll('span') ?? [])]
          .filter(line => !line.querySelector('span'))
          .map(line => ({ ...rect(line), text: text(line) }));
        const references = [
          ...(location?.querySelectorAll('.fleet-route-popup__reference') ??
            []),
        ].map(reference => ({
          ...section(reference),
          afterAddress: reference.previousElementSibling === address,
        }));
        const distances = [
          ...element.querySelectorAll('.fleet-map-next-load-card__metrics dt'),
        ].map(label => {
          const range = document.createRange();
          range.selectNodeContents(label);
          const valueRange = document.createRange();
          valueRange.selectNodeContents(label.nextElementSibling);
          return {
            label: text(label),
            value: text(label.nextElementSibling),
            labelBounds: rect(label),
            labelTextBounds: rect(range),
            valueBounds: rect(label.nextElementSibling),
            valueTextBounds: rect(valueRange),
          };
        });
        const spacing = token => {
          const probe = document.createElement('span');
          probe.style.cssText = `position:absolute;display:block;width:var(${token});min-width:0;padding:0;border:0;transition:none!important;animation:none!important`;
          element.append(probe);
          const value = probe.getBoundingClientRect().width;
          probe.remove();
          return value;
        };
        const compactGap = spacing('--space-sm');
        const verticalGap = spacing('--space-xs');
        const separatorGap = spacing('--space-micro');
        const inspectorCap = spacing('--size-map-stop-inspector');
        const verticalGaps = [
          ...element.querySelectorAll(
            '.fleet-map-next-load-card__header, .fleet-route-popup__location, .fleet-route-popup__information, .fleet-route-popup__facts, .fleet-route-popup__appointment, .fleet-map-next-load-card__metrics',
          ),
        ].flatMap(container => {
          const children = [...container.children]
            .map(child => ({
              ...rect(child),
              job: child === job,
              detailsLink: child === detailsLink,
              fuel: child.classList.contains('fleet-route-popup__fuel'),
              assignment: child.classList.contains(
                'fleet-map-next-load-card__assignment',
              ),
            }))
            .filter(child => child.height > 0);
          return children.slice(1).map((child, index) => ({
            container: container.className,
            afterJob: children[index].job,
            detailsLink: child.detailsLink,
            extraSmallGap:
              child.fuel || children[index].fuel || child.assignment,
            gap: child.y - children[index].y - children[index].height,
          }));
        });
        const sectionDividerCount = [
          ...(location?.children ?? []),
          ...(information?.children ?? []),
        ].reduce((count, child) => {
          const style = getComputedStyle(child);
          return (
            count +
            ['Top', 'Bottom'].filter(
              side =>
                Number.parseFloat(style[`border${side}Width`]) > 0 &&
                !['none', 'hidden'].includes(style[`border${side}Style`]),
            ).length
          );
        }, 0);
        const distancePadding = Number.parseFloat(
          getComputedStyle(
            element.querySelector('.fleet-map-next-load-card__metrics'),
          ).paddingTop,
        );
        const headerSpacing = Number.parseFloat(
          getComputedStyle(
            element.querySelector('.fleet-map-next-load-card__header'),
          ).marginBottom,
        );
        return {
          card: rect(element),
          stage: stage && rect(stage),
          inspector: {
            ...rect(inspector),
            mode: inspector.dataset.inspectorMode,
            background: inspectorStyle.backgroundColor,
            scrollWidth: inspector.scrollWidth,
            clientWidth: inspector.clientWidth,
            scrollHeight: inspector.scrollHeight,
            clientHeight: inspector.clientHeight,
          },
          inspectorCount: document.querySelectorAll(
            '.fleet-map-inspector.has-selection',
          ).length,
          legacyCardCount: document.querySelectorAll('.fleet-map-details-card')
            .length,
          currentHidden: document.querySelector('#fleet-map-details')?.hidden,
          closeCount: inspector.querySelectorAll(
            '[aria-label="Close map information"]',
          ).length,
          map: rect(document.querySelector('#fleet-map')),
          outsideProvider: !element.closest('#fleet-map'),
          embedded: element.classList.contains('fleet-map-inspector__next'),
          position: style.position,
          background: style.backgroundColor,
          shadow: style.boxShadow,
          scrollWidth: element.scrollWidth,
          clientWidth: element.clientWidth,
          scrollHeight: element.scrollHeight,
          clientHeight: element.clientHeight,
          text: element.textContent,
          appointment: appointment && {
            ...rect(appointment),
            text: text(appointment),
          },
          loadHeader: loadHeader && rect(loadHeader),
          loadReference: loadReference && {
            ...rect(loadReference),
            text: text(loadReference),
          },
          detailsLink: detailsLink && {
            ...rect(detailsLink),
            text: text(detailsLink),
            href: detailsLink.getAttribute('href'),
            lastInInformation: information.lastElementChild === detailsLink,
            borderTopWidth: Number.parseFloat(detailsLinkStyle.borderTopWidth),
            borderTopStyle: detailsLinkStyle.borderTopStyle,
            borderTopColor: detailsLinkStyle.borderTopColor,
            paddingTop: Number.parseFloat(detailsLinkStyle.paddingTop),
            marginTop: Number.parseFloat(detailsLinkStyle.marginTop),
          },
          detailsLinkCount: element.querySelectorAll('a[href^="/dispatch/"]')
            .length,
          job: job && section(job),
          company: company && {
            ...section(company),
            afterJob: company.previousElementSibling === job,
          },
          distanceSection: distanceSection && {
            ...section(distanceSection),
            afterEta: distanceSection.previousElementSibling === eta,
          },
          sectionDividerCount,
          address: address && rect(address),
          addressLines,
          references,
          location: location && { ...rect(location), text: text(location) },
          information: information && {
            ...rect(information),
            text: text(information),
          },
          eta: eta && {
            ...rect(eta),
            rowGap: Number.parseFloat(getComputedStyle(eta).rowGap),
          },
          etaLabel,
          etaTime: etaTime && rect(etaTime),
          distances,
          compactGap,
          verticalGap,
          separatorGap,
          inspectorCap,
          verticalGaps,
          distancePadding,
          headerSpacing,
          appointmentCount: [...element.querySelectorAll('dt')].filter(
            label => text(label) === 'Appointment',
          ).length,
          repeatedAppointmentCount: element.querySelectorAll(
            '.arrival-estimate__appointment',
          ).length,
          currentLoad: document.querySelector(
            '[aria-label="Current dispatch route"]',
          )?.textContent,
          pageWidth: document.documentElement.scrollWidth,
          viewportWidth: document.documentElement.clientWidth,
        };
      });
      const label = `${name} stop ${index + 1}`;
      check(
        metrics.embedded &&
          metrics.outsideProvider &&
          metrics.stage &&
          metrics.position === 'static' &&
          metrics.inspectorCount === 1 &&
          metrics.legacyCardCount === 0 &&
          metrics.closeCount === 1,
        `${label}: one shared map-stage inspector owns the embedded stop details outside the provider`,
      );
      const stage = metrics.stage;
      if (stage) {
        check(
          metrics.inspectorCap > 0 &&
            Math.abs(
              metrics.inspector.width -
                Math.min(stage.width, metrics.inspectorCap),
            ) <= 1 &&
            Math.abs(
              metrics.inspector.x +
                metrics.inspector.width / 2 -
                stage.x -
                stage.width / 2,
            ) <= 1 &&
            Math.abs(metrics.inspector.y - stage.y) <= 1,
          `${label}: shared inspector is centered at the map top within its named width cap`,
        );
        if (width === 2344)
          check(
            metrics.inspector.width < stage.width - 1,
            `${label}: wide maps keep visible space on both sides of the inspector`,
          );
        check(
          metrics.inspector.y + metrics.inspector.height <=
            stage.y + stage.height + 1,
          `${label}: inspector stays vertically bounded by the map stage`,
        );
        check(
          Math.abs(stage.width - metrics.map.width) <= 1 &&
            Math.abs(stage.height - metrics.map.height) <= 1,
          `${label}: provider fills map stage`,
        );
      }
      check(
        metrics.inspector.background !== 'rgba(0, 0, 0, 0)' &&
          metrics.inspector.mode === 'nextstop',
        `${label}: selected stop uses the themed shared inspector surface`,
      );
      check(
        metrics.scrollWidth <= metrics.clientWidth + 1 &&
          metrics.pageWidth <= metrics.viewportWidth + 1,
        `${label}: no horizontal clipping`,
      );
      check(
        metrics.inspector.scrollWidth <= metrics.inspector.clientWidth + 1,
        `${label}: inspector has no horizontal clipping`,
      );
      check(
        metrics.detailsLink &&
          metrics.detailsLink.y >= metrics.card.y &&
          metrics.detailsLink.y + metrics.detailsLink.height <=
            metrics.card.y + metrics.card.height + 1,
        `${label}: route details link remains inside the complete selected-stop body`,
      );
      check(
        metrics.verticalGap > 0 && metrics.verticalGaps.length >= 5,
        `${label}: compact vertical spacing is measurable`,
      );
      check(
        metrics.separatorGap === 2 &&
          metrics.verticalGap === 4 &&
          metrics.compactGap === 8,
        `${label}: separators and columns use the micro, extra-small and small spacing tokens`,
      );
      for (const row of metrics.verticalGaps)
        check(
          Math.abs(
            row.gap -
              (row.afterJob
                ? metrics.separatorGap
                : row.detailsLink || row.extraSmallGap
                  ? metrics.verticalGap
                  : 0),
          ) <= 1,
          `${label}: ${row.container} adds vertical spacing only at section dividers`,
        );
      check(
        metrics.distancePadding === metrics.separatorGap,
        `${label}: distance divider uses micro top padding`,
      );
      check(
        metrics.eta?.rowGap === 0,
        `${label}: ETA rows add no vertical gap`,
      );
      check(
        metrics.headerSpacing === 0,
        `${label}: load header adds no bottom margin`,
      );
      check(
        metrics.currentHidden && metrics.currentLoad === initialCurrent,
        `${label}: truck details remain mounted with unchanged current-load data while hidden by the selected stop`,
      );
      check(
        ['x', 'y', 'width', 'height'].every(
          key => Math.abs(metrics.map[key] - initialMap[key]) <= 1,
        ),
        `${label}: selecting a future stop does not move or resize the map`,
      );
      check(
        metrics.currentLoad.includes('CURRENT-1441') &&
          metrics.currentLoad.includes('AMF1441') &&
          !metrics.currentLoad.includes('FUTURE-1442'),
        `${label}: prefixed current load stays in the header`,
      );
      check(
        metrics.loadHeader &&
          metrics.loadReference?.text.includes('FUTURE-1442') &&
          metrics.loadReference.text.includes('AMF1442') &&
          metrics.loadHeader.y >= metrics.location.y - 1 &&
          metrics.loadHeader.y <= metrics.location.y + 1,
        `${label}: load/order header starts the left column`,
      );
      check(
        !metrics.text.includes(commoditySentinel) &&
          !metrics.text.includes(notesSentinel) &&
          !metrics.text.includes(bolSentinel) &&
          !metrics.text.includes(serviceSentinel),
        `${label}: commodity, BOL and unrelated notes stay outside the stop popup`,
      );
      if (withReferences) {
        const reference = metrics.references[0];
        check(
          metrics.references.length === 1 &&
            new RegExp(`^Appt #\\s*${referenceIds[index]}$`).test(
              reference?.text ?? '',
            ) &&
            !metrics.text.includes(referenceIds[1 - index]),
          `${label}: only the appointment reference for the selected job is shown`,
        );
        check(
          reference &&
            metrics.address &&
            reference.y >= metrics.address.y + metrics.address.height - 1 &&
            reference.x >= metrics.location.x - 1 &&
            reference.x + reference.width <=
              metrics.location.x + metrics.location.width + 1,
          `${label}: appointment reference appears beneath the left address`,
        );
      } else {
        check(
          metrics.references.length === 0 &&
            referenceIds.every(id => !metrics.text.includes(id)),
          `${label}: notes without an appointment number produce no reference row`,
        );
      }
      check(
        metrics.company?.afterJob &&
          metrics.company.borderTopWidth === 0 &&
          metrics.job &&
          metrics.company.y >= metrics.job.y + metrics.job.height - 1,
        `${label}: company follows the pickup/delivery job without a duplicate top divider`,
      );
      check(
        metrics.job &&
          metrics.job.borderBottomWidth === 1 &&
          metrics.job.borderBottomStyle === 'solid' &&
          metrics.job.borderBottomColor ===
            metrics.detailsLink?.borderTopColor &&
          metrics.job.paddingBottom === metrics.separatorGap &&
          metrics.job.marginBottom === metrics.separatorGap,
        `${label}: shared job row has the themed bottom divider and micro spacing`,
      );
      check(
        metrics.distanceSection?.afterEta &&
          metrics.eta &&
          metrics.distanceSection.y >= metrics.eta.y + metrics.eta.height - 1,
        `${label}: distance divider follows ETA`,
      );
      if (withReferences)
        check(
          metrics.references[0]?.afterAddress,
          `${label}: appointment-reference divider follows the address`,
        );
      check(
        metrics.sectionDividerCount === (withReferences ? 7 : 6),
        `${label}: stop, fuel, references, assignment and distance dividers`,
      );
      for (const [name, section] of [
        ['distances', metrics.distanceSection],
        ...metrics.references.map(reference => [
          'appointment reference',
          reference,
        ]),
      ]) {
        check(
          section &&
            section.sectionStart &&
            section.borderTopWidth === 1 &&
            section.borderTopStyle === 'solid' &&
            section.borderTopColor === metrics.detailsLink?.borderTopColor &&
            section.paddingTop === metrics.separatorGap &&
            section.marginTop === 0,
          `${label}: ${name} uses the shared themed divider and micro padding without a margin`,
        );
      }
      check(
        metrics.detailsLinkCount === 1 &&
          metrics.detailsLink?.href === `/dispatch/${futureLoadId}` &&
          metrics.detailsLink.text === 'Route & load details ↗' &&
          metrics.detailsLink.lastInInformation,
        `${label}: route details link is last in the right information column`,
      );
      check(
        metrics.detailsLink &&
          metrics.detailsLink.borderTopWidth >= 1 &&
          metrics.detailsLink.borderTopStyle !== 'none' &&
          Math.abs(metrics.detailsLink.paddingTop - metrics.verticalGap) <= 1 &&
          Math.abs(metrics.detailsLink.marginTop - metrics.verticalGap) <= 1,
        `${label}: route details link has a top divider with compact margin and padding`,
      );
      check(
        metrics.job.text.includes(`Load stop ${index + 1} of 2`) &&
          metrics.appointmentCount === 1 &&
          metrics.repeatedAppointmentCount === 0 &&
          new RegExp(
            `^Appointment\\s*Sep 9 · ${index === 0 ? '08:00 AM' : '06:00 PM'}$`,
          ).test(metrics.appointment?.text ?? ''),
        `${label}: one compact appointment with selected date/time`,
      );
      check(
        metrics.appointment &&
          metrics.eta &&
          metrics.appointment.y + metrics.appointment.height <=
            metrics.eta.y + 1 &&
          metrics.appointment.height <= 42 &&
          !metrics.location?.text.includes('Appointment'),
        `${label}: compact appointment above ETA in the information column`,
      );
      if (width === 1440)
        check(
          metrics.appointment &&
            metrics.loadReference &&
            Math.abs(metrics.appointment.y - metrics.loadReference.y) <= 2,
          `${label}: Appointment aligns with Load/Order at the top of the card`,
        );
      check(
        metrics.addressLines.length === 2 &&
          metrics.addressLines[0].text === futureStops[index].address &&
          metrics.addressLines[1].text ===
            `${futureStops[index].city}, ON, Canada` &&
          metrics.addressLines[1].y >=
            metrics.addressLines[0].y + metrics.addressLines[0].height - 1,
        `${label}: street and locality occupy separate address lines`,
      );
      check(
        metrics.information?.text.includes(
          index === 0 ? 'ETA Sep 9 · 08:10 AM' : 'ETA Sep 9 · 06:00 PM',
        ) &&
          metrics.information.text.includes('local · UTC-04:00') &&
          metrics.information.text.includes(
            index === 0 ? 'Late by 10m' : 'On time',
          ),
        `${label}: information column shows selected stop ETA and lateness`,
      );
      if (width === 1440)
        check(
          metrics.information &&
            metrics.location &&
            metrics.information.x >=
              metrics.location.x + metrics.location.width - 1,
          `${label}: ETA sits to the right of the stop location`,
        );
      if (width === 1440)
        check(
          metrics.etaLabel &&
            metrics.etaTime &&
            Math.abs(metrics.etaLabel.y - metrics.etaTime.y) <= 4 &&
            metrics.etaTime.x >=
              metrics.etaLabel.x + metrics.etaLabel.width - 1,
          `${label}: ETA label and arrival time share a row`,
        );
      check(
        metrics.etaLabel &&
          metrics.etaTime &&
          metrics.compactGap > 0 &&
          metrics.etaTime.x - metrics.etaLabel.x - metrics.etaLabel.width >=
            -1 &&
          metrics.etaTime.x - metrics.etaLabel.x - metrics.etaLabel.width <=
            metrics.compactGap + 1,
        `${label}: ETA immediately follows its label without a stretched gap`,
      );
      const total = metrics.distances.find(row => row.label === 'Total');
      check(
        total?.value === (index === 0 ? '160 mi · 257 km' : '460 mi · 740 km'),
        `${label}: Total shows cumulative miles and kilometres from truck`,
      );
      check(
        total &&
          Math.abs(total.labelBounds.y - total.valueBounds.y) <= 4 &&
          total.valueBounds.x >=
            total.labelBounds.x + total.labelBounds.width - 1,
        `${label}: Total label and distance share a row`,
      );
      const longestLabel = metrics.distances.reduce(
        (longest, row) =>
          !longest || row.labelTextBounds.width > longest.labelTextBounds.width
            ? row
            : longest,
        null,
      );
      check(
        longestLabel &&
          Math.abs(
            longestLabel.valueTextBounds.x -
              longestLabel.labelTextBounds.x -
              longestLabel.labelTextBounds.width -
              metrics.compactGap,
          ) <= 1,
        `${label}: metric value column starts one small spacing token after the longest label`,
      );
      for (const distance of metrics.distances)
        check(
          longestLabel &&
            Math.abs(
              distance.valueTextBounds.x - longestLabel.valueTextBounds.x,
            ) <= 1,
          `${label}: ${distance.label} value aligns in the shared metric column`,
        );
      check(
        metrics.distances.find(
          row => row.label === (index === 0 ? 'Empty' : 'Leg'),
        )?.value === (index === 0 ? '40 mi · 64 km' : '300 mi · 483 km'),
        `${label}: selected empty or loaded leg distance`,
      );
      check(
        JSON.stringify(metrics.distances.map(row => row.label)) ===
          JSON.stringify([index === 0 ? 'Empty' : 'Leg', 'Total']),
        `${label}: popup contains only selected leg and cumulative distance metrics`,
      );
      await card.locator('button[title="Copy full address"]').click();
      const fullAddress = `${futureStops[index].address}, ${futureStops[index].city}, ON, Canada`;
      await page.waitForFunction(
        address => window.stopDetailsCopiedAddress === address,
        fullAddress,
      );
      await page.mouse.move(0, 0);
      const screenshot = resolve(
        output,
        `${name}-${index === 0 ? 'pickup' : 'delivery'}.png`,
      );
      await page.screenshot({ path: screenshot, fullPage: true });
      await page.locator('.fleet-map-inspector').evaluate(element => {
        element.scrollTop = element.scrollHeight;
      });
      const distanceVisible = await card
        .getByText('Total', { exact: true })
        .evaluate(element => {
          const text = element.parentElement.getBoundingClientRect();
          const card = element
            .closest('.fleet-map-inspector')
            .getBoundingClientRect();
          return text.top >= card.top && text.bottom <= card.bottom;
        });
      check(
        distanceVisible,
        `${label}: distance is reachable inside the scrollable shared inspector`,
      );
      const scrolledScreenshot = resolve(
        output,
        `${name}-${index === 0 ? 'pickup' : 'delivery'}-scrolled.png`,
      );
      await page.screenshot({ path: scrolledScreenshot, fullPage: true });
      if (index === 0 && width !== 2344) {
        await page.evaluate(() => {
          document.documentElement.style.fontSize = '32px';
        });
        for (const expanded of [false, true]) {
          await disclosure.click();
          assert.equal(
            await disclosure.getAttribute('aria-expanded'),
            String(expanded),
          );
          const largeText = await card.evaluate(element => {
            const inspector = element.closest('.fleet-map-inspector');
            const controls = inspector.querySelector(
              '.fleet-map-inspector__controls',
            );
            const bounds = controls.getBoundingClientRect();
            return {
              clipped: inspector.scrollWidth > inspector.clientWidth + 1,
              left: bounds.left,
              right: bounds.right,
              viewport: document.documentElement.clientWidth,
              heights: [...controls.querySelectorAll('button')].map(
                button => button.getBoundingClientRect().height,
              ),
            };
          });
          assert.equal(largeText.clipped, false);
          assert.ok(largeText.left >= 0);
          assert.ok(largeText.right <= largeText.viewport);
          assert.ok(largeText.heights.every(height => height >= 44));
          const addressButton = card.locator('[title="Copy full address"]');
          await addressButton.scrollIntoViewIfNeeded();
          const readableAddress = await addressButton.evaluate(node => {
            const inspector = node.closest('.fleet-map-inspector');
            const panel = inspector.getBoundingClientRect();
            const header = inspector
              .querySelector('.fleet-map-inspector__header')
              .getBoundingClientRect();
            const address = node.getBoundingClientRect();
            return (
              address.top >= Math.max(panel.top, header.bottom) - 1 &&
              address.bottom <= panel.bottom + 1
            );
          });
          assert.equal(readableAddress, true);
          await page.screenshot({
            path: resolve(output, `${name}-${expanded}-large-text.png`),
            fullPage: true,
          });
        }
        await page.evaluate(() => {
          document.documentElement.style.fontSize = '';
        });
      }
      caseResult.stops.push({
        index,
        screenshot,
        scrolledScreenshot,
        distanceVisible,
        ...metrics,
      });
      if (index === 0)
        await page
          .getByRole('button', { name: 'Back to truck', exact: true })
          .click();
      else {
        await card.focus();
        await page.keyboard.press('Escape');
      }
      await card.waitFor({ state: 'detached' });
      check(
        (await current.textContent()) === metrics.currentLoad,
        `${label}: dismissal retains current route`,
      );
      check(
        (await page
          .locator('.fleet-map-inspector')
          .getAttribute('data-inspector-mode')) === 'truck',
        `${label}: Back and Escape return to the same truck inspector`,
      );
      const restoredMap = await page.locator('#fleet-map').boundingBox();
      check(
        ['x', 'y', 'width', 'height'].every(
          key => Math.abs(restoredMap[key] - initialMap[key]) <= 1,
        ),
        `${label}: returning to truck keeps map geometry unchanged`,
      );
    }
    check(
      (await page.evaluate(
        () =>
          window.stopDetailsFixture.inspectorModes.filter(
            mode => mode === 'truck',
          ).length,
      )) >= 2,
      `${name}: Back and Escape restore truck mode in the map adapter`,
    );
    await page
      .getByRole('button', { name: 'Close map information', exact: true })
      .click();
    check(
      (await page.evaluate(() => window.stopDetailsFixture.clearCount)) >= 1,
      `${name}: shared Close clears map inspection`,
    );
    check(
      (await page
        .locator('.fleet-map-inspector')
        .getAttribute('data-inspector-mode')) === 'closed' &&
        (await current.count()) === 0,
      `${name}: closing the restored truck inspector deselects its route`,
    );
    report.cases.push(caseResult);
    await writeFile(
      resolve(output, 'report.json'),
      JSON.stringify(report, null, 2),
    );
    await context.close();
    console.log(
      `Stop details smoke ${name}: pickup, delivery, close and Escape checked.`,
    );
  }
  assert.deepEqual(
    report.unexpectedRequests,
    [],
    'Offline stop details smoke attempted unexpected network or writes',
  );
  assert.deepEqual(
    report.browserErrors,
    [],
    'Staged UI emitted browser errors',
  );
  assert.deepEqual(report.failures, [], 'Stop details UI regressions');
} catch (error) {
  report.failures.push(error.message);
  throw error;
} finally {
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  await browser.close();
}
console.log(`Offline stop details report: ${resolve(output, 'report.json')}`);
