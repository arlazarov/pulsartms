import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import {
  currentRouteColor,
  futureRouteColor,
} from '../../Scripts/fleetMap/rendering/routePalette.ts';

const output = browserOutput('stop-cards', process.env.STOP_CARD_OUTPUT_DIR);
const origin = 'http://stop-cards.invalid';
const bundle = await build({
  entryPoints: ['tests/browser/stopCardsFixture.js'],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
const css = await readFile('wwwroot/css/main.css', 'utf8');
const html = `<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="/styles.css">
<style>body{margin:0;background:var(--ui-canvas);color:var(--ui-text);font-family:Arial,sans-serif}
header{padding:24px 40px}header h1{font-size:20px;font-weight:400;margin:0 0 8px}header p{font-size:14px;margin:0}
#map{position:relative;width:100%;height:580px;border-block:1px solid var(--ui-border-subtle);background-color:var(--ui-surface-muted);
background-image:repeating-linear-gradient(0deg,transparent 0 79px,var(--ui-surface) 79px 83px,var(--ui-border-subtle) 83px 84px,transparent 84px 160px),
repeating-linear-gradient(90deg,transparent 0 99px,var(--ui-surface) 99px 103px,var(--ui-border-subtle) 103px 104px,transparent 104px 200px),
linear-gradient(120deg,transparent 0 46%,var(--ui-selected) 46% 58%,transparent 58%)}
#fuel{position:relative;bottom:auto;left:auto;transform:none;margin:28px auto;width:min(520px,calc(100% - 24px))}
</style></head><body><header><h1>Compact trucks and selected stop cards</h1>
<p>11001–11003: moving · 11004: idle · 11005: off · 11006: selected moving</p>
<p>Production GPU scene and fuel popup • offline synthetic map-like background • no map providers</p></header>
<div id="map"></div><section id="fuel" class="fleet-map-details-card"><div class="fleet-map-details-card__body"></div></section>
<script type="module" src="/fixture.js"></script></body></html>`;
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
  args: ['--enable-unsafe-swiftshader'],
});
const report = {
  scope:
    'Production GPU trucks and unlabeled selectable stops, current marker click and actual details card on an offline orthographic canvas; compiled CSS and fuel popup. Only the Google gesture-isolation helper is stubbed; no provider integration.',
  cases: [],
  circleChecks: [],
  popupCases: [],
  fuelMobileCases: [],
  fuelSingleCases: [],
  hoursCases: [],
  constrainedCases: [],
  failures: [],
  browserErrors: [],
  blockedRequests: [],
};
async function fuelVisitBounds(card) {
  const bounds = await card.evaluate(element => {
    const rect = node => {
      const value = node.getBoundingClientRect();
      return {
        left: value.left,
        right: value.right,
        top: value.top,
        bottom: value.bottom,
        width: value.width,
        height: value.height,
        clientWidth: node.clientWidth,
        scrollWidth: node.scrollWidth,
      };
    };
    return {
      card: {
        ...rect(element),
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
      },
      visitArea: rect(element.querySelector('.fleet-station-popup__visits')),
      visits: [...element.querySelectorAll('.fleet-fuel-visit')].map(visit => ({
        ...rect(visit),
        levels: rect(visit.querySelector('.fleet-fuel-visit__levels')),
        columns: [
          ...visit.querySelector('.fleet-fuel-visit__levels').children,
        ].map(rect),
        rows: [
          ...visit.querySelectorAll(
            '.fleet-fuel-visit__heading, .fleet-fuel-visit__gauge, .fleet-fuel-visit__action',
          ),
        ]
          .filter(node => node.getClientRects().length)
          .map(rect),
        dials: [...visit.querySelectorAll('.fleet-fuel-visit__dial')].map(rect),
      })),
    };
  });
  assert.ok(
    bounds.card.scrollWidth <= bounds.card.clientWidth + 1 &&
      bounds.card.scrollHeight <= bounds.card.clientHeight + 1,
    'ordinary fuel card has no internal scrolling',
  );
  for (const visit of bounds.visits) {
    assert.ok(
      visit.scrollWidth <= visit.clientWidth + 1,
      'fuel visit does not clip its contents',
    );
    assert.ok(
      visit.levels.left >= visit.left && visit.levels.right <= visit.right,
      'fuel levels stay inside their visit',
    );
    assert.equal(
      visit.columns.length,
      3,
      'arrival, purchase and departure retain distinct columns',
    );
    assert.ok(
      visit.columns[0].right <= visit.columns[1].left &&
        visit.columns[1].right <= visit.columns[2].left,
      'arrival, purchase and departure never overlap',
    );
    assert.ok(
      Math.abs(
        (visit.columns[1].left +
          visit.columns[1].right -
          visit.levels.left -
          visit.levels.right) /
          2,
      ) <= 1,
      'purchase is centered between the two fuel gauges',
    );
    assert.ok(
      visit.dials.length === 2 &&
        visit.dials.every(
          dial =>
            Math.abs(dial.width - 64) <= 1 && Math.abs(dial.height - 64) <= 1,
        ),
      'planned fuel popup retains two prominent64px round gauges',
    );
    assert.ok(
      visit.left >= bounds.card.left && visit.right <= bounds.card.right,
      'fuel visit stays within the card',
    );
    assert.ok(
      Math.abs(visit.dials[0].top - visit.dials[1].top) <= 1,
      'arrival and fueled gauges align',
    );
    for (const row of visit.rows)
      assert.ok(
        row.scrollWidth <= row.clientWidth + 1 &&
          row.left >= visit.left &&
          row.right <= visit.right,
        'fuel header, gauges and purchase fit within the visit',
      );
  }
  return bounds;
}
async function forecastAppearance(popup) {
  return popup.evaluate(element => ({
    text: element.textContent,
    values: [
      ...element.querySelectorAll(
        '.stop-hours__value, .stop-hours__status, .fleet-route-popup__eta, .fleet-route-popup__status',
      ),
    ].map(node => {
      const style = getComputedStyle(node);
      return {
        text: node.textContent,
        className: node.className,
        color: style.color,
        background: style.backgroundColor,
      };
    }),
  }));
}
async function waitForRenderedFuel(page, numbers, afterFrame = 0) {
  // Scene updates and Deck drawing use separate frames; an unrelated frame does not acknowledge a fuel update.
  await page.waitForFunction(
    ({ numbers, afterFrame }) => {
      const rendered = window.fixtureRenderedFuel;
      return (
        rendered?.frame > afterFrame &&
        rendered.loaded &&
        JSON.stringify(rendered.visitNumbers) === JSON.stringify(numbers)
      );
    },
    { numbers, afterFrame },
  );
}
async function waitForRenderedStops(page, labels, afterFrame = 0) {
  await page.waitForFunction(
    ({ labels, afterFrame }) => {
      const rendered = window.fixtureRenderedStops;
      return (
        rendered?.frame > afterFrame &&
        rendered.loaded &&
        JSON.stringify(rendered.labels) === JSON.stringify(labels)
      );
    },
    { labels, afterFrame },
  );
}
async function circlePixelBounds(page, screenshot) {
  const circles = (await page.evaluate(() => window.fixtureStopMetrics()))
    .filter(layer => layer.type === 'icon')
    .flatMap(layer => layer.rows)
    .map(circle => ({
      ...circle,
      fill: (circle.label === '1'
        ? currentRouteColor
        : futureRouteColor(0)
      ).slice(0, 3),
    }));
  const background = await page.locator('#map').evaluate(element => {
    const previous = {
      image: element.style.backgroundImage,
      color: element.style.backgroundColor,
    };
    // Isolate actual GPU stop pixels from roads and the decorative fixture grid.
    element.style.backgroundImage = 'none';
    element.style.backgroundColor = '#182030';
    return previous;
  });
  try {
    const before = await page.evaluate(() => {
      const frame = window.fixtureFrames;
      window.fixtureCircleOnly(true);
      return frame;
    });
    await waitForRenderedStops(
      page,
      circles.map(circle => circle.label).sort(),
      before,
    );
    const png = await page
      .locator('#map')
      .screenshot({ path: resolve(output, screenshot) });
    return await page.evaluate(
      async ({ png, circles }) => {
        const bytes = Uint8Array.from(atob(png), value => value.charCodeAt(0));
        const bitmap = await createImageBitmap(
          new Blob([bytes], { type: 'image/png' }),
        );
        const canvas = document.createElement('canvas');
        canvas.width = bitmap.width;
        canvas.height = bitmap.height;
        const context = canvas.getContext('2d');
        context.drawImage(bitmap, 0, 0);
        bitmap.close();
        const pixels = context.getImageData(
          0,
          0,
          canvas.width,
          canvas.height,
        ).data;
        const density =
          canvas.width /
          document.getElementById('map').getBoundingClientRect().width;
        return circles.map(circle => {
          const cx = circle.x * density,
            cy = circle.y * density,
            margin = 20 * density;
          let left = Infinity,
            right = -Infinity,
            top = Infinity,
            bottom = -Infinity,
            count = 0;
          for (
            let y = Math.floor(cy - margin);
            y <= Math.ceil(cy + margin);
            y++
          )
            for (
              let x = Math.floor(cx - margin);
              x <= Math.ceil(cx + margin);
              x++
            ) {
              const offset = (y * canvas.width + x) * 4;
              const minimum = Math.min(
                pixels[offset],
                pixels[offset + 1],
                pixels[offset + 2],
              );
              const maximum = Math.max(
                pixels[offset],
                pixels[offset + 1],
                pixels[offset + 2],
              );
              if (minimum >= 200 && maximum - minimum <= 15) count++;
              if (
                [24, 32, 48].every(
                  (channel, index) =>
                    Math.abs(pixels[offset + index] - channel) <= 24,
                )
              )
                continue;
              left = Math.min(left, x);
              right = Math.max(right, x);
              top = Math.min(top, y);
              bottom = Math.max(bottom, y);
            }
          let fillPixels = 0;
          for (let y = top; y <= bottom; y++)
            for (let x = left; x <= right; x++) {
              const offset = (y * canvas.width + x) * 4;
              if (
                circle.fill.every(
                  (channel, index) =>
                    Math.abs(pixels[offset + index] - channel) <= 8,
                )
              )
                fillPixels++;
            }
          return {
            ...circle,
            density,
            whitePixels: count,
            fillPixels,
            width: right - left + 1,
            height: bottom - top + 1,
            centerOffsetX: (left + right + 1) / 2 - cx,
            centerOffsetY: (top + bottom + 1) / 2 - cy,
          };
        });
      },
      { png: png.toString('base64'), circles },
    );
  } finally {
    const before = await page.evaluate(() => {
      const frame = window.fixtureFrames;
      window.fixtureCircleOnly(false);
      return frame;
    });
    await waitForRenderedFuel(page, ['1/2'], before);
    await page.locator('#map').evaluate((element, previous) => {
      element.style.backgroundImage = previous.image;
      element.style.backgroundColor = previous.color;
    }, background);
  }
}
async function openFixture(width, theme, density = 1, height = 980) {
  const context = await browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: density,
    colorScheme: theme,
    serviceWorkers: 'block',
  });
  await context.addInitScript(
    theme =>
      document.addEventListener('DOMContentLoaded', () => {
        document.documentElement.dataset.theme = theme;
      }),
    theme,
  );
  await context.addInitScript(() =>
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: async text => {
          window.currentStopCopiedText = text;
        },
      },
    }),
  );
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.origin === origin && route.request().method() === 'GET') {
      const resource =
        url.pathname === '/'
          ? ['text/html', html]
          : url.pathname === '/fixture.js'
            ? ['text/javascript', bundle.outputFiles[0].text]
            : url.pathname === '/styles.css'
              ? ['text/css', css]
              : null;
      if (resource) {
        await route.fulfill({
          status: 200,
          contentType: resource[0],
          body: resource[1],
        });
        return;
      }
    }
    report.blockedRequests.push(
      `${route.request().method()} ${url.origin}${url.pathname}`,
    );
    await route.abort('blockedbyclient');
  });
  const page = await context.newPage();
  page.on('pageerror', error => report.browserErrors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') report.browserErrors.push(message.text());
  });
  await page.goto(origin);
  await page.waitForFunction(
    () => window.fixtureFrames >= 1 && window.fixtureReport?.().length === 0,
  );
  await page.waitForFunction(
    () => window.fixtureTruckReport?.()?.loaded === true,
  );
  await waitForRenderedFuel(page, ['1/2']);
  await waitForRenderedStops(page, ['1', '2', '3']);
  await page.evaluate(() => document.fonts.ready);
  return { context, page };
}
try {
  for (const theme of ['light', 'dark'])
    for (const density of [1, 2]) {
      const { context, page } = await openFixture(1320, theme, density);
      const cards = await page.evaluate(() => window.fixtureReport());
      const trucks = (await page.evaluate(() => window.fixtureTruckReport()))
        .trucks;
      assert.equal(
        cards.length,
        0,
        'selected and unselected stops have no floating labels',
      );
      const markers = await page.evaluate(() => window.fixtureMarkerReport());
      assert.ok(
        markers.every(marker => marker.distance === null),
        'all stop information belongs to the bottom popup',
      );
      assert.equal(markers[0].number, '1');
      assert.equal(
        markers[0].clickable,
        true,
        'current numbered circle retains its popup action',
      );
      assert.deepEqual(
        markers.map(marker => marker.number),
        ['1', '2', '3'],
      );
      assert.ok(
        markers
          .slice(1)
          .every(marker => marker.highlighted && marker.clickable),
        'selected future pickup and delivery remain highlighted and selectable',
      );
      const stopMetrics = await page.evaluate(() =>
        window.fixtureStopMetrics(),
      );
      const circles = stopMetrics.filter(layer => layer.id.endsWith('-points'));
      const numbers = stopMetrics.filter(layer =>
        layer.id.endsWith('-numbers'),
      );
      assert.equal(circles.length, 3);
      assert.ok(
        circles.every(
          layer =>
            layer.type === 'icon' &&
            layer.numberSize === 34 &&
            layer.sizeUnits === 'pixels' &&
            layer.pickable,
        ),
        'fixed-diameter stop circles remain selectable',
      );
      for (const circle of circles) {
        assert.deepEqual(circle.icon, {
          x: 0,
          y: 0,
          width: 136,
          height: 136,
          anchorX: 68,
          anchorY: 68,
          mask: false,
        });
      }
      assert.equal(numbers.length, 3);
      assert.ok(
        numbers.every(layer => layer.numberSize === 15 && layer.pickable),
        'the whole number badge is selectable',
      );
      assert.deepEqual(numbers.flatMap(layer => layer.labels).sort(), [
        '1',
        '2',
        '3',
      ]);
      assert.ok(
        numbers.every(
          layer =>
            layer.background === false &&
            JSON.stringify(layer.textColor) === '[255,255,255,255]',
        ),
        'white digits cannot add a text-sized background',
      );
      for (const circle of circles)
        assert.deepEqual(
          circle.rows,
          numbers.find(
            layer => layer.id === circle.id.replace(/-points$/, '-numbers'),
          ).rows,
          'circle and number share their geographic position and pixel offset',
        );
      const roads = await page.evaluate(() => window.fixtureRoadReport());
      assert.equal(roads.length, 2);
      assert.ok(
        roads.every(road => road.width === 5),
        'selection preserves the five-pixel future routes',
      );
      assert.deepEqual(
        roads.map(road => road.color),
        // An empty run reads in the slate the appearance table gives every
        // empty road; the loaded one keeps its place in the route series.
        [[100, 116, 139, 235], futureRouteColor(0)],
        'future road and stop colors match while empty-route styling stays unchanged',
      );
      await page.evaluate(() => window.fixtureRefresh());
      assert.deepEqual(await page.evaluate(() => window.fixtureReport()), []);
      assert.deepEqual(
        await page.evaluate(() => window.fixtureMarkerReport()),
        markers,
        'polling keeps numbered circles, selected emphasis and no labels',
      );
      await page.evaluate(() => window.fixtureToggleFuture(false));
      await page.waitForFunction(() => window.fixtureRoadReport().length === 0);
      assert.deepEqual(await page.evaluate(() => window.fixtureReport()), []);
      assert.ok(
        (await page.evaluate(() => window.fixtureMarkerReport())).every(
          marker => !marker.highlighted,
        ),
      );
      await page.evaluate(() => window.fixtureToggleFuture(true));
      await page.waitForFunction(
        () =>
          window.fixtureRoadReport().length === 2 &&
          window.fixtureRoadReport().every(road => road.visible === true),
      );
      assert.deepEqual(await page.evaluate(() => window.fixtureReport()), []);
      const unselectedRoads = await page.evaluate(() =>
        window.fixtureRoadReport(),
      );
      assert.ok(
        unselectedRoads.every(road => road.width === 5),
        'unselected routes retain the 5px visibility floor',
      );
      await page.evaluate(() => window.fixtureSelectFuture());
      await page.waitForFunction(expected => {
        const actual = window.fixtureRoadReport();
        return (
          actual.length === expected.length &&
          actual.every(
            (road, index) =>
              road.id === expected[index].id &&
              road.width === expected[index].width,
          )
        );
      }, roads);
      assert.deepEqual(
        await page.evaluate(() => window.fixtureRoadReport()),
        roads,
        'reopening retains roads and click selection restores their emphasis',
      );
      assert.deepEqual(
        await page.evaluate(() => window.fixtureMarkerReport()),
        markers,
      );
      assert.deepEqual(await page.evaluate(() => window.fixtureReport()), []);
      assert.equal(trucks.length, 6);
      for (const truck of trucks) {
        assert.equal(truck.size, 28);
        assert.equal(truck.textureWidth, 112);
        assert.equal(truck.textureHeight, 120);
        assert.equal(truck.labelSize, 13);
        assert.equal(truck.labelPhysicalFontSize, 13 * density);
        assert.deepEqual(truck.labelPadding, [9, 4]);
        assert.deepEqual(
          truck.labelBackground,
          truck.unit === '11006' ? [49, 94, 234] : [30, 41, 59],
        );
      }
      assert.deepEqual(
        trucks.map(truck => truck.engine),
        ['on', 'on', 'on', 'idle', 'off', 'on'],
      );
      assert.deepEqual(
        trucks.map(truck => truck.angle || 0),
        [0, -45, -90, 0, 0, -225],
      );
      const screenshot = `${theme}-${density}x.png`;
      await page.screenshot({
        path: resolve(output, screenshot),
        fullPage: true,
      });
      const circleScreenshots = [
        `${theme}-${density}x-circles-single.png`,
        `${theme}-${density}x-circles-double.png`,
      ];
      const singleDigitCircles = await circlePixelBounds(
        page,
        circleScreenshots[0],
      );
      const beforeNumbers = await page.evaluate(() => {
        const frame = window.fixtureFrames;
        window.fixtureStopNumbers(9);
        return frame;
      });
      await waitForRenderedStops(page, ['1', '10', '11'], beforeNumbers);
      const doubleDigitCircles = await circlePixelBounds(
        page,
        circleScreenshots[1],
      );
      report.circleChecks.push({
        theme,
        density,
        screenshots: circleScreenshots,
        singleDigitCircles,
        doubleDigitCircles,
      });
      assert.deepEqual(singleDigitCircles.map(circle => circle.label).sort(), [
        '1',
        '2',
        '3',
      ]);
      assert.deepEqual(doubleDigitCircles.map(circle => circle.label).sort(), [
        '1',
        '10',
        '11',
      ]);
      for (const circle of [...singleDigitCircles, ...doubleDigitCircles]) {
        assert.ok(
          circle.whitePixels > 20 * density * density,
          `${circle.job} ${circle.label}: visible white circle border is rendered`,
        );
        assert.ok(
          circle.fillPixels > 20 * density * density,
          `${circle.job} ${circle.label}: rendered circle retains its route color`,
        );
        assert.ok(
          Math.abs(circle.width - circle.height) <= 1,
          `${circle.job} ${circle.label}: rendered circle width and height agree within one physical pixel`,
        );
        assert.ok(
          circle.width / density >= 33 && circle.width / density <= 35,
          `${circle.job} ${circle.label}: rendered circle keeps its 34px diameter`,
        );
        assert.ok(
          Math.abs(circle.centerOffsetX) <= density &&
            Math.abs(circle.centerOffsetY) <= density,
          `${circle.job} ${circle.label}: circle remains centered on its intended position`,
        );
      }
      for (const [single, double] of [
        ['2', '10'],
        ['3', '11'],
      ]) {
        const first = singleDigitCircles.find(
          circle => circle.label === single,
        );
        const second = doubleDigitCircles.find(
          circle => circle.label === double,
        );
        assert.equal(first.job, second.job);
        assert.equal(
          first.width,
          second.width,
          `${first.job}: a second digit does not widen the circle`,
        );
        assert.equal(
          first.height,
          second.height,
          `${first.job}: a second digit does not resize the circle`,
        );
      }
      const beforeRestore = await page.evaluate(() => {
        const frame = window.fixtureFrames;
        window.fixtureStopNumbers(1);
        return frame;
      });
      await waitForRenderedStops(page, ['1', '2', '3'], beforeRestore);
      const fuel = await page.evaluate(() => window.fixtureFuelReport());
      assert.deepEqual(
        fuel.labels,
        ['fuel-recommendation-numbers'],
        'only fuel-order badges appear on the map; prices stay in the popup',
      );
      assert.deepEqual(fuel.badge.text, ['Fuel 1/2']);
      assert.deepEqual(fuel.badge.offset, [0, -24]);
      assert.equal(fuel.badge.size, 12);
      assert.equal(fuel.badge.fontSize, 12 * density);
      assert.deepEqual(fuel.badge.padding, [6, 4]);
      assert.equal(
        fuel.badge.radius,
        4,
        'fuel badges are rounded rectangles, not pickup/delivery circles',
      );
      assert.deepEqual(fuel.badge.background, [30, 41, 59]);
      assert.equal(fuel.badge.pickable, true);
      assert.deepEqual(
        fuel.visitNumbers,
        ['1/2'],
        'the selected station retains its popup visit order',
      );
      assert.deepEqual(
        fuel.points,
        [
          { id: 'fuel-points', stations: ['ordinary-fuel'] },
          { id: 'fuel-recommendation-points', stations: ['recommended-fuel'] },
        ],
        'each station fill is rendered exactly once in its matching layer',
      );
      const fillIndex = fuel.layerOrder.indexOf('fuel-recommendation-points');
      const ringIndex = fuel.layerOrder.indexOf('fuel-recommendation-rings');
      const badgeIndex = fuel.layerOrder.indexOf('fuel-recommendation-numbers');
      assert.ok(
        roads.every(
          road =>
            fuel.layerOrder.indexOf('fuel-points') >
            fuel.layerOrder.indexOf(road.id),
        ),
        'ordinary station points also cover route paths',
      );
      assert.ok(
        fillIndex > fuel.layerOrder.indexOf('fuel-points') &&
          roads.every(road => fillIndex > fuel.layerOrder.indexOf(road.id)) &&
          ringIndex > fillIndex,
        'recommended fills and rings remain above ordinary stations and route paths',
      );
      assert.ok(badgeIndex > ringIndex);
      assert.ok(
        fuel.layerOrder.every(
          (id, index) =>
            !(id.startsWith('route-stop-') || id.startsWith('truck-')) ||
            index > badgeIndex,
        ),
        'pickup/delivery circles and trucks retain priority over fuel recommendations',
      );
      assert.deepEqual(
        fuel.ring.positions,
        [[120, 0]],
        'the recommended station keeps its GPU ring',
      );
      assert.ok(
        fuel.ring.visible &&
          fuel.ring.pickable &&
          fuel.ring.radius > 0 &&
          fuel.ring.width > 0,
      );
      const fuelPoint = await page.evaluate(() => window.fixtureFuelPoint());
      await page.mouse.click(fuelPoint.x, fuelPoint.y + fuel.badge.offset[1]);
      const fuelCard = page.getByRole('dialog', {
        name: 'Map details',
        exact: true,
      });
      await fuelCard.locator('.fleet-station-popup__title').waitFor();
      assert.equal(
        await fuelCard.locator('.fleet-station-popup__title').textContent(),
        'Recommended fuel stop',
      );
      assert.equal(
        await fuelCard.locator('.fleet-station-popup__discount').textContent(),
        '3.800',
      );
      assert.equal(
        await fuelCard.locator('.fleet-station-popup__ifta').textContent(),
        '3.600',
      );
      const fuelAddress = fuelCard.locator('.fleet-station-popup__address');
      assert.deepEqual(
        await fuelAddress
          .locator('.fleet-station-popup__address-line')
          .allTextContents(),
        ['200 Example Road', 'Albany, NY'],
      );
      const addressLines = await fuelAddress
        .locator('.fleet-station-popup__address-line')
        .evaluateAll(lines =>
          lines.map(line => {
            const rect = line.getBoundingClientRect();
            return {
              left: rect.left,
              top: rect.top,
              bottom: rect.bottom,
              clientWidth: line.clientWidth,
              scrollWidth: line.scrollWidth,
            };
          }),
        );
      assert.ok(
        addressLines[1].top >= addressLines[0].bottom - 1 &&
          Math.abs(addressLines[1].left - addressLines[0].left) <= 1,
        'fuel popup has aligned street and locality on separate lines',
      );
      assert.ok(
        addressLines.every(line => line.scrollWidth <= line.clientWidth + 1),
        'fuel address lines do not clip',
      );
      await fuelAddress.click();
      assert.equal(
        await page.evaluate(() => window.currentStopCopiedText),
        '200 Example Road, Albany, NY',
        'fuel address copy preserves the complete original address',
      );
      assert.deepEqual(
        await fuelCard.locator('.fleet-fuel-visit__number').allTextContents(),
        ['1', '2'],
      );
      assert.deepEqual(
        await fuelCard.locator('.fleet-fuel-visit__buy').allTextContents(),
        ['35 US gal', '164 US gal'],
      );
      assert.deepEqual(
        await fuelCard.locator('.fleet-fuel-visit__level').allTextContents(),
        ['20%', '38%', '13%', '95%'],
      );
      const fuelDistance = fuelCard
        .locator('.fleet-fuel-visit__distance')
        .first();
      assert.equal(await fuelDistance.textContent(), '10 mi · 16 km');
      assert.equal(
        await fuelDistance.getAttribute('aria-label'),
        '10 mi · 16 km away',
      );
      assert.equal(
        await fuelDistance.isVisible(),
        true,
        'station distance remains visible inside its popup',
      );
      await page.evaluate(() => window.fixtureFuelProgress());
      assert.equal(await fuelDistance.textContent(), '5 mi · 8 km');
      assert.equal(
        await fuelDistance.getAttribute('aria-label'),
        '5 mi · 8 km away',
      );
      assert.deepEqual(
        await page.evaluate(() => window.fixtureFuelReport()),
        fuel,
        'mileage refresh preserves the ring without restoring floating labels',
      );
      assert.deepEqual(
        await page.evaluate(() => window.fixtureRoadReport()),
        roads,
        'station selection and mileage refresh do not change route emphasis',
      );
      const fuelScreenshot = `${theme}-${density}x-recommended-fuel.png`;
      await fuelCard.screenshot({ path: resolve(output, fuelScreenshot) });
      const fuelBounds = await fuelCard.evaluate(element => ({
        width: element.clientWidth,
        scrollWidth: element.scrollWidth,
        height: element.clientHeight,
        scrollHeight: element.scrollHeight,
      }));
      assert.ok(
        fuelBounds.scrollWidth <= fuelBounds.width + 1 &&
          fuelBounds.scrollHeight <= fuelBounds.height + 1,
        'ordinary two-visit fuel popup fits without internal scrolling',
      );
      const visits = await fuelVisitBounds(fuelCard);
      report.cases.push({
        theme,
        density,
        screenshot,
        circleScreenshots,
        singleDigitCircles,
        doubleDigitCircles,
        fuelScreenshot,
        fuel,
        cards,
        markers,
        stopMetrics,
        roads,
        unselectedRoads,
        trucks,
        visits,
      });
      await page.evaluate(() => window.fixtureDispose());
      await context.close();
    }
  for (const theme of ['light', 'dark']) {
    const { context, page } = await openFixture(390, theme);
    const point = await page.evaluate(() => window.fixtureFuelPoint());
    await page.mouse.click(point.x, point.y);
    const card = page.getByRole('dialog', { name: 'Map details', exact: true });
    await card.locator('.fleet-fuel-visit').first().waitFor();
    const bounds = await card.evaluate(element => ({
      width: element.clientWidth,
      scrollWidth: element.scrollWidth,
      height: element.clientHeight,
      scrollHeight: element.scrollHeight,
    }));
    await card.screenshot({
      path: resolve(output, `390-${theme}-fuel-before-scroll.png`),
    });
    assert.ok(
      bounds.scrollWidth <= bounds.width + 1 &&
        bounds.scrollHeight <= bounds.height + 1,
      `mobile two-visit fuel popup fits without internal scrolling: ${JSON.stringify(bounds)}`,
    );
    assert.deepEqual(
      await card.locator('.fleet-fuel-visit__number').allTextContents(),
      ['1', '2'],
    );
    assert.deepEqual(
      await card.locator('.fleet-fuel-visit__level').allTextContents(),
      ['20%', '38%', '13%', '95%'],
    );
    const screenshot = `390-${theme}-fuel-gauges.png`;
    await card.screenshot({ path: resolve(output, screenshot) });
    const visits = await fuelVisitBounds(card);
    report.fuelMobileCases.push({ theme, screenshot, bounds, visits });
    await page.evaluate(() => window.fixtureDispose());
    await context.close();
  }
  for (const width of [1320, 390])
    for (const theme of ['light', 'dark']) {
      const { context, page } = await openFixture(width, theme);
      const frames = await page.evaluate(() => window.fixtureFrames);
      await page.evaluate(() => window.fixtureFuelVisits(1));
      await waitForRenderedFuel(page, ['1'], frames);
      const fuel = await page.evaluate(() => window.fixtureFuelReport());
      assert.deepEqual(fuel.visitNumbers, ['1']);
      assert.deepEqual(fuel.labels, ['fuel-recommendation-numbers']);
      assert.deepEqual(
        fuel.badge.text,
        ['Fuel 1'],
        'single-visit badge keeps its actual fuel stop number',
      );
      const point = await page.evaluate(() => window.fixtureFuelPoint());
      await page.mouse.click(point.x, point.y);
      const card = page.getByRole('dialog', {
        name: 'Map details',
        exact: true,
      });
      await card.locator('.fleet-fuel-visit').waitFor();
      assert.deepEqual(
        await card.locator('.fleet-fuel-visit__number').allTextContents(),
        ['1'],
      );
      assert.deepEqual(
        await card.locator('.fleet-fuel-visit__buy').allTextContents(),
        ['35 US gal'],
      );
      assert.deepEqual(
        await card.locator('.fleet-fuel-visit__level').allTextContents(),
        ['20%', '38%'],
      );
      const bounds = await fuelVisitBounds(card);
      assert.equal(bounds.visits.length, 1);
      assert.ok(
        Math.abs(bounds.visits[0].width - bounds.visitArea.width) <= 1,
        'a single fuel visit fills its available row without a blank side area',
      );
      const screenshot = `${width}-${theme}-single-fuel.png`;
      await card.screenshot({ path: resolve(output, screenshot) });
      report.fuelSingleCases.push({ width, theme, screenshot, bounds });
      await page.evaluate(() => window.fixtureDispose());
      await context.close();
    }
  for (const width of [1320, 390])
    for (const theme of ['light', 'dark'])
      for (const job of ['Drop Off', 'Pick Up']) {
        const { context, page } = await openFixture(width, theme);
        await page.evaluate(job => window.fixtureCurrentJob(job), job);
        await page.evaluate(() => window.fixtureFocusCurrent());
        await page.waitForFunction(
          () =>
            window.fixtureCurrentPoint().x > 0 &&
            window.fixtureCurrentPoint().x < window.innerWidth,
        );
        const point = await page.evaluate(() => window.fixtureCurrentPoint());
        await page.mouse.click(point.x, point.y);
        const card = page.getByRole('dialog', {
          name: 'Map details',
          exact: true,
        });
        await card.waitFor();
        const popup = card.locator('.fleet-route-popup--stop');
        await popup.waitFor();
        const loadReference = popup.locator('.fleet-map-route-info__load');
        assert.equal(
          await loadReference.textContent(),
          'Load 1441 · Order: CURRENT-1441',
          'a popup without configured display metadata does not invent a load prefix',
        );
        assert.equal(
          await loadReference.evaluate(
            element => element.parentElement.firstElementChild === element,
          ),
          true,
          'current load and order begin the destination column',
        );
        await loadReference
          .getByRole('button', { name: '1441', exact: true })
          .click();
        assert.equal(
          await page.evaluate(() => window.currentStopCopiedText),
          '1441',
        );
        await loadReference
          .getByRole('button', { name: 'CURRENT-1441', exact: true })
          .click();
        assert.equal(
          await page.evaluate(() => window.currentStopCopiedText),
          'CURRENT-1441',
        );
        const facts = await popup
          .locator('.fleet-route-popup__field')
          .evaluateAll(fields =>
            fields.map(field => {
              const label = field.querySelector('dt'),
                value = field.querySelector('dd');
              const labelRect = label.getBoundingClientRect(),
                valueRect = value.firstElementChild.getBoundingClientRect();
              const spacingProbe = document.createElement('span');
              spacingProbe.style.cssText =
                'position:absolute;width:var(--space-sm)';
              field.append(spacingProbe);
              const compactGap = spacingProbe.getBoundingClientRect().width;
              spacingProbe.remove();
              return {
                label: label.textContent,
                value: value.textContent,
                labelColor: getComputedStyle(label).color,
                labelSize: Number.parseFloat(getComputedStyle(label).fontSize),
                valueSize: Number.parseFloat(getComputedStyle(value).fontSize),
                labelRight: labelRect.right,
                valueLeft: valueRect.left,
                compactGap,
                labelY: labelRect.y,
                valueY: valueRect.y,
              };
            }),
          );
        assert.deepEqual(
          facts.map(fact => fact.label),
          ['Appointment', 'ETA', 'Total'],
        );
        assert.equal(
          facts[0].value,
          'Sep 9 · 07:00 AM – 02:00 PM',
          'same-day appointment window is compact and readable',
        );
        assert.match(facts[1].value, /Sep 9, 06:02 PM.*Late/);
        assert.equal(facts[2].value, '865 mi · 1392 km');
        for (const fact of facts)
          assert.ok(
            fact.valueSize >= fact.labelSize,
            'values have at least the prominence of their labels',
          );
        for (const fact of facts.slice(1)) {
          assert.ok(
            fact.valueLeft >= fact.labelRight,
            `${fact.label} shares one compact row with its value`,
          );
          assert.ok(
            Math.abs(fact.valueY - fact.labelY) <= 4,
            `${fact.label} and its value align on the same row`,
          );
          assert.ok(
            fact.compactGap > 0 &&
              Math.abs(fact.valueLeft - fact.labelRight - fact.compactGap) <= 1,
            `${fact.label} value immediately follows its label using the small spacing token`,
          );
        }
        assert.ok(
          Math.abs(facts[1].valueLeft - facts[2].valueLeft) <= 1,
          'ETA and Total values start in the same column',
        );
        assert.equal(
          await popup.locator('.fleet-route-popup__company').textContent(),
          'Schaeffler Group USA Inc',
        );
        assert.equal(
          await popup.locator('.fleet-route-popup__kind').textContent(),
          job,
        );
        assert.match(
          await popup.locator('.fleet-route-popup__address').textContent(),
          /308 Springhill Farm Rd/,
        );
        assert.deepEqual(
          await popup
            .locator('.fleet-route-popup__address-line')
            .allTextContents(),
          ['308 Springhill Farm Rd, building 3', 'Fort Mill, SC 29715, US'],
        );
        assert.equal(
          await popup.locator('.fleet-route-popup__status').textContent(),
          'Late',
        );
        assert.ok(
          !(await popup.textContent()).includes('Times are local to the stop'),
        );
        assert.ok(
          !(await popup.textContent()).includes('STEEL COILS'),
          'cargo is not repeated in the compact stop card',
        );
        assert.ok(
          !(await popup.textContent()).includes('Service for Load'),
          'service notes stay out of the compact stop card',
        );
        const reference = popup.locator('.fleet-route-popup__reference');
        assert.equal(
          await reference.locator('.fleet-route-popup__label').textContent(),
          'Appt #',
        );
        assert.equal(
          await reference.textContent(),
          `Appt #${job === 'Pick Up' ? 'PU123456' : 'DL654321'}`,
          'the current stop shows only its own pickup or delivery appointment reference',
        );
        assert.ok(
          !(await popup.textContent()).includes('42845601'),
          'BOL is not treated as an appointment reference',
        );
        assert.ok(
          !(await popup.textContent()).includes(
            'appointment confirmation number:',
          ),
          'reference extraction does not repeat full notes',
        );
        const detailsLink = popup.getByRole('link', {
          name: 'Route & load details ↗',
          exact: true,
        });
        assert.equal(
          await detailsLink.getAttribute('href'),
          '/dispatch/33333333-3333-3333-3333-333333333333',
        );
        const geometry = await card.evaluate(element => {
          const rect = node => {
            const { x, y, width, height } = node.getBoundingClientRect();
            return { x, y, width, height };
          };
          const spacingProbe = document.createElement('span');
          spacingProbe.style.cssText =
            'position:absolute;width:var(--space-xs);transition:none!important;animation:none!important';
          const separatorProbe = document.createElement('span');
          separatorProbe.style.cssText =
            'position:absolute;width:var(--space-micro);border-color:var(--ui-border-subtle);transition:none!important;animation:none!important';
          element.append(spacingProbe, separatorProbe);
          const verticalGap = spacingProbe.getBoundingClientRect().width;
          const separatorGap = separatorProbe.getBoundingClientRect().width;
          const separatorColor =
            getComputedStyle(separatorProbe).borderTopColor;
          spacingProbe.remove();
          separatorProbe.remove();
          const verticalGaps = [
            ...element.querySelectorAll(
              '.fleet-route-popup__location, .fleet-route-popup__information, .fleet-route-popup__facts',
            ),
          ].flatMap(container => {
            const children = [...container.children]
              .map(child => ({ ...rect(child), className: child.className }))
              .filter(child => child.height > 0);
            return children.slice(1).map((child, index) => ({
              container: container.className,
              previousClass: children[index].className,
              nextClass: child.className,
              gap: child.y - children[index].y - children[index].height,
            }));
          });
          const separators = [
            ['job', '.fleet-route-popup__kind', 'Bottom'],
            ['reference', '.fleet-route-popup__reference', 'Top'],
            ['distance', '.fleet-route-popup__distance', 'Top'],
          ].map(([name, selector, side]) => {
            const style = getComputedStyle(element.querySelector(selector));
            return {
              name,
              border: Number.parseFloat(style[`border${side}Width`]),
              style: style[`border${side}Style`],
              color: style[`border${side}Color`],
              margin: Number.parseFloat(style[`margin${side}`]),
              padding: Number.parseFloat(style[`padding${side}`]),
            };
          });
          const link = element.querySelector(
            '.fleet-route-popup__details-link',
          );
          const linkStyle = getComputedStyle(link);
          return {
            card: rect(element),
            map: rect(document.querySelector('#map')),
            location: rect(
              element.querySelector('.fleet-route-popup__location'),
            ),
            information: rect(
              element.querySelector('.fleet-route-popup__information'),
            ),
            address: rect(element.querySelector('.fleet-route-popup__address')),
            reference: rect(
              element.querySelector('.fleet-route-popup__reference'),
            ),
            appointment: rect(
              element.querySelector('.fleet-route-popup__appointment'),
            ),
            eta: rect(element.querySelector('.fleet-route-popup__eta')),
            link: rect(link),
            linkBorder: Number.parseFloat(linkStyle.borderTopWidth),
            linkPadding: Number.parseFloat(linkStyle.paddingTop),
            clientWidth: element.clientWidth,
            scrollWidth: element.scrollWidth,
            clientHeight: element.clientHeight,
            scrollHeight: element.scrollHeight,
            pageWidth: document.documentElement.scrollWidth,
            viewportWidth: window.innerWidth,
            verticalGap,
            verticalGaps,
            separatorGap,
            separatorColor,
            separators,
            text: element.textContent,
          };
        });
        assert.ok(
          Math.abs(
            geometry.card.x +
              geometry.card.width / 2 -
              geometry.map.x -
              geometry.map.width / 2,
          ) <= 1,
          'current card remains centered below the map',
        );
        assert.ok(
          Math.abs(
            geometry.map.y +
              geometry.map.height -
              geometry.card.y -
              geometry.card.height -
              (width === 390 ? 0 : 28),
          ) <= 1,
          'current card keeps the shared bottom anchor',
        );
        assert.ok(
          geometry.scrollWidth <= geometry.clientWidth + 1 &&
            geometry.pageWidth <= geometry.viewportWidth + 1,
          'popup has no horizontal clipping',
        );
        assert.ok(
          geometry.scrollHeight <= geometry.clientHeight + 1,
          'ordinary current-stop details fit without an unnecessary internal scrollbar',
        );
        assert.ok(
          geometry.link.y >= geometry.card.y &&
            geometry.link.y + geometry.link.height <=
              geometry.card.y + geometry.card.height,
          'the current route link is visible without scrolling',
        );
        assert.ok(
          geometry.verticalGap > 0 && geometry.verticalGaps.length >= 5,
        );
        assert.ok(
          geometry.separatorGap > 0 &&
            geometry.separatorGap < geometry.verticalGap,
        );
        for (const divider of geometry.separators) {
          assert.equal(
            divider.border,
            1,
            `${divider.name} uses a thin separator`,
          );
          assert.equal(divider.style, 'solid');
          assert.equal(
            divider.color,
            geometry.separatorColor,
            `${divider.name} separator uses the current theme`,
          );
          assert.equal(
            divider.margin,
            divider.name === 'job' ? geometry.separatorGap : 0,
            `${divider.name} separator adds no unnecessary margin`,
          );
          assert.equal(
            divider.padding,
            geometry.separatorGap,
            `${divider.name} separator padding stays compact`,
          );
        }
        for (const row of geometry.verticalGaps) {
          const separator = row.previousClass === 'fleet-route-popup__kind';
          const gap =
            row.nextClass === 'fleet-route-popup__details-link'
              ? geometry.verticalGap
              : separator
                ? geometry.separatorGap
                : 0;
          assert.ok(
            row.gap >= -1 && row.gap <= gap + 1,
            `${row.container} rows have no added vertical gaps except compact separators`,
          );
        }
        assert.ok(
          geometry.link.x >= geometry.information.x - 1 &&
            geometry.link.x + geometry.link.width <=
              geometry.information.x + geometry.information.width + 1,
          'current details link remains in the information column',
        );
        assert.equal(
          geometry.linkBorder,
          1,
          'a visible divider separates the current details link',
        );
        assert.equal(
          geometry.linkPadding,
          geometry.verticalGap,
          'divider uses extra-small spacing',
        );
        assert.ok(
          geometry.reference.y >= geometry.address.y + geometry.address.height,
          'appointment reference follows the address',
        );
        assert.ok(
          Math.abs(geometry.reference.x - geometry.address.x) <= 1,
          'appointment reference stays in the destination column',
        );
        assert.ok(
          Math.abs(geometry.appointment.y - geometry.information.y) <= 1,
          'appointment begins the information column',
        );
        assert.ok(
          Math.abs(geometry.appointment.x - geometry.eta.x) <= 1,
          'appointment and ETA share a compact aligned column',
        );
        assert.ok(
          geometry.eta.y >=
            geometry.appointment.y + geometry.appointment.height,
          'ETA follows appointment without overlap',
        );
        if (width === 1320) {
          assert.equal(
            geometry.card.width,
            600,
            'desktop popup uses the shared compact map-card width',
          );
          assert.ok(
            geometry.information.x >
              geometry.location.x + geometry.location.width,
            'desktop appointment and ETA sit to the right of the destination',
          );
          assert.ok(
            Math.abs(geometry.information.y - geometry.location.y) <= 1,
            'desktop appointment begins alongside the destination heading',
          );
        } else {
          assert.ok(
            geometry.information.y >=
              geometry.address.y + geometry.address.height,
            'mobile appointment and ETA follow the destination without changing the reading order',
          );
        }
        const screenshot = `${width}-${theme}-${job === 'Pick Up' ? 'pickup' : 'delivery'}-current.png`;
        await page.screenshot({
          path: resolve(output, screenshot),
          fullPage: true,
        });
        await card.evaluate(element => {
          element.scrollTop = element.scrollHeight;
        });
        const distanceVisible = await popup
          .locator('.fleet-route-popup__distance dd')
          .evaluate(element => {
            const text = element.getBoundingClientRect();
            const card = element
              .closest('.fleet-map-details-card')
              .getBoundingClientRect();
            return text.top >= card.top && text.bottom <= card.bottom;
          });
        assert.ok(
          distanceVisible,
          'distance value is visible or reachable by scrolling',
        );
        const scrolledScreenshot = `${width}-${theme}-${job === 'Pick Up' ? 'pickup' : 'delivery'}-current-scrolled.png`;
        await page.screenshot({
          path: resolve(output, scrolledScreenshot),
          fullPage: true,
        });
        if (job === 'Drop Off') {
          for (const mode of [
            'short',
            'recap',
            'restart',
            'unknown',
            'pending',
          ]) {
            let beforePending;
            if (mode === 'pending') {
              await page.clock.setFixedTime(new Date('2026-09-08T12:00:00Z'));
              await page.evaluate(() => window.fixtureHours('recap'));
              beforePending = await forecastAppearance(popup);
            }
            await page.evaluate(mode => window.fixtureHours(mode), mode);
            const hours = popup.locator('.stop-hours');
            await hours.waitFor();
            const text = await popup.textContent();
            assert.equal(
              await popup.locator('.fleet-route-popup__eta dt').textContent(),
              'ETA',
            );
            assert.ok(!text.includes('Road ETA'));
            const distanceGap = await popup
              .locator('.fleet-route-popup__distance')
              .evaluate(row => {
                const label = row.querySelector('dt').getBoundingClientRect();
                const value = row.querySelector('dd').getBoundingClientRect();
                return value.left - label.right;
              });
            assert.ok(
              distanceGap >= 0 && distanceGap <= 10,
              'Total uses only the compact token gap',
            );
            assert.ok(
              text.includes('Cycle remaining') && !text.includes('On arrival'),
            );
            assert.ok(
              !text.includes('After service'),
              'service departure balance is not displayed in any stop-card scenario',
            );
            assert.equal(
              await popup.locator('section.stop-hours__cycle').count(),
              1,
            );
            const recap = await popup
              .locator('.stop-hours__recap')
              .evaluate(row => ({
                label: row.querySelector('.stop-hours__label').textContent,
                date: row.querySelector('.stop-hours__value').firstChild
                  .textContent,
                credit: row.querySelector('.stop-hours__value > span')
                  .textContent,
                text: row.textContent,
              }));
            assert.equal(recap.label, 'Next recap');
            assert.match(
              recap.date,
              /^[A-Z][a-z]{2} \d{1,2}$/,
              'recap shows its local date without a clock time',
            );
            assert.equal(recap.credit, '+3h 05m');
            assert.ok(
              !/\d{1,2}:\d{2}|\bhome\b|\bUTC\b/i.test(recap.text),
              'recap excludes time and zone annotations',
            );
            assert.ok(
              !/[\u0400-\u04ff]/u.test(text),
              'hours cards remain English-only',
            );
            const cycleAlignment = await popup
              .locator('.fleet-route-popup__eta')
              .evaluate(row => {
                const label = row.querySelector('dt').getBoundingClientRect();
                const warning = row
                  .querySelector('.fleet-route-popup__cycle-status')
                  .getBoundingClientRect();
                const arrival = row
                  .querySelector('.fleet-route-popup__arrival')
                  .getBoundingClientRect();
                return {
                  labelLeft: label.left,
                  warningLeft: warning.left,
                  warningTop: warning.top,
                  arrivalBottom: arrival.bottom,
                };
              });
            assert.ok(
              Math.abs(cycleAlignment.labelLeft - cycleAlignment.warningLeft) <=
                1,
              'secondary cycle warning begins at the ETA label, not the time value',
            );
            assert.ok(
              cycleAlignment.warningTop >= cycleAlignment.arrivalBottom - 1,
              'cycle warning is below the inline ETA',
            );
            if (mode === 'unknown') {
              assert.equal(
                await popup
                  .locator(
                    '.fleet-route-popup__arrival .fleet-route-popup__status',
                  )
                  .textContent(),
                'Late',
              );
              assert.equal(
                await popup
                  .locator('.fleet-route-popup__cycle-status')
                  .textContent(),
                'Cycle unknown',
              );
            } else assert.ok(text.includes('Cycle short'));
            if (mode === 'recap')
              assert.ok(text.includes('On time with recap'));
            if (mode === 'restart') {
              assert.ok(!text.includes('If reset'));
              assert.equal(
                await popup.locator('.stop-hours__status--success').count(),
                0,
              );
              assert.equal(
                await popup.locator('.stop-hours__status--conditional').count(),
                0,
              );
            }
            if (mode === 'pending') {
              assert.deepEqual(
                await forecastAppearance(popup),
                beforePending,
                'pending keeps current-stop ETA text, statuses, cycle, recap, alternatives and colors unchanged',
              );
              assert.ok(
                !text.includes('Previous:') &&
                  !text.includes('Previously late by') &&
                  !text.includes('Updating'),
              );
              assert.ok(text.includes('−8h 00m'));
            }
            assert.equal(
              await popup.locator('.fleet-route-popup__eta--success').count(),
              0,
              'a conditional road ETA is not a feasible On time claim',
            );
            const bounds = await card.evaluate(element => ({
              width: element.clientWidth,
              scrollWidth: element.scrollWidth,
              height: element.clientHeight,
              scrollHeight: element.scrollHeight,
              text: element.textContent,
            }));
            assert.ok(
              bounds.scrollWidth <= bounds.width + 1,
              'hours card has no horizontal clipping',
            );
            assert.ok(
              bounds.scrollHeight <= bounds.height + 1,
              'ordinary hours card fits without an internal scrollbar',
            );
            const screenshot = `${width}-${theme}-hours-${mode}.png`;
            await card.screenshot({ path: resolve(output, screenshot) });
            report.hoursCases.push({ width, theme, mode, screenshot, bounds });
          }
          await page.evaluate(() => window.fixtureHours('legacy'));
        }
        await detailsLink.evaluate(link =>
          link.addEventListener(
            'click',
            event => {
              event.preventDefault();
              window.currentPopupNavigation = link.pathname;
            },
            { once: true },
          ),
        );
        await detailsLink.click();
        assert.equal(
          await page.evaluate(() => window.currentPopupNavigation),
          '/dispatch/33333333-3333-3333-3333-333333333333',
          'both current pickup and delivery clicks target the exact current dispatch',
        );
        await card
          .getByRole('button', { name: 'Close map details', exact: true })
          .focus();
        await page.keyboard.press('Escape');
        await card.waitFor({ state: 'detached' });
        assert.equal(
          (await page.evaluate(() => window.fixtureMarkerReport()))[0].distance,
          null,
        );
        await page.mouse.click(point.x, point.y);
        await card.waitFor();
        await card
          .getByRole('button', { name: 'Close map details', exact: true })
          .click();
        await card.waitFor({ state: 'detached' });
        report.popupCases.push({
          width,
          theme,
          job,
          screenshot,
          scrolledScreenshot,
          facts,
          geometry,
          distanceVisible,
        });
        await page.evaluate(() => window.fixtureDispose());
        await context.close();
      }
  for (const scenario of [
    { name: 'landscape', width: 700, height: 390 },
    { name: 'long-address', width: 390, height: 700 },
  ]) {
    const { context, page } = await openFixture(
      scenario.width,
      'light',
      1,
      scenario.height,
    );
    if (scenario.name === 'long-address')
      await page.evaluate(() =>
        window.fixtureCurrentAddress(
          `${'Receiving office beyond the north warehouse entrance, '.repeat(8)}Fort Mill, SC 29715, US`,
        ),
      );
    const beforeFocus = await page.evaluate(() => {
      const frames = window.fixtureFrames;
      document.getElementById('map').style.height =
        `${window.innerHeight - document.querySelector('header').getBoundingClientRect().height - 2}px`;
      window.fixtureFocusCurrent();
      return frames;
    });
    await page.waitForFunction(
      frames => window.fixtureFrames > frames,
      beforeFocus,
    );
    await page.waitForFunction(() => {
      const point = window.fixtureCurrentPoint();
      return (
        point.x > 0 &&
        point.x < window.innerWidth &&
        point.y > 0 &&
        point.y < window.innerHeight
      );
    });
    const point = await page.evaluate(() => window.fixtureCurrentPoint());
    await page.mouse.click(point.x, point.y);
    const card = page.getByRole('dialog', { name: 'Map details', exact: true });
    await card.waitFor();
    const geometry = await card.evaluate(element => {
      const card = element.getBoundingClientRect(),
        map = document.getElementById('map').getBoundingClientRect();
      const probe = document.createElement('span');
      probe.style.cssText =
        'position:absolute;width:var(--size-control-touch);transition:none!important;animation:none!important';
      element.append(probe);
      const reserved = probe.getBoundingClientRect().width;
      probe.remove();
      return {
        top: card.top,
        bottom: card.bottom,
        mapTop: map.top,
        mapBottom: map.bottom,
        height: card.height,
        reserved,
        viewportHeight: window.innerHeight,
        scrollHeight: element.scrollHeight,
        clientHeight: element.clientHeight,
        scrollWidth: element.scrollWidth,
        clientWidth: element.clientWidth,
      };
    });
    assert.ok(
      geometry.scrollHeight > geometry.clientHeight + 1,
      `${scenario.name}: genuinely oversized content remains scrollable`,
    );
    assert.ok(
      geometry.top >= geometry.mapTop + geometry.reserved - 1,
      `${scenario.name}: a touch-sized strip of map remains visible`,
    );
    assert.ok(
      geometry.bottom <=
        Math.min(geometry.mapBottom, geometry.viewportHeight) + 1,
      `${scenario.name}: the card stays within its map and viewport`,
    );
    assert.ok(
      geometry.scrollWidth <= geometry.clientWidth + 1,
      `${scenario.name}: no horizontal clipping`,
    );
    await card.evaluate(element => {
      element.scrollTop = element.scrollHeight;
    });
    assert.equal(
      await card
        .getByRole('link', { name: 'Route & load details ↗' })
        .evaluate(element => {
          const link = element.getBoundingClientRect(),
            card = element.closest('[role="dialog"]').getBoundingClientRect();
          return link.top >= card.top && link.bottom <= card.bottom;
        }),
      true,
      `${scenario.name}: all details remain reachable`,
    );
    const screenshot = `${scenario.width}-${scenario.height}-${scenario.name}.png`;
    await page.screenshot({
      path: resolve(output, screenshot),
      fullPage: true,
    });
    await card
      .getByRole('button', { name: 'Close map details', exact: true })
      .click();
    await card.waitFor({ state: 'detached' });
    report.constrainedCases.push({ ...scenario, screenshot, geometry });
    await page.evaluate(() => window.fixtureDispose());
    await context.close();
  }
} catch (error) {
  report.failures.push(error.message);
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
}
console.log(
  JSON.stringify(
    {
      report: resolve(output, 'report.json'),
      gpuCases: report.cases.length,
      popupCases: report.popupCases.length,
      fuelMobileCases: report.fuelMobileCases.length,
      fuelSingleCases: report.fuelSingleCases.length,
      hoursCases: report.hoursCases.length,
      constrainedCases: report.constrainedCases.length,
      failures: report.failures,
      browserErrors: report.browserErrors,
      blockedRequests: report.blockedRequests,
    },
    null,
    2,
  ),
);
assert.equal(report.cases.length, 4);
assert.equal(report.popupCases.length, 8);
assert.equal(report.fuelMobileCases.length, 2);
assert.equal(report.fuelSingleCases.length, 4);
assert.equal(report.hoursCases.length, 20);
assert.equal(report.constrainedCases.length, 2);
assert.equal(report.failures.length, 0);
assert.equal(report.browserErrors.length, 0);
assert.equal(report.blockedRequests.length, 0);
