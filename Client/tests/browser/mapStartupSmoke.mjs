import assert from 'node:assert/strict';
import {browserOutput} from '../../../scripts/artifacts.mjs';
import {readFile, mkdir, writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {build} from 'esbuild';
import {chromium} from 'playwright';

const output = browserOutput('map-startup', process.env.MAP_STARTUP_OUTPUT_DIR);
const origin = 'http://map-startup.invalid';
const bundle = await build({stdin: {resolveDir: process.cwd(), contents: `
import {createMapHost} from './Scripts/fleetMap/provider/mapHost.js';
import {createTruckLayer} from './Scripts/fleetMap/trucks/truckLayer.js';
const camera = [], events = new Map();
let mounted, layer, host, creations = 0;
window.google = {maps: {LatLngBounds: class {
  points = []; extend(point) {this.points.push(point);} isEmpty() {return !this.points.length;}
}}};
const mount = createMapHost(node => {
  host = node; creations++; host.textContent = 'Default camera';
  return {getDiv: () => host, setOptions: options => camera.push({options}),
    addListener(name, callback) {const callbacks = events.get(name) ?? new Set(); callbacks.add(callback); events.set(name, callbacks);
      return {remove() {callbacks.delete(callback);}};},
    fitBounds(bounds) {camera.push({fit: bounds.points}); host.textContent = 'Fleet fitted';},
    moveCamera(value) {camera.push(value); host.textContent = 'Selected truck';}, panTo(center) {camera.push({pan: center});}, setZoom() {}};
});
const data = [{truckId: 'a', truckExternalId: 'a', latitude: 40, longitude: -80},
  {truckId: 'b', truckExternalId: 'b', latitude: 45, longitude: -75}]
  .map(truck => ({...truck, updatedAt: new Date(Date.now() - 90000).toISOString(), speed: 0}));
window.start = id => {
  layer?.dispose(); mounted?.release(); camera.length = 0;
  mounted = mount(document.querySelector('#fleet-map'), {center: {lat: 41.5, lng: -87.5}, zoom: 5, mapTypeId: 'roadmap'});
  layer = createTruckLayer(mounted.map, undefined, undefined, undefined,
    () => ({render() {}, update() {}, setVisible() {}, setSelected() {}, dispose() {}}), undefined,
    (change, wait) => mounted.initialCamera(change, wait));
  layer.setInitialTruck(id);
};
window.publish = empty => layer.setTrucks(empty ? [] : data, []);
window.settle = () => [...(events.get('idle') ?? [])].forEach(callback => callback());
window.fail = () => mounted.show();
window.queryFocus = () => layer.focusTruck('b', undefined, true);
window.report = () => ({visibility: getComputedStyle(host).visibility, text: host.textContent, camera, creations,
  width: host.clientWidth, height: host.clientHeight});
`}, bundle: true, write: false, format: 'esm', platform: 'browser'});
const css = await readFile('wwwroot/css/main.css', 'utf8');
const html = `<!doctype html><html><head><link rel="stylesheet" href="/styles.css"><style>
body{margin:0;background:var(--ui-canvas);color:var(--ui-text);font-family:Arial,sans-serif}
.fleet-map-page{height:400px}.fleet-map-host{background:var(--ui-surface);padding:20px}
</style></head><body><main class="fleet-map-page"><div id="fleet-map"></div></main><script type="module" src="/fixture.js"></script></body></html>`;
await mkdir(output, {recursive: true});
const browser = await chromium.launch({headless: true, channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome'});
const report = {cases: [], failures: [], browserErrors: [], unexpectedRequests: [],
  scope: 'Real DOM and production host/truck lifecycle with a deterministic provider camera; no live provider or backend.'};
try {
  for (const width of [1440, 390]) {
    const page = await browser.newPage({viewport: {width, height: 600}});
    page.on('pageerror', error => report.browserErrors.push(error.message));
    await page.route('**/*', async route => {
      const url = new URL(route.request().url());
      const resource = url.origin === origin && route.request().method() === 'GET'
        ? {'/': ['text/html', html], '/styles.css': ['text/css', css], '/fixture.js': ['text/javascript', bundle.outputFiles[0].text]}[url.pathname] : null;
      if (resource) await route.fulfill({status: 200, contentType: resource[0], body: resource[1]});
      else {report.unexpectedRequests.push(route.request().url()); await route.abort();}
    });
    await page.goto(origin);
    await page.waitForFunction(() => window.start);
    await page.evaluate(() => window.start(null));
    let state = await page.evaluate(() => window.report());
    assert.equal(state.visibility, 'hidden');
    assert.ok(state.width > 0 && state.height > 0, 'hidden host remains measurable for provider fitBounds');
    await page.evaluate(() => window.publish(false));
    assert.equal((await page.evaluate(() => window.report())).visibility, 'hidden');
    await page.evaluate(() => window.settle());
    state = await page.evaluate(() => window.report());
    assert.equal(state.visibility, 'visible');
    assert.equal(state.text, 'Fleet fitted');
    assert.equal(state.camera.filter(x => x.fit).length, 1);
    await page.locator('#fleet-map').screenshot({path: resolve(output, `${width}-fleet.png`)});
    await page.evaluate(() => window.start('b'));
    assert.equal((await page.evaluate(() => window.report())).visibility, 'hidden');
    await page.evaluate(() => window.publish(false));
    state = await page.evaluate(() => window.report());
    assert.equal(state.visibility, 'visible');
    assert.equal(state.camera.filter(x => x.fit).length, 0);
    assert.deepEqual(state.camera.at(-1), {center: {lat: 45, lng: -75}});
    assert.equal(state.creations, 1, 'reopening reuses one provider without displaying its old camera');
    assert.ok(state.camera.filter(x => x.options).every(x => !('zoom' in x.options) && !('center' in x.options)));
    await page.locator('#fleet-map').screenshot({path: resolve(output, `${width}-truck.png`)});
    for (const first of ['empty', 'failed']) {
      await page.evaluate(() => window.start('b'));
      await page.evaluate(mode => mode === 'empty' ? window.publish(true) : window.fail(), first);
      assert.equal((await page.evaluate(() => window.report())).visibility, 'visible');
      await page.locator('.fleet-map-host').dispatchEvent('pointerdown');
      await page.evaluate(() => {window.publish(false); window.queryFocus();});
      state = await page.evaluate(() => window.report());
      assert.equal(state.camera.filter(x => x.fit || x.center || x.pan).length, 0,
        'recovery and query focus cannot undo a user camera action');
    }
    report.cases.push({width, fleet: true, directTruck: true, reusedProvider: true, emptyRecovery: true, failedRecovery: true});
    await page.close();
  }
} catch (error) {report.failures.push(error.stack ?? String(error));}
finally {await browser.close(); await writeFile(resolve(output, 'report.json'), JSON.stringify(report, null, 2));}
console.log(JSON.stringify(report, null, 2));
if (report.cases.length !== 2 || report.failures.length || report.browserErrors.length || report.unexpectedRequests.length) process.exitCode = 1;
