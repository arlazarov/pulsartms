import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';
import { installReleaseArtifact } from './releaseArtifact.mjs';

const baseURL = process.env.MAP_TEST_URL || 'http://localhost:5067';
const output = browserOutput(
  'map-lifecycle',
  process.env.MAP_LIFECYCLE_OUTPUT_DIR,
);
assert.ok(
  ['localhost', '127.0.0.1'].includes(new URL(baseURL).hostname),
  'Use a local client for this probe.',
);
const browser = await chromium.launch({ headless: false, channel: 'chrome' });
const context = await browser.newContext({
  viewport: { width: 1440, height: 900 },
});
if (process.env.MAP_TEST_ARTIFACT_DIR) {
  await installReleaseArtifact(
    context,
    process.env.MAP_TEST_ARTIFACT_DIR,
    baseURL,
  );
}
const page = await context.newPage();
const providerOnly = process.env.MAP_TEST_PROVIDER_ONLY === '1';
const clearProviderListeners =
  process.env.MAP_TEST_KEEP_PROVIDER_LISTENERS !== '1';
const providerScene =
  providerOnly && process.env.MAP_TEST_PROVIDER_SCENE === '1';
const providerTraffic =
  providerOnly && process.env.MAP_TEST_PROVIDER_TRAFFIC === '1';
const providerReuse =
  providerOnly && process.env.MAP_TEST_PROVIDER_REUSE === '1';
const soak = process.env.MAP_TEST_SOAK === '1';
assert.ok(!soak || !providerOnly, 'Soak mode requires the full application');
const cycles = soak ? 30 : 12;
const resultName = providerOnly
  ? `map-provider-${providerReuse ? 'reuse-' : ''}${providerScene ? 'scene-' : ''}${providerTraffic ? 'traffic-' : ''}${clearProviderListeners ? 'clear' : 'keep'}`
  : soak
    ? 'map-soak'
    : 'map-lifecycle';
const errors = [];
page.on('pageerror', error => {
  errors.push(error.message);
  console.error(error.message);
});
const samples = [];
const started = Date.now();
try {
  await page.goto(`${baseURL}/fleet/map`);
  console.log(
    'Sign in in the visible test browser if requested. Waiting up to five minutes for the map.',
  );
  await page
    .getByRole('combobox', { name: 'Truck, driver or trailer', exact: true })
    .waitFor({ timeout: 300000 });
  if (providerOnly) {
    await page
      .locator('#fleet-map canvas')
      .nth(1)
      .waitFor({ state: 'visible', timeout: 60000 });
    await page.getByRole('link', { name: 'Dispatch', exact: true }).click();
    await page.locator('#fleet-map').waitFor({ state: 'detached' });
  }
  const cdp = await context.newCDPSession(page);
  const captureHeap = process.env.MAP_TEST_HEAP === '1';
  await mkdir(output, { recursive: true });
  async function snapshot(name) {
    const chunks = [];
    const collect = ({ chunk }) => chunks.push(chunk);
    cdp.on('HeapProfiler.addHeapSnapshotChunk', collect);
    try {
      await cdp.send('HeapProfiler.takeHeapSnapshot', {
        reportProgress: false,
      });
      await writeFile(
        `${output}/${resultName}-${name}.heapsnapshot`,
        chunks.join(''),
      );
    } finally {
      cdp.off('HeapProfiler.addHeapSnapshotChunk', collect);
    }
  }
  for (let cycle = 0; cycle < cycles; cycle++) {
    let canvasCount;
    let activeBytes;
    if (providerOnly) {
      canvasCount = await page.evaluate(
        async ({ clearListeners, scene, traffic, reuse }) => {
          const cached = reuse && window.__mapLifecycleFixture;
          const host = cached?.host ?? document.createElement('div');
          host.style.cssText = 'position:fixed;inset:0;z-index:10000';
          document.body.append(host);
          const map =
            cached?.map ??
            new google.maps.Map(host, {
              center: { lat: 41.5, lng: -87.5 },
              zoom: 5,
              mapId: 'DEMO_MAP_ID',
              renderingType: google.maps.RenderingType.VECTOR,
            });
          if (reuse) window.__mapLifecycleFixture = { host, map };
          let gpuScene;
          let trafficLayer;
          try {
            if (!cached)
              await new Promise((resolve, reject) => {
                const timeout = setTimeout(
                  () => reject(new Error('Provider map idle timeout')),
                  30000,
                );
                google.maps.event.addListenerOnce(map, 'idle', () => {
                  clearTimeout(timeout);
                  resolve();
                });
              });
            if (scene) {
              const module = await import(
                '/js/generated/fleetMap/rendering/gpuScene.js'
              );
              gpuScene = module.createGpuScene(map);
            }
            if (traffic) {
              trafficLayer = new google.maps.TrafficLayer();
              trafficLayer.setMap(map);
            }
            await new Promise(resolve => setTimeout(resolve, 1000));
            return host.querySelectorAll('canvas').length;
          } finally {
            trafficLayer?.setMap(null);
            gpuScene?.dispose();
            if (!reuse && clearListeners)
              google.maps.event.clearInstanceListeners(map);
            if (!reuse) host.replaceChildren();
            host.remove();
          }
        },
        {
          clearListeners: clearProviderListeners,
          scene: providerScene,
          traffic: providerTraffic,
          reuse: providerReuse,
        },
      );
      assert.ok(
        canvasCount >= (providerScene ? 2 : 1),
        'Expected canvases must render',
      );
    } else {
      if (cycle)
        await page
          .getByRole('link', { name: 'Fleet Map', exact: true })
          .click();
      await page
        .locator('#fleet-map canvas')
        .nth(1)
        .waitFor({ state: 'visible', timeout: 60000 });
      await page.waitForTimeout(1000);
      canvasCount = await page.locator('#fleet-map canvas').count();
      if (soak) {
        await page
          .getByRole('combobox', {
            name: 'Truck, driver or trailer',
            exact: true,
          })
          .fill('11006');
        await page.getByRole('option').first().click({ timeout: 60000 });
        const follow = page.getByRole('button', {
          name: 'Follow',
          exact: true,
        });
        await follow.click();
        await page.waitForTimeout(5000);
        assert.equal(
          await follow.getAttribute('aria-pressed'),
          'true',
          'Follow must survive route loading',
        );
        const box = await page.locator('#fleet-map').boundingBox();
        await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await page.mouse.wheel(0, 500);
        await page.waitForFunction(
          () =>
            document
              .querySelector('button[aria-label="Follow"]')
              ?.getAttribute('aria-pressed') === 'false',
        );
        await follow.click();
        await page.waitForTimeout(5000);
        assert.equal(
          await follow.getAttribute('aria-pressed'),
          'true',
          'Follow must resume after manual zoom',
        );
        await cdp.send('HeapProfiler.collectGarbage');
        activeBytes = (await cdp.send('Runtime.getHeapUsage')).usedSize;
      }
      await page.getByRole('link', { name: 'Dispatch', exact: true }).click();
      await page
        .getByRole('heading', { name: 'Dispatch', exact: true })
        .waitFor();
      await page.locator('#fleet-map').waitFor({ state: 'detached' });
      assert.equal(await page.locator('#fleet-map canvas').count(), 0);
    }
    if (captureHeap) await new Promise(resolve => setTimeout(resolve, 1000));
    await cdp.send('HeapProfiler.collectGarbage');
    const heap = await cdp.send('Runtime.getHeapUsage');
    samples.push({
      cycle,
      canvasCount,
      activeBytes,
      usedBytes: heap.usedSize,
      elapsedSeconds: (Date.now() - started) / 1000,
    });
    console.log(JSON.stringify(samples.at(-1)));
    await writeFile(
      `${output}/${resultName}-progress.json`,
      JSON.stringify(samples, null, 2),
    );
    if (captureHeap && [3, cycles - 1].includes(cycle))
      await snapshot(`map-cycle-${cycle}`);
  }
  if (captureHeap) {
    await new Promise(resolve => setTimeout(resolve, 10000));
    await cdp.send('HeapProfiler.collectGarbage');
    console.log(
      JSON.stringify({ settledHeap: await cdp.send('Runtime.getHeapUsage') }),
    );
    await snapshot('map-settled');
  }
  assert.deepEqual(errors, [], 'Unexpected browser errors');
  const baseline =
    samples.slice(3, 6).reduce((sum, x) => sum + x.usedBytes, 0) / 3;
  const final = samples.slice(-3).reduce((sum, x) => sum + x.usedBytes, 0) / 3;
  const result = {
    soak,
    cycles,
    elapsedSeconds: (Date.now() - started) / 1000,
    providerOnly,
    providerScene,
    providerTraffic,
    providerReuse,
    clearProviderListeners,
    samples,
    retainedGrowthBytes: final - baseline,
    note: 'JS heap after GC, not GPU memory. Growth can include provider caches; review the trend.',
  };
  await mkdir(output, { recursive: true });
  await writeFile(
    `${output}/${resultName}.json`,
    JSON.stringify(result, null, 2),
  );
  console.log(
    `Retained JS heap change: ${((final - baseline) / 1048576).toFixed(2)} MiB`,
  );
  if (!providerOnly && process.env.MAP_TEST_SELECTION === '1') {
    let delayedRoutes = 0;
    await page.route(/\/planning(?:\/automatic)?(?:\?.*)?$/, async route => {
      const response = await route.fetch();
      await page.waitForTimeout(6000);
      delayedRoutes++;
      await route.fulfill({ response });
    });
    await page.getByRole('link', { name: 'Fleet Map', exact: true }).click();
    await page
      .locator('#fleet-map canvas')
      .nth(1)
      .waitFor({ state: 'visible' });
    await page
      .getByRole('combobox', { name: 'Truck, driver or trailer', exact: true })
      .fill('11006');
    await page.getByRole('option').first().click({ timeout: 60000 });
    const follow = page.getByRole('button', { name: 'Follow', exact: true });
    await follow.click();
    await page.waitForFunction(
      () =>
        document
          .querySelector('button[aria-label="Follow"]')
          ?.getAttribute('aria-pressed') === 'true',
    );
    await page.waitForTimeout(8000);
    assert.ok(delayedRoutes > 0, 'A delayed route response must reach the map');
    assert.equal(await follow.getAttribute('aria-pressed'), 'true');
    await page.screenshot({ path: `${output}/map-selection.png` });
    await follow.click();
    await page.waitForFunction(
      () =>
        document
          .querySelector('button[aria-label="Follow"]')
          ?.getAttribute('aria-pressed') === 'false',
    );
    assert.deepEqual(errors, [], 'Unexpected browser errors after selection');
  }
} catch (error) {
  await page.screenshot({ path: `${output}/map-failure.png` });
  throw error;
} finally {
  await browser.close();
}
