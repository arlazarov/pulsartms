import assert from 'node:assert/strict';
import {browserOutput} from '../../../scripts/artifacts.mjs';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';

const output = browserOutput('map-markers', process.env.MARKER_TEST_OUTPUT_DIR);
const origin = 'http://map-markers.invalid';
const bundle = await build({ entryPoints: ['tests/browser/mapMarkersFixture.js'], bundle: true, write: false, format: 'esm', platform: 'browser' });
const html = `<!doctype html><html><head><meta charset="utf-8"><style>
body{margin:0;background:#f4f7fb;color:#172438;font:14px Arial,sans-serif}header{padding:24px 32px}h1{font-size:20px;margin:0 0 8px}p{margin:0}
#map{position:relative;width:100%;height:650px;background:#e4eee9;background-image:linear-gradient(135deg,transparent 70%,#c5e4ee 70%)}
</style></head><body><header><h1>Fleet map marker comparison</h1><p>Production GPU layers · synthetic route and telemetry · semantic fuel colors · no map providers</p></header>
<main id="map"></main><script type="module" src="/fixture.js"></script></body></html>`;
const report = { scope: 'Actual production WebGL truck/stop/station layers on an offline geographic viewport, DPR1/2. Provider map, API and production data are not exercised.', cases: [], errors: [], failures: [] };
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome', args: ['--enable-unsafe-swiftshader'] });
try {
  for (const density of [1, 2]) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 800 }, deviceScaleFactor: density, hasTouch: density === 2 });
    await context.route('**/*', route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/') return route.fulfill({ contentType: 'text/html', body: html });
      if (path === '/fixture.js') return route.fulfill({ contentType: 'text/javascript', body: bundle.outputFiles[0].text });
      report.errors.push(`Unexpected request: ${route.request().url()}`); return route.abort();
    });
    const page = await context.newPage();
    page.on('pageerror', error => report.errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') report.errors.push(message.text()); });
    await page.goto(origin);
    await page.waitForFunction(() => window.markerFrames > 0 && window.markerReport?.().loaded);
    const markers = await page.evaluate(() => window.markerReport());
    assert.equal(markers.trucks.length, 4);
    assert.deepEqual(markers.trucks.map(truck => truck.angle || 0), [-35, 0, -90, -180]);
    assert.ok(markers.trucks.every(truck => truck.size === 28 && !truck.svg.includes('linearGradient') && !truck.svg.includes('r="2.1"')));
    assert.ok(markers.trucks[0].svg.includes('fill="#16a34a"') && markers.trucks[0].svg.includes('M13 1 L24 23') && !markers.trucks[0].svg.includes('<circle'));
    assert.ok(markers.trucks[1].svg.includes('<circle') && markers.trucks[1].svg.includes('fill="#64748b"') && !markers.trucks[1].svg.includes('<path'));
    assert.ok(markers.trucks[2].svg.includes('<circle') && markers.trucks[2].svg.includes('fill="#16a34a"') && !markers.trucks[2].svg.includes('<path'));
    assert.ok(markers.trucks[3].svg.includes('<circle') && markers.trucks[3].svg.includes('fill="#16a34a"') && !markers.trucks[3].svg.includes('<path'),
      'engine On at zero speed is stationary, not a moving arrow');
    assert.deepEqual(markers.stops.map(stop => stop.number), ['1', '2', '12']);
    assert.ok(markers.stops.every(stop => stop.size === 34));
    assert.deepEqual(markers.stops[1].color, [124, 58, 237, 255]);
    assert.equal(markers.stations.length, 6, 'spacing never removes an original station');
    assert.ok(markers.stations.every(station => station.radius === 8), 'prices, clutter and plan selection never change the 16px station circle size');
    assert.deepEqual(markers.prices, [], 'prices belong in the popup, not inside station circles');
    assert.equal(markers.layerIds.includes('fuel-price-labels'), false);
    assert.deepEqual(markers.stations.map(station => [station.id, station.color]), [
      ['green', [21,128,61]], ['amber', [245,158,11]], ['red', [185,28,28]],
      ['crowded', [245,158,11]], ['unavailable', [128,144,165]], ['planned', [21,128,61]]
    ], 'semantic price colors remain unchanged for all original stations');
    const screenshot = await page.locator('#map').screenshot({ path: resolve(output, `markers-${density}x.png`) });
    const pixels = await page.evaluate(async ({ png, trucks }) => {
      const bitmap = await createImageBitmap(new Blob([Uint8Array.from(atob(png), byte => byte.charCodeAt(0))], { type: 'image/png' }));
      const canvas = document.createElement('canvas'); canvas.width = bitmap.width; canvas.height = bitmap.height;
      const ctx = canvas.getContext('2d'); ctx.drawImage(bitmap, 0, 0); bitmap.close();
      const rgba = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
      const dpr = canvas.width / document.getElementById('map').clientWidth;
      return trucks.map(truck => {
        let ink = 0;
        const target = truck.unit === '11005' ? [100, 116, 139] : [22, 163, 74];
        for (let y = Math.floor((truck.mapY - 26) * dpr); y <= (truck.mapY + 26) * dpr; y++)
          for (let x = Math.floor((truck.mapX - 26) * dpr); x <= (truck.mapX + 26) * dpr; x++) {
            const offset = (y * canvas.width + x) * 4;
            const pixel = rgba.slice(offset, offset + 3);
            if (target.every((value, index) => Math.abs(value - pixel[index]) < 25)) ink++;
          }
        return { unit: truck.unit, statePixels: ink, density: dpr };
      });
    }, { png: screenshot.toString('base64'), trucks: markers.trucks });
    assert.ok(pixels.every(truck => truck.statePixels > 100 * density * density), 'actual GPU pixels retain green moving/idle markers and gray engine-off markers');
    const ordinary = markers.stations.find(station => station.id === 'green');
    const fillWidth = await page.evaluate(async ({png, station}) => {
      const bitmap = await createImageBitmap(new Blob([Uint8Array.from(atob(png), byte => byte.charCodeAt(0))], {type: 'image/png'}));
      const canvas = document.createElement('canvas'); canvas.width = bitmap.width; canvas.height = bitmap.height;
      const ctx = canvas.getContext('2d'); ctx.drawImage(bitmap, 0, 0); bitmap.close();
      const dpr = canvas.width / document.getElementById('map').clientWidth;
      const y = Math.floor(station.mapY * dpr), xs = [];
      for (let x = Math.floor((station.mapX - 14) * dpr); x <= (station.mapX + 14) * dpr; x++) {
        const rgba = ctx.getImageData(x, y, 1, 1).data;
        if (station.color.every((channel, index) => Math.abs(channel - rgba[index]) < 10)) xs.push(x);
      }
      return xs.length ? (Math.max(...xs) - Math.min(...xs) + 1) / dpr : 0;
    }, {png: screenshot.toString('base64'), station: ordinary});
    assert.ok(fillWidth >= 12 && fillWidth <= 16, `actual station fill must be compact: ${fillWidth}px`);
    if (density === 2) await page.touchscreen.tap(ordinary.x + 10, ordinary.y);
    else await page.mouse.click(ordinary.x + 10, ordinary.y);
    await page.waitForFunction(() => window.markerClicks.includes('green'));
    const planned = markers.stations.find(station => station.id === 'planned');
    if (density === 2) await page.touchscreen.tap(planned.x + 9, planned.y);
    else await page.mouse.click(planned.x + 9, planned.y);
    await page.waitForFunction(() => window.markerClicks.includes('planned'));
    const stop = markers.stops.find(stop => stop.number === '12');
    await page.mouse.click(stop.x, stop.y);
    await page.waitForFunction(() => window.markerClicks.includes('stop-12'));
    const selectedFrame = await page.evaluate(() => {window.selectNextRoute(true); return window.markerFrames;});
    await page.waitForFunction(frame => window.markerFrames > frame && window.markerReport().loaded, selectedFrame);
    const selectedRoads = await page.evaluate(() => window.markerReport().roads);
    assert.deepEqual(selectedRoads.map(road => road.opacity), [.4, 1], 'selecting a next load also subdues the current route');
    assert.deepEqual(selectedRoads.map(road => road.width), markers.roads.map(road => road.width), 'selection never widens any route');
    assert.ok(selectedRoads.every(road => road.originalGeometry));
    await page.locator('#map').screenshot({path: resolve(output, `selected-next-${density}x.png`)});
    const restoredFrame = await page.evaluate(() => {window.selectNextRoute(false); return window.markerFrames;});
    await page.waitForFunction(frame => window.markerFrames > frame && window.markerReport().loaded, restoredFrame);
    assert.deepEqual(await page.evaluate(() => window.markerReport().roads), markers.roads, 'clearing selection restores all road appearances');
    const routes = [];
    for (const role of ['future', 'future-muted', 'current', 'current-muted']) {
      const frame = await page.evaluate(role => { window.showRouteProbe(role); return window.markerFrames; }, role);
      await page.waitForFunction(({role, frame}) => window.markerFrames > frame && window.routeProbeReport()?.loaded
        && window.routeProbeReport().role === role, {role, frame});
      const route = await page.evaluate(() => window.routeProbeReport());
      assert.equal(route.pointCount, 161, 'dense input must not create extra application route points');
      assert.equal(route.originalGeometry, true, 'outline and fill retain the original route array');
      assert.equal(route.width, 5);
      assert.equal(route.opacity, role.endsWith('-muted') ? .4 : 1);
      if (!role.startsWith('current')) assert.deepEqual(route.extensions, [{dash: true, offset: false, highPrecisionDash: true}]);
      else assert.deepEqual(route.extensions ?? [], []);
      const capture = await page.locator('#map').screenshot({path: resolve(output, `route-${role}-${density}x.png`)});
      const samples = await page.evaluate(async ({png, route}) => {
        const bitmap = await createImageBitmap(new Blob([Uint8Array.from(atob(png), byte => byte.charCodeAt(0))], {type: 'image/png'}));
        const canvas = document.createElement('canvas'); canvas.width = bitmap.width; canvas.height = bitmap.height;
        const ctx = canvas.getContext('2d'); ctx.drawImage(bitmap, 0, 0); bitmap.close();
        const rgba = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
        const dpr = canvas.width / document.getElementById('map').clientWidth;
        const color = [], gaps = [], whiteEdges = [];
        const pixel = (x, y) => [...rgba.slice((Math.floor(y * dpr) * canvas.width + Math.floor(x * dpr)) * 4,
          (Math.floor(y * dpr) * canvas.width + Math.floor(x * dpr)) * 4 + 3)];
        for (let x = Math.ceil(route.start.mapX + 20); x < route.end.mapX - 20; x++) {
          const center = pixel(x, route.start.mapY);
          const background = pixel(x, route.start.mapY - 12);
          // Deck gamma-corrects layer opacity; the fill composites over its white keyline.
          const opacity = Math.pow(route.opacity, 1 / 2.2);
          const outlineAlpha = route.outlineColor[3] / 255 * opacity;
          const expected = route.color.map((value, index) => value * opacity
            + (route.outlineColor[index] * outlineAlpha + background[index] * (1 - outlineAlpha)) * (1 - opacity));
          color.push(center.every((value, index) => Math.abs(value - expected[index]) < 20));
          gaps.push(center.every((value, index) => Math.abs(value - background[index]) < 8));
          const edge = pixel(x, route.start.mapY + route.width / 2 + .5);
          whiteEdges.push(edge.every((value, index) => value > background[index] + 3 * route.opacity));
        }
        const runs = values => values.reduce((count, value, index) => count + (value && !values[index - 1] ? 1 : 0), 0);
        return {total: color.length, colored: color.filter(Boolean).length, background: gaps.filter(Boolean).length,
          dashRuns: runs(color), gapRuns: runs(gaps), whiteEdges: whiteEdges.filter(Boolean).length};
      }, {png: capture.toString('base64'), route});
      (report.routeProbes ??= []).push({density, role, ...route, samples});
      if (!role.startsWith('current')) {
        assert.ok(samples.dashRuns >= 12 && samples.gapRuns >= 12, 'short segments must render continuous repeated dashes, not a solid line');
        assert.ok(samples.background / samples.total > .08, 'rounded outline caps still leave background gaps in each dash period');
        assert.ok(samples.colored / samples.total > .45 && samples.colored / samples.total < .85);
      } else assert.ok(samples.colored / samples.total > .98 && samples.gapRuns === 0, 'current route stays continuous in both normal and subdued selection states');
      assert.ok(samples.whiteEdges > samples.total * .25, 'gentle white keyline remains visible beside the route');
      routes.push({role, ...route, samples});
    }
    assert.equal(await page.evaluate(() => window.disposeMarkerFixture()), 0);
    report.cases.push({ density, markers, pixels, stationFillWidth: fillWidth, selectedRoads, routes, circleEdgeInput: density === 2 ? 'touch' : 'mouse', screenshot: `markers-${density}x.png` });
    await context.close();
  }
} catch (error) { report.failures.push(error.stack ?? String(error)); }
finally { await browser.close(); await writeFile(resolve(output, 'report.json'), JSON.stringify(report, null, 2)); }
console.log(JSON.stringify({ cases: report.cases.length, errors: report.errors, failures: report.failures, output }, null, 2));
assert.equal(report.errors.length + report.failures.length, 0);
