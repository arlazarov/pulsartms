import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'MAP_TEST_ARTIFACT_DIR must identify the staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput(
  'station-popup',
  process.env.STATION_TEST_OUTPUT_DIR,
);
const client = resolve(import.meta.dirname, '../..');
const css = await readFile(resolve(artifact, 'css/main.css'), 'utf8');
const origin = 'http://station-popup.invalid';
const fixture = `import {createStationPopup} from './Scripts/fleetMap/stations/stationPopup.ts';
import {createDetailsCard} from './Scripts/fleetMap/ui/detailsCard.ts';
const host = document.getElementById('map');
window.google = {maps: {OverlayView: {preventMapHitsAndGesturesFrom() {}}}};
const card = createDetailsCard({getDiv: () => host});
const popup = createStationPopup(value => {window.editSelection = value;});
const data = {station: {id: 'station', name: 'LOVES #706', country: 'US', address: '3499 Lee Jackson Hwy, Staunton, VA 24401, USA'},
  discount: {currency: 'USD', unit: 'US gal', retailPrice: 6.289, discountPrice: 5.613, priceAfterIfta: 5.286, savings: .676},
  canEdit: true, fuel: {visits: [{number: 1, beforeStopId: 'delivery', gallons: 177.3, full: true, miles: 465,
    arrivalGallons: 34, departureGallons: 211.3, tankGallons: 211.3, purchaseCostUsd: 995.18}]}};
window.stationFixture = {update(changes = {}) {popup.update({...data, ...changes});},
  multiple() {this.update({fuel: {visits: [data.fuel.visits[0], {...data.fuel.visits[0], number: 2, gallons: 20,
    beforeStopId: 'next-delivery', full: false, miles: 950, departureGallons: 54, purchaseCostUsd: 112.26}]}});}};
window.stationFixture.update();
card.show(popup.element);
`;
const bundled = await build({
  stdin: { contents: fixture, resolveDir: client },
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
const html = `<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="/main.css">
<style>body{margin:0}#map{position:relative;height:600px;margin:24px;background:var(--ui-surface-muted);border:1px solid var(--ui-border-subtle)}
@media(max-width:768px){#map{margin:12px;height:640px}}</style></head><body><main id="map"></main><script type="module" src="/fixture.js"></script></body></html>`;
const report = {
  artifact,
  scope:
    'Production station popup and details-card modules, staged CSS, offline map host. No providers, API calls, database or writes.',
  cases: [],
  failures: [],
  browserErrors: [],
  requests: [],
};
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
try {
  for (const width of [1440, 390])
    for (const theme of ['light', 'dark']) {
      const name = `${width}-${theme}`;
      const context = await browser.newContext({
        viewport: { width, height: 800 },
        colorScheme: theme,
        reducedMotion: 'reduce',
      });
      await context.addInitScript(
        theme =>
          document.addEventListener('DOMContentLoaded', () => {
            document.documentElement.dataset.theme = theme;
          }),
        theme,
      );
      await context.route('**/*', async route => {
        const url = new URL(route.request().url());
        if (url.origin === origin && route.request().method() === 'GET') {
          if (url.pathname === '/')
            return route.fulfill({ contentType: 'text/html', body: html });
          if (url.pathname === '/main.css')
            return route.fulfill({ contentType: 'text/css', body: css });
          if (url.pathname === '/fixture.js')
            return route.fulfill({
              contentType: 'text/javascript',
              body: bundled.outputFiles[0].text,
            });
        }
        report.requests.push(`${route.request().method()} ${url}`);
        await route.abort();
      });
      const page = await context.newPage();
      page.on('pageerror', error =>
        report.browserErrors.push(`${name}: ${error.message}`),
      );
      await page.goto(origin);
      const popup = page.getByRole('dialog', {
        name: 'Map details',
        exact: true,
      });
      await popup.locator('.fleet-station-popup--single').waitFor();
      assert.equal(
        await popup.locator('.fleet-station-popup__plan-label').textContent(),
        'Fuel stop 1',
      );
      assert.deepEqual(
        await popup.locator('.fleet-fuel-visit__level').allTextContents(),
        ['16%', '100%'],
      );
      assert.equal(
        await popup.locator('.fleet-station-popup__cost-value').textContent(),
        '≈ $995.18 USD',
      );
      for (const field of ['retail', 'discount', 'ifta', 'savings'])
        assert.equal(
          await popup.locator(`.fleet-station-popup__${field}`).isVisible(),
          true,
        );
      const bounds = await popup.evaluate(element => {
        const box = node => {
          const r = node.getBoundingClientRect();
          return {
            left: r.left,
            right: r.right,
            top: r.top,
            bottom: r.bottom,
            width: r.width,
            height: r.height,
          };
        };
        return {
          popup: box(element),
          map: box(document.getElementById('map')),
          title: box(element.querySelector('.fleet-station-popup__title')),
          badge: box(element.querySelector('.fleet-station-popup__plan-label')),
          titleSize: parseFloat(
            getComputedStyle(
              element.querySelector('.fleet-station-popup__title'),
            ).fontSize,
          ),
          dials: [...element.querySelectorAll('.fleet-fuel-visit__dial')].map(
            box,
          ),
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
          clientHeight: element.clientHeight,
          scrollHeight: element.scrollHeight,
          fields: [
            ...element.querySelectorAll(
              '.fleet-station-popup__prices > *, .fleet-station-popup__actions > :not([hidden]), .fleet-fuel-visit__gauge',
            ),
          ].map(box),
        };
      });
      assert.ok(
        bounds.scrollWidth <= bounds.clientWidth + 1 &&
          bounds.scrollHeight <= bounds.clientHeight + 1,
        `${name}: ordinary planned popup fits without internal scrolling`,
      );
      assert.ok(
        bounds.title.width > 0 && bounds.title.right <= bounds.badge.left,
        `${name}: planned fuel badge must never cover the station name`,
      );
      assert.equal(
        bounds.titleSize,
        20,
        `${name}: planned station title keeps its approved hierarchy`,
      );
      assert.ok(
        bounds.dials.length === 2 &&
          bounds.dials.every(
            dial =>
              Math.abs(dial.width - 64) <= 1 && Math.abs(dial.height - 64) <= 1,
          ),
        `${name}: planned popup has two prominent round 64px gauges`,
      );
      assert.ok(
        bounds.fields.every(
          field =>
            field.left >= bounds.popup.left &&
            field.right <= bounds.popup.right &&
            field.top >= bounds.popup.top &&
            field.bottom <= bounds.popup.bottom,
        ),
        `${name}: price, gauge and cost controls fit the popup`,
      );
      if (width === 1440)
        assert.ok(
          Math.abs(bounds.popup.width - 512) <= 1 &&
            bounds.popup.left > bounds.map.left + bounds.map.width / 2 &&
            Math.abs(bounds.map.right - bounds.popup.right - 17) <= 1,
          `${name}: planned popup uses the 512px reference column at the map's right edge`,
        );
      await popup.screenshot({ path: resolve(output, `${name}-planned.png`) });
      await popup
        .getByRole('button', { name: 'Edit fuel plan', exact: true })
        .click();
      assert.deepEqual(await page.evaluate(() => window.editSelection), {
        stationId: 'station',
        name: 'LOVES #706',
        beforeStopId: 'delivery',
        addNew: false,
      });
      await page.evaluate(() => window.stationFixture.multiple());
      assert.equal(
        await popup.locator('.fleet-station-popup__plan-label').textContent(),
        'Fuel stops 1, 2',
      );
      assert.deepEqual(
        await popup
          .locator(
            '.fleet-station-popup__visit-cost .fleet-station-popup__cost-value',
          )
          .allTextContents(),
        ['≈ $995.18 USD', '≈ $112.26 USD'],
      );
      await popup.screenshot({
        path: resolve(output, `${name}-return-visits.png`),
      });
      await page.evaluate(() =>
        window.stationFixture.update({ canEdit: false }),
      );
      assert.equal(
        await popup
          .getByRole('button', { name: 'Edit fuel plan', exact: true })
          .count(),
        0,
      );
      assert.equal(
        await popup.locator('.fleet-station-popup__cost-value').textContent(),
        '≈ $995.18 USD',
      );
      await page.evaluate(() => window.stationFixture.update({ fuel: null }));
      assert.equal(
        await popup.locator('.fleet-station-popup--planned').count(),
        0,
      );
      assert.equal(
        await popup
          .getByRole('button', { name: 'Add to fuel plan', exact: true })
          .isVisible(),
        true,
      );
      report.cases.push({ name, bounds });
      await context.close();
    }
} catch (error) {
  report.failures.push(error.stack ?? String(error));
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
      cases: report.cases.length,
      failures: report.failures,
      browserErrors: report.browserErrors,
      requests: report.requests,
      output,
    },
    null,
    2,
  ),
);
assert.equal(
  report.failures.length + report.browserErrors.length + report.requests.length,
  0,
);
