import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';

// A fuel station's popup on the map for stops already given to a driver:
// the production popup module and staged CSS in a representative
// inspector shell. It says "Sent", how far WhatsApp got, or "Changed since
// sent", and a later update that no longer carries a hand-over (another
// assignment, a new plan) leaves no label behind. Synthetic data; no API,
// provider or map drawing.

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput(
  'station-popup',
  process.env.FUEL_VISIT_OUTPUT_DIR,
);
const css = await readFile(resolve(artifact, 'css/main.css'), 'utf8');
const origin = 'http://fuel-visit.invalid';
const fixture = `
import {createStationPopup}
  from './Scripts/fleetMap/stations/stationPopup.ts';
const popup = createStationPopup();
document.querySelector('.fleet-map-inspector__native').append(popup.element);
const station = {id: 'fixture', name: 'LOVES #706', country: 'US',
  address: '3499 Lee Jackson Hwy, Staunton, VA 24401, USA'};
const discount = {currency: 'USD', unit: 'US gal', retailPrice: 4.3,
  discountPrice: 4.1, priceAfterIfta: 3.9, savings: .2};
const visit = (number, sent) => ({number, miles: number * 120, gallons: 100,
  arrivalGallons: 40, departureGallons: 140, tankGallons: 211.3,
  purchaseCostUsd: 410, yourPrice: 4.1, currency: 'USD', unit: 'US gal', sent});
window.visitFixture = visits => popup.update({station, canEdit: true,
  discount, fuel: {visits, miles: 120, unit: 'US gal'}});
window.visitFixture([
  visit(1, {changed: false, delivery: 'delivered'}),
  visit(2, {changed: true}),
]);
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
Back to truck</button></header>
<div class="fleet-map-inspector__native"></div>
</section></main><script type="module" src="/fixture.js"></script>
</body></html>`;
const report = {
  artifact,
  cases: [],
  errors: [],
  requests: [],
  scope:
    'Production station popup module and staged CSS in a representative ' +
    'shell; synthetic visits. No API, provider or map drawing.',
};

const labels = page =>
  page.$$eval('.fleet-fuel-visit', rows =>
    rows.map(row => {
      const label = row.querySelector('.fleet-fuel-visit__sent');
      if (!label) return null;
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
      let back = label;
      let background = getComputedStyle(back).backgroundColor;
      while (
        back.parentElement &&
        (background === 'transparent' || rgb(background)[3] === 0)
      ) {
        back = back.parentElement;
        background = getComputedStyle(back).backgroundColor;
      }
      const [a, b] = [
        lum(rgb(getComputedStyle(label).color)),
        lum(rgb(background)),
      ];
      const box = label.getBoundingClientRect();
      const card = row.getBoundingClientRect();
      return {
        text: label.textContent.trim(),
        warn: label.classList.contains('fleet-fuel-visit__sent--changed'),
        contrast: (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05),
        inside: box.left >= card.left - 0.5 && box.right <= card.right + 0.5,
        clipped: label.scrollWidth > label.clientWidth + 1,
      };
    }),
  );

const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
try {
  for (const width of [1440, 390])
    for (const theme of ['light', 'dark']) {
      const name = `${width}-${theme}`;
      const context = await browser.newContext({
        viewport: { width, height: 1000 },
        reducedMotion: 'reduce',
      });
      const themed = html.replace('<html>', `<html data-theme="${theme}">`);
      await context.route('**/*', route => {
        const url = new URL(route.request().url());
        const file =
          url.origin === origin &&
          {
            '/': ['text/html', themed],
            '/styles.css': ['text/css', css],
            '/fixture.js': ['text/javascript', bundle.outputFiles[0].text],
          }[url.pathname];
        if (file) return route.fulfill({ contentType: file[0], body: file[1] });
        report.requests.push(url.toString());
        return route.abort();
      });
      const page = await context.newPage();
      page.on('pageerror', error =>
        report.errors.push(`${name}: ${error.message}`),
      );
      await page.goto(origin);
      await page.locator('.fleet-fuel-visit').nth(1).waitFor();

      const given = await labels(page);
      assert.equal(given.length, 2);
      assert.deepEqual(
        given.map(label => [label.text, label.warn]),
        [
          ['Delivered', false],
          ['Changed since sent', true],
        ],
        `${name}: labels`,
      );
      for (const label of given) {
        assert.ok(
          label.inside && !label.clipped,
          `${name}: ${label.text} cut off`,
        );
        assert.ok(
          label.contrast >= 4.5,
          `${name}: ${label.text} contrast ${label.contrast.toFixed(2)}`,
        );
      }
      assert.ok(
        (await page.evaluate(() => document.documentElement.scrollWidth)) <=
          width,
        `${name}: page scrolls sideways`,
      );
      await page.screenshot({ path: resolve(output, `${name}-given.png`) });

      // The next update carries no hand-over for the second stop (another
      // assignment, a new plan) and WhatsApp reported the first as read.
      await page.evaluate(() =>
        window.visitFixture([
          {
            number: 1,
            miles: 120,
            gallons: 100,
            arrivalGallons: 40,
            departureGallons: 140,
            tankGallons: 211.3,
            purchaseCostUsd: 410,
            yourPrice: 4.1,
            currency: 'USD',
            unit: 'US gal',
            sent: { changed: false, delivery: 'read' },
          },
          {
            number: 2,
            miles: 240,
            gallons: 100,
            arrivalGallons: 40,
            departureGallons: 140,
            tankGallons: 211.3,
            purchaseCostUsd: 410,
            yourPrice: 4.1,
            currency: 'USD',
            unit: 'US gal',
            sent: null,
          },
        ]),
      );
      const after = await labels(page);
      assert.deepEqual(
        after.map(label => label && label.text),
        ['Read', null],
        `${name}: a stale label remained`,
      );
      await page.screenshot({ path: resolve(output, `${name}-updated.png`) });
      report.cases.push({
        name,
        contrast: given.map(label => Number(label.contrast.toFixed(2))),
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
assert.deepEqual(report.requests, [], 'unexpected requests');
console.log(`Fuel visit sent smoke: ${report.cases.length} cases. ${output}`);
