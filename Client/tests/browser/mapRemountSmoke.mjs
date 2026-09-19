import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';

const output = browserOutput('map-markers');
const bundle = await build({
  entryPoints: ['tests/browser/mapRemountFixture.js'],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
const html = `<!doctype html><html><head><style>
body{margin:0}#map{height:700px;position:relative;background:#e4eee9}
.fleet-map-host{width:100%;height:100%}
.fleet-map-host--initializing{visibility:hidden}
</style></head><body><main id="map"></main>
<script type="module" src="/fixture.js"></script></body></html>`;
const origin = 'http://map-remount.invalid';
const report = {
  scope:
    'Production scene and retained host with GoogleMapsOverlay/Deck.' +
    ' A synthetic stationary provider only paints on requestRedraw.' +
    ' No live Google Maps, APIs or business data.',
  cases: [],
  errors: [],
  requests: [],
};
const browser = await chromium.launch({
  headless: true,
  channel: 'chrome',
  args: ['--enable-unsafe-swiftshader'],
});
try {
  for (const density of [1, 2]) {
    const context = await browser.newContext({
      viewport: { width: 1440, height: 800 },
      deviceScaleFactor: density,
    });
    await context.route('**/*', route => {
      const url = new URL(route.request().url());
      const file =
        url.origin === origin &&
        {
          '/': ['text/html', html],
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
    await page.waitForFunction(() => window.remountFixture);
    for (let cycle = 0; cycle < 4; cycle++) {
      await page.evaluate(() => {
        window.remountFixture.holdCamera(true);
        window.remountFixture.open();
      });
      await page.waitForFunction(() => window.remountFixture.report().loaded);
      const pending = await page.evaluate(() => window.remountFixture.report());
      assert.equal(pending.camera.zoom, 1);
      assert.equal(pending.hidden, true);
      assert.equal(pending.ready, false);
      const compressed = pending.trucks[1].x - pending.trucks[0].x;
      assert.ok(
        compressed < 100,
        'The unsynchronized startup viewport compresses distant trucks',
      );
      await page.evaluate(() => window.remountFixture.holdCamera(false));
      await page.waitForFunction(() => window.remountFixture.report().ready);
      const result = await page.evaluate(() => window.remountFixture.report());
      assert.deepEqual(result.camera, {
        longitude: -96,
        latitude: 38,
        zoom: 4,
      });
      assert.equal(result.frameRequests, cycle + 1);
      assert.equal(result.cameraWrites, 0);
      assert.equal(result.mapCreations, 1);
      assert.equal(result.overlays, 1);
      assert.equal(result.canvases, 1);
      const west = result.trucks.find(truck => truck.unit === '11006');
      const east = result.trucks.find(truck => truck.unit === '54777');
      assert.ok(
        east.x - west.x > 600,
        'Distant trucks must not compress into the default startup viewport',
      );
      await page.locator('#map').screenshot({
        path: resolve(output, `remount-${density}x-${cycle}.png`),
      });
      await page.evaluate(() => window.remountFixture.close());
      const closed = await page.evaluate(() => window.remountFixture.report());
      assert.equal(closed.overlays, 0);
      assert.equal(closed.listeners, 0);
      assert.equal(closed.canvases, 0);
      report.cases.push({ density, cycle, pending, result, closed });
    }
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
assert.equal(report.cases.length, 8);
