// The owner's September 26 map drawn by the production scene on the
// provider's real basemap (mapProposalFixture.js), at the overview the
// owner saw, closer over New York, and at Albany where two trucks stand
// with three stops; each with and without a next load picked. The browser
// key in wwwroot/appsettings.json loads the map exactly as the Client does;
// it is never written to the report.
//
// node tests/browser/mapProposalSmoke.mjs [label]
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';

const label = process.argv[2] ?? 'now';
const output = browserOutput('map-markers', process.env.ROAD_TEST_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const key = JSON.parse(await readFile('wwwroot/appsettings.json', 'utf8'))
  .GoogleMaps?.ApiKey;
if (!key) throw new Error('wwwroot/appsettings.json has no browser key');
const redact = text => String(text).split(key).join('[key]');
const bundle = await build({
  entryPoints: ['tests/browser/mapProposalFixture.js'],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
// The compiled stylesheet: the price colours are its custom properties.
const css = await readFile('wwwroot/css/main.css', 'utf8');
const html = `<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="/styles.css"><style>
body{margin:0}#map{position:absolute;inset:0}
</style></head><body><main id="map"></main><script type="module" src="/fixture.js"></script></body></html>`;
const provider = /(^|\.)(googleapis|gstatic|google)\.com$/;
const report = { label, cases: [], errors: [], renderer: null };
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
  args: [
    '--ignore-gpu-blocklist',
    '--use-angle=metal',
    '--enable-gpu-rasterization',
  ],
});
try {
  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: 1,
  });
  await context.addInitScript(key => {
    window.mapsKey = key;
  }, key);
  await context.route('**/*', route => {
    const url = new URL(route.request().url());
    if (provider.test(url.hostname)) return route.continue();
    if (url.origin === origin && url.pathname === '/')
      return route.fulfill({ contentType: 'text/html', body: html });
    if (url.origin === origin && url.pathname === '/styles.css')
      return route.fulfill({ contentType: 'text/css', body: css });
    if (url.origin === origin && url.pathname === '/fixture.js')
      return route.fulfill({
        contentType: 'text/javascript',
        body: bundle.outputFiles[0].text,
      });
    report.errors.push(`Unexpected request: ${url.origin}${url.pathname}`);
    return route.abort();
  });
  const page = await context.newPage();
  page.on('pageerror', error => report.errors.push(redact(error.message)));
  page.on('crash', () => report.errors.push('The page crashed.'));
  page.on('console', message => {
    if (message.type() === 'error')
      report.errors.push(`console: ${redact(message.text())}`);
  });
  await page.goto(`${origin}/`);
  await page.waitForFunction(() => window.proposal, null, { timeout: 60000 });
  await page.evaluate(() => window.proposal.setStations(true));
  const views = {
    continent: [-84.0, 36.8, 5.4],
    newyork: [-74.9, 42.45, 8.3],
    albany: [-73.9, 42.75, 10],
  };
  for (const [view, [lng, lat, zoom]] of Object.entries(views))
    for (const selected of [null, 'load-1413']) {
      await page.evaluate(
        async ([lng, lat, zoom, selected]) => {
          if (selected) window.proposal.select(selected, 0);
          else window.proposal.clear();
          await window.proposal.view(lng, lat, zoom);
        },
        [lng, lat, zoom, selected],
      );
      // Tiles and labels settle after idle.
      await page.waitForTimeout(3000);
      const name = `${label}-${view}-${selected ? 'picked' : 'none'}`;
      await page.screenshot({ path: resolve(output, `${name}.png`) });
      report.cases.push(name);
    }
  report.renderer = await page.evaluate(() => window.proposal.renderer());
} finally {
  await browser.close();
  await writeFile(
    resolve(output, `${label}-report.json`),
    JSON.stringify(report, null, 2),
  );
}
console.log(JSON.stringify({ output, ...report }, null, 2));
