// The stop badges P, D, D1 and D2 in every state, drawn by the production
// scene on the real basemap (browser key from wwwroot/appsettings.json,
// never written out), for comparing their sizes. Screenshots and a report
// of each badge's measured glyph box go to managed browser output.
//
// node tests/browser/stopBadgeSmoke.mjs [label]
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';

const label = process.argv[2] ?? 'now';
const output = browserOutput('map-markers', process.env.BADGE_TEST_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const key = JSON.parse(await readFile('wwwroot/appsettings.json', 'utf8'))
  .GoogleMaps?.ApiKey;
if (!key) throw new Error('wwwroot/appsettings.json has no browser key');
const redact = text => String(text).split(key).join('[key]');
const bundle = await build({
  entryPoints: ['tests/browser/stopBadgeFixture.js'],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
const html = `<!doctype html><html><head><meta charset="utf-8"><style>
body{margin:0}#map{position:absolute;inset:0}
</style></head><body><main id="map"></main><script type="module" src="/fixture.js"></script></body></html>`;
const provider = /(^|\.)(googleapis|gstatic|google)\.com$/;
const report = { label, errors: [], warnings: [], shots: [] };
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
  for (const scheme of ['LIGHT', 'DARK']) {
    const context = await browser.newContext({
      viewport: { width: 900, height: 700 },
      deviceScaleFactor: 2,
    });
    await context.addInitScript(
      ({ key, scheme }) => {
        window.mapsKey = key;
        window.mapScheme = scheme;
      },
      { key, scheme },
    );
    await context.route('**/*', route => {
      const url = new URL(route.request().url());
      if (provider.test(url.hostname)) return route.continue();
      if (url.origin === origin && url.pathname === '/')
        return route.fulfill({ contentType: 'text/html', body: html });
      if (url.origin === origin && url.pathname === '/fixture.js')
        return route.fulfill({
          contentType: 'text/javascript',
          body: bundle.outputFiles[0].text,
        });
      return route.abort();
    });
    const page = await context.newPage();
    page.on('pageerror', e => report.errors.push(redact(e.message)));
    page.on('console', m => {
      if (['error', 'warning'].includes(m.type()))
        report.warnings.push(`${m.type()}: ${redact(m.text()).slice(0, 200)}`);
    });
    await page.goto(`${origin}/`);
    await page.waitForFunction(() => window.badges?.ready, null, {
      timeout: 60000,
    });
    await page.waitForTimeout(2500);
    const path = resolve(output, `${label}-badges-${scheme.toLowerCase()}.png`);
    await page.screenshot({ path });
    report.shots.push(path);
    await context.close();
  }
} finally {
  await browser.close();
  await writeFile(
    resolve(output, `${label}-report.json`),
    JSON.stringify(report, null, 2),
  );
}
console.log(JSON.stringify({ output, ...report }, null, 2));
