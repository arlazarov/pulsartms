import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';

const output = browserOutput(
  'map-startup',
  process.env.TRUCK_PLAYBACK_OUTPUT_DIR,
);
await mkdir(output, { recursive: true });
const bundle = await build({
  stdin: {
    resolveDir: process.cwd(),
    contents: `
import { createTruckLayer } from './Scripts/fleetMap/trucks/truckLayer.js';
import { truckPlaybackDelay } from './Scripts/fleetMap/trucks/truckPlayback.js';
const realNow = Date.now.bind(Date);
let offset = 0, camera, paints = [], visibility = [];
Date.now = () => realNow() + offset;
const origin = Date.now();
const point = time => ({truckId:'54777', truckExternalId:'fixture-54777',
  updatedAt: new Date(time).toISOString(), latitude:38+(time-origin)/3600000,
  longitude:-79, heading:0, speed:69});
document.addEventListener('visibilitychange', () => visibility.push(document.hidden));
const layer = createTruckLayer({addListener:()=>({remove(){}}),
  moveCamera:value=>{camera=value.center;}}, undefined, undefined, undefined,
  ()=>({update(){},setVisible(){},setSelected(){},dispose(){},render(position){
    paints.push({at:Date.now(),gps:position.gpsTime,latitude:position.latitude});
    if(paints.length>250) paints.shift();
    document.querySelector('#position').textContent = position.latitude.toFixed(6);
    document.querySelector('#marker').style.transform = 'translateX(' + ((position.gpsTime-origin)/1000%600) + 'px)';
  }}));
layer.setInitialTruck('54777');
function publish(){
  const now=Date.now(), points=[];
  for(let at=now-180000;at<=now;at+=5000) points.push(point(at));
  layer.setTrucks([point(now)],points);
}
window.fixture={publish, advance(ms){offset+=ms;}, clear(){paints=[];},
  exhaust(){layer.setTrucks([],[]);layer.setTrucks([point(Date.now()-truckPlaybackDelay)],[]);layer.setFollow('54777',true);},
  report:()=>({hidden:document.hidden,visibility,paints,camera,
    following:layer.isFollowing(),delay:truckPlaybackDelay,now:Date.now()}),
  dispose:()=>layer.dispose()};
publish(); layer.setFollow('54777',true);
`,
  },
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
const chrome = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const processHandle = spawn(
  existsSync(chrome) ? chrome : chromium.executablePath(),
  [
    `--user-data-dir=${output}/profile`,
    '--remote-debugging-port=0',
    '--no-first-run',
    '--no-default-browser-check',
    'about:blank',
  ],
  { stdio: 'ignore' },
);
let endpoint;
for (let attempt = 0; attempt < 100; attempt++) {
  try {
    const [port, path] = (
      await readFile(`${output}/profile/DevToolsActivePort`, 'utf8')
    )
      .trim()
      .split('\n');
    endpoint = `ws://127.0.0.1:${port}${path}`;
    break;
  } catch {
    await new Promise(resolve => setTimeout(resolve, 100));
  }
}
if (!endpoint) {
  processHandle.kill();
  throw new Error('Fixture Chrome did not start');
}
// A second CDP session cannot undo Playwright's session-owned focus emulation.
const browser = await chromium.connectOverCDP(endpoint, { noDefaults: true });
const report = {
  cases: [],
  errors: [],
  limitations:
    'Synthetic GPS and provider; real Chrome tab visibility and production truck playback. No live API or business writes.',
};
try {
  const context = browser.contexts()[0];
  await context.route('**/*', route => route.abort());
  const page = await context.newPage();
  const session = await context.newCDPSession(page);
  const { windowId } = await session.send('Browser.getWindowForTarget');
  await page.setViewportSize({ width: 1100, height: 650 });
  page.on('pageerror', error => report.errors.push(error.message));
  await page.route('http://truck-playback.invalid/', route =>
    route.fulfill({
      contentType: 'text/html',
      body: `<html><body style="font:20px system-ui;padding:32px">
    <h1>Truck playback — synthetic 54777</h1><p>Production animation · fixture GPS</p>
    <p>Latitude: <strong id="position"></strong></p>
    <div style="height:80px;background:#edf2fa"><span id="marker" style="display:inline-block;font-size:48px;color:#16803c">▲</span></div>
    </body></html>`,
    }),
  );
  await page.goto('http://truck-playback.invalid/');
  await page.addScriptTag({
    type: 'module',
    content: bundle.outputFiles[0].text,
  });
  await page.waitForFunction(() => window.fixture?.report().paints.length > 3);
  const cover = await context.newPage();
  await cover.route('http://playback-cover.invalid/', route =>
    route.fulfill({
      contentType: 'text/html',
      body: '<h1>Another tab — playback is hidden</h1>',
    }),
  );
  await cover.goto('http://playback-cover.invalid/');
  for (const mode of [
    'snapshot-while-hidden',
    'snapshot-after-return',
    'minimized-empty-buffer',
  ]) {
    await page.bringToFront();
    await page.waitForFunction(() => !document.hidden);
    await page.evaluate(() => window.fixture.clear());
    if (mode === 'minimized-empty-buffer') {
      await page.evaluate(() => window.fixture.exhaust());
      await session.send('Browser.setWindowBounds', {
        windowId,
        bounds: { windowState: 'minimized' },
      });
    } else await cover.bringToFront();
    await page.waitForFunction(() => document.hidden, null, {
      polling: 100,
      timeout: 5000,
    });
    const hiddenCount = await page.evaluate(
      () => window.fixture.report().paints.length,
    );
    await page.evaluate(mode => {
      window.fixture.advance(600000);
      if (mode !== 'snapshot-after-return') window.fixture.publish();
    }, mode);
    assert.equal(
      await page.evaluate(() => window.fixture.report().paints.length),
      hiddenCount,
    );
    if (mode === 'minimized-empty-buffer')
      await session.send('Browser.setWindowBounds', {
        windowId,
        bounds: { windowState: 'normal' },
      });
    await page.bringToFront();
    await page.waitForFunction(() => !document.hidden);
    if (mode === 'snapshot-after-return')
      await page.evaluate(() => window.fixture.publish());
    await page.waitForFunction(() => {
      const r = window.fixture.report(),
        p = r.paints.at(-1);
      return p && Math.abs(r.now - r.delay - p.gps) < 100;
    });
    await page.evaluate(() => window.fixture.clear());
    await page.waitForFunction(
      () => window.fixture.report().paints.length >= 25,
    );
    const state = await page.evaluate(() => window.fixture.report());
    const first = state.paints[0],
      last = state.paints.at(-1);
    const rate = (last.gps - first.gps) / (last.at - first.at);
    assert.ok(rate >= 0.9 && rate <= 1.1, `normal-time playback: ${rate}`);
    assert.ok(state.following);
    assert.equal(state.camera.lat, last.latitude);
    assert.ok(
      state.visibility.includes(true) && state.visibility.includes(false),
    );
    report.cases.push({
      mode,
      hiddenPaints: 0,
      playbackRate: rate,
      lagMs: last.at - last.gps,
      following: state.following,
    });
    await page.screenshot({ path: `${output}/${mode}.png` });
  }
  await page.evaluate(() => window.fixture.dispose());
  assert.deepEqual(report.errors, []);
} catch (error) {
  report.errors.push(error.stack);
  process.exitCode = 1;
} finally {
  await browser.close();
  processHandle.kill();
  await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2));
  console.log(JSON.stringify({ output, ...report }, null, 2));
}
