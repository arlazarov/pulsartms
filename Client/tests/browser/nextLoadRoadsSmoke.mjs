// The roads of a truck's next loads and their empty miles as the map draws
// them, before and after a stop is picked, at an overview and a closer
// zoom, with the station layer off and on. Production scene, next-loads
// layer and station layer (nextLoadRoadsFixture.js) on an offline view; no
// provider basemap, so this is the roads against a plain ground only.
//
// node tests/browser/nextLoadRoadsSmoke.mjs [label]
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';

const label = process.argv[2] ?? 'now';
const output = browserOutput('map-markers', process.env.ROAD_TEST_OUTPUT_DIR);
const origin = 'http://next-load-roads.invalid';
const bundle = await build({
  entryPoints: ['tests/browser/nextLoadRoadsFixture.js'],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
// The compiled stylesheet: the price colours are its custom properties.
const css = await readFile('wwwroot/css/main.css', 'utf8');
const html = `<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="/styles.css"><style>
body{margin:0;background:#eceff1}#map{position:relative;width:100%;height:100vh;background:#eceff1}
</style></head><body><main id="map"></main><script type="module" src="/fixture.js"></script></body></html>`;
const report = { label, cases: [], errors: [] };
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
  args: ['--enable-unsafe-swiftshader'],
});
try {
  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: 1,
  });
  await context.route('**/*', route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/')
      return route.fulfill({ contentType: 'text/html', body: html });
    if (path === '/styles.css')
      return route.fulfill({ contentType: 'text/css', body: css });
    if (path === '/fixture.js')
      return route.fulfill({
        contentType: 'text/javascript',
        body: bundle.outputFiles[0].text,
      });
    report.errors.push(`Unexpected request: ${route.request().url()}`);
    return route.abort();
  });
  const page = await context.newPage();
  page.on('pageerror', error => report.errors.push(error.message));
  await page.goto(origin);
  await page.waitForFunction(() => window.roadFixture && window.roadFrames > 0);
  const frame = async () => {
    const start = await page.evaluate(() => window.roadFrames);
    await page.waitForFunction(n => window.roadFrames > n, start);
    await page.waitForTimeout(250);
  };
  const views = {
    overview: [-75.0, 42.75, 7.2],
    closer: [-74.6, 42.95, 9],
  };
  for (const stations of [false, true]) {
    await page.evaluate(
      visible => window.roadFixture.setStations(visible),
      stations,
    );
    for (const [view, [lon, lat, zoom]] of Object.entries(views))
      for (const selected of [null, 'load-1413']) {
        await page.evaluate(
          ([lon, lat, zoom, selected]) => {
            window.roadFixture.view(lon, lat, zoom);
            if (selected) window.roadFixture.select(selected, 0);
            else window.roadFixture.clear();
          },
          [lon, lat, zoom, selected],
        );
        await frame();
        const name = `${label}-${view}-${selected ? 'picked' : 'none'}${stations ? '-stations' : ''}`;
        await page.screenshot({ path: resolve(output, `${name}.png`) });
        report.cases.push(name);
      }
  }
} finally {
  await browser.close();
  await writeFile(
    resolve(output, `${label}-report.json`),
    JSON.stringify(report, null, 2),
  );
}
console.log(JSON.stringify({ output, ...report }, null, 2));
