import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('station-popup');
const css = await readFile(resolve(artifact, 'css/main.css'), 'utf8');
const origin = 'http://station-quote.invalid';
const fixture = `
import {createStationPopup}
  from './Scripts/fleetMap/stations/stationPopup.js';
const popup = createStationPopup();
document.querySelector('.fleet-map-inspector__native').append(popup.element);
const discount = {currency: 'USD', unit: 'US gal', retailPrice: 6.089,
  discountPrice: 5.893, priceAfterIfta: 5.423, savings: .196};
const station = {id: 'fixture', name: 'LOVES #332', country: 'US',
  address: '10145 Avon Lake Rd, Burbank, OH 44214, USA'};
window.quoteFixture = showComparison => popup.update({station, canEdit: true,
  discount: {...discount, comparison: showComparison ? {
    date: '2026-09-13', nextDate: '2026-09-14',
    next: {...discount, discountPrice: 5.793},
    retailChange: 0, discountChange: -.1, iftaChange: 0, savingsChange: 0,
    retailChangePercent: 0, discountChangePercent: -1.7,
    iftaChangePercent: 0, savingsChangePercent: 0,
  } : null}});
window.quoteFixture(false);
window.quoteTruck = selected => {
  document.querySelector('.fleet-map-inspector__back').hidden = !selected;
  document.querySelector('.fleet-map-inspector__controls').hidden = selected;
};
window.quoteTruck(true);
`;
const bundle = await build({
  stdin: {
    contents: fixture,
    resolveDir: resolve(import.meta.dirname, '../..'),
  },
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
const html = `<!doctype html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="/styles.css">
<style>body{margin:0}.fleet-map-stage{height:900px;margin:12px}
#fleet-map{width:100%;background:var(--ui-canvas)}</style></head><body>
<main class="fleet-map-stage"><div id="fleet-map"></div>
<section class="fleet-map-info-reserved fleet-map-inspector has-selection"
 data-inspector-mode="fuel">
<header class="fleet-map-inspector__header">
<strong class="fleet-map-inspector__title">Fuel station</strong>
<button class="btn btn--text btn--small fleet-map-inspector__back">
Back to truck</button>
<div class="fleet-map-inspector__controls">
<button class="fleet-map-inspector__close" aria-label="Close">×</button>
</div></header><div class="fleet-map-inspector__native"></div>
</section></main><script type="module" src="/fixture.js"></script>
</body></html>`;
const report = {
  artifact,
  cases: [],
  errors: [],
  requests: [],
  scope:
    'Production station popup and staged CSS in a representative shell. ' +
    'Synthetic prices; no APIs, providers, authentication or business writes.',
};
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
try {
  for (const width of [1440, 720, 390, 320]) {
    for (const theme of ['light', 'dark']) {
      for (const scale of [100, 200]) {
        const name = `${width}-${theme}-${scale}`;
        const context = await browser.newContext({
          viewport: { width, height: 1000 },
          reducedMotion: 'reduce',
        });
        const themedHtml = html.replace(
          '<html>',
          `<html data-theme="${theme}" style="font-size:${scale}%">`,
        );
        await context.route('**/*', route => {
          const url = new URL(route.request().url());
          const file =
            url.origin === origin &&
            {
              '/': ['text/html', themedHtml],
              '/styles.css': ['text/css', css],
              '/fixture.js': ['text/javascript', bundle.outputFiles[0].text],
            }[url.pathname];
          if (file) {
            return route.fulfill({ contentType: file[0], body: file[1] });
          }
          report.requests.push(url.toString());
          return route.abort();
        });
        const page = await context.newPage();
        page.on('pageerror', error => report.errors.push(error.message));
        await page.goto(origin);
        const panel = page.locator('.fleet-map-inspector');
        await panel.locator('.fleet-station-popup').waitFor();
        const geometry = () =>
          panel.evaluate(element => {
            const box = node => {
              const { x, y, width, height } = node.getBoundingClientRect();
              return { x, y, width, height };
            };
            return {
              panel: box(element),
              map: box(document.querySelector('#fleet-map')),
              overflow: element.scrollWidth > element.clientWidth + 1,
              title: box(element.querySelector('.fleet-map-inspector__title')),
              close: box(element.querySelector('.fleet-map-inspector__close')),
              back: box(element.querySelector('.fleet-map-inspector__back')),
            };
          });
        const single = await geometry();
        assert.equal(single.overflow, false, `${name}: single-day overflow`);
        const mapWidth = single.map.width;
        assert.ok(
          single.panel.width <= mapWidth,
          `${name}: station stays within the map`,
        );
        assert.ok(
          single.panel.width >= Math.min(mapWidth, (360 * scale) / 100),
          `${name}: station retains its original minimum width`,
        );
        if (width < 800) {
          assert.equal(
            single.panel.width,
            mapWidth,
            `${name}: phone station fills the map width`,
          );
        }
        const close = panel.getByRole('button', { name: 'Close', exact: true });
        const back = panel.getByRole('button', { name: 'Back to truck' });
        assert.equal(await close.isVisible(), false);
        assert.equal(await back.isVisible(), true);
        const separate = (a, b) =>
          a.x >= b.x + b.width - 1 ||
          a.y >= b.y + b.height - 1 ||
          b.x >= a.x + a.width - 1 ||
          b.y >= a.y + a.height - 1;
        assert.ok(
          separate(single.title, single.back),
          `${name}: title and Back do not overlap when wrapped`,
        );
        if (scale === 100) {
          const center = box => box.y + box.height / 2;
          assert.ok(
            Math.abs(center(single.title) - center(single.back)) <= 1,
            `${name}: title stays on the same header row`,
          );
          await panel.screenshot({
            path: resolve(output, `${name}-single.png`),
          });
        }
        await page.evaluate(() => window.quoteTruck(false));
        const standalone = await geometry();
        assert.equal(await close.isVisible(), true);
        assert.equal(await back.isVisible(), false);
        assert.equal(standalone.overflow, false);
        assert.ok(
          separate(standalone.title, standalone.close),
          `${name}: standalone Close does not overlap the title`,
        );
        await page.evaluate(() => window.quoteTruck(true));
        await page.evaluate(() => window.quoteFixture(true));
        const comparison = await geometry();
        assert.equal(
          comparison.overflow,
          false,
          `${name}: comparison overflow`,
        );
        assert.deepEqual(comparison.map, single.map);
        if (scale === 100 && width >= 800) {
          assert.ok(
            comparison.panel.width >= single.panel.width,
            `${name}: comparison retains room for both days`,
          );
          assert.ok(
            comparison.panel.width <= 512,
            `${name}: comparison respects the fuel-card width cap`,
          );
        }
        await panel
          .locator('.fleet-station-popup__comparison')
          .evaluate(node => {
            node.scrollLeft = node.scrollWidth;
          });
        const lastPrice = panel.locator('tbody tr:last-child td:last-child');
        assert.ok(await lastPrice.isVisible());
        await page.evaluate(() => window.quoteFixture(false));
        const restored = await geometry();
        assert.equal(
          restored.panel.width,
          single.panel.width,
          `${name}: removing next-day data restores the single-day width`,
        );
        assert.deepEqual(restored.map, single.map);
        report.cases.push({ name, single, standalone, comparison });
        await context.close();
      }
    }
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
console.log(
  JSON.stringify(
    {
      cases: report.cases.length,
      errors: report.errors,
      requests: report.requests,
      output,
    },
    null,
    2,
  ),
);
assert.equal(report.errors.length + report.requests.length, 0);
assert.equal(report.cases.length, 16);
