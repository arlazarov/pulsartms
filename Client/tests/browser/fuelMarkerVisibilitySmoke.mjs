import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';

const output = browserOutput('stop-cards', process.env.STOP_CARD_OUTPUT_DIR);
const bundle = await build({
  entryPoints: ['tests/browser/stopCardsFixture.js'],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
const css = await readFile('wwwroot/css/main.css', 'utf8');
const html = `<!doctype html><html><head><link rel="stylesheet" href="/styles.css">
<style>body{margin:0;background:var(--ui-canvas)}#map{position:relative;height:580px;background:var(--ui-surface-muted)}</style>
</head><body><div id="map"></div><div id="fuel"><div class="fleet-map-details-card__body"></div></div>
<script type="module" src="/fixture.js"></script></body></html>`;
const report = {
  scope:
    'Offline production station controller and GPU layers; synthetic quotes, no providers or API.',
  cases: [],
  errors: [],
};
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
  args: ['--enable-unsafe-swiftshader'],
});
try {
  for (const theme of ['light', 'dark'])
    for (const density of [1, 2]) {
      const context = await browser.newContext({
        viewport: { width: 1440, height: 800 },
        deviceScaleFactor: density,
      });
      await context.addInitScript(
        theme =>
          document.addEventListener('DOMContentLoaded', () => {
            document.documentElement.dataset.theme = theme;
          }),
        theme,
      );
      await context.route('**/*', route => {
        const url = new URL(route.request().url());
        const resources = {
          '/': ['text/html', html],
          '/styles.css': ['text/css', css],
          '/fixture.js': ['text/javascript', bundle.outputFiles[0].text],
        };
        const resource =
          url.origin === 'http://fuel-markers.invalid' &&
          resources[url.pathname];
        if (resource)
          return route.fulfill({ contentType: resource[0], body: resource[1] });
        report.errors.push(`Unexpected request: ${url.origin}${url.pathname}`);
        return route.abort();
      });
      const page = await context.newPage();
      page.on('pageerror', error => report.errors.push(error.message));
      await page.goto('http://fuel-markers.invalid');
      await page.waitForFunction(
        () =>
          window.fixtureRenderedFuel?.loaded &&
          window.fixtureFuelReport?.().colors.length === 2,
      );
      const before = await page.evaluate(() => window.fixtureFuelReport());
      const expected = before.colors.filter(
        station => station.id === 'recommended-fuel',
      );
      assert.equal(expected.length, 1);
      const middle = await page.evaluate(() =>
        getComputedStyle(document.documentElement)
          .getPropertyValue('--clr-warning-500-rgb')
          .trim()
          .split(',')
          .map(Number),
      );
      assert.deepEqual(
        expected[0].color.slice(0, 3),
        middle,
        'a single price tier uses the warning middle color',
      );
      const frame = await page.evaluate(async () => {
        const frame = window.fixtureFrames;
        await window.fixtureFuelVisible(false);
        return frame;
      });
      await page.waitForFunction(
        frame =>
          window.fixtureFrames > frame &&
          window.fixtureFuelReport().colors.length === 1,
        frame,
      );
      assert.deepEqual(
        (await page.evaluate(() => window.fixtureFuelReport())).colors,
        expected,
      );
      await page.locator('#map').screenshot({
        path: resolve(output, `${theme}-${density}x-hidden-priced-fuel.png`),
      });
      const point = await page.evaluate(() => window.fixtureFuelPoint());
      await page.mouse.click(point.x, point.y + before.badge.offset[1]);
      await page
        .locator('.fleet-map-details-card .fleet-station-popup__title')
        .filter({ hasText: 'Recommended fuel stop' })
        .waitFor();
      assert.deepEqual(
        (await page.evaluate(() => window.fixtureFuelReport())).colors,
        expected,
        'selection preserves price color',
      );
      await page.evaluate(() => window.fixtureFuelVisible(true));
      await page.waitForFunction(
        () => window.fixtureFuelReport().colors.length === 2,
      );
      assert.deepEqual(
        (await page.evaluate(() => window.fixtureFuelReport())).colors,
        before.colors,
      );
      report.cases.push({ theme, density, color: expected[0].color });
      await context.close();
    }
  assert.deepEqual(report.errors, []);
} finally {
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  await browser.close();
}
console.log(
  JSON.stringify({ output, cases: report.cases.length, errors: report.errors }),
);
