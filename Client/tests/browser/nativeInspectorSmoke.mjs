import assert from 'node:assert/strict';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build } from 'esbuild';
import { chromium } from 'playwright';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'Use the exact verified release wwwroot for inspector styles',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput(
  'native-inspector',
  process.env.INSPECTOR_OUTPUT_DIR,
);
const origin = 'http://native-inspector.invalid';
const css = await readFile(resolve(artifact, 'css/main.css'), 'utf8');
const bundle = await build({
  entryPoints: ['tests/browser/nativeInspectorFixture.js'],
  bundle: true,
  write: false,
  format: 'esm',
  platform: 'browser',
});
const html = `<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="/styles.css">
<style>body{margin:0;background:var(--ui-canvas)}#fixture-page-heading{padding:16px}#fixture-markers{display:flex;flex-wrap:wrap;gap:8px;padding:8px}
.fleet-map-stage{height:780px}#fleet-map{width:100%;height:100%;background:var(--ui-surface-muted)}
</style></head><body><h1 id="fixture-page-heading">Fleet Map — native inspector integration</h1><div id="fixture-markers"></div>
<div class="fleet-map-stage"><div id="fleet-map" tabindex="-1"></div>
<section class="fleet-map-info-reserved fleet-map-inspector has-selection is-expanded" data-inspector-mode="truck">
<header class="fleet-map-inspector__header"><strong class="fleet-map-inspector__title">Truck 54777</strong>
<button class="btn btn--text btn--small fleet-map-inspector__back">Back to truck</button>
<button class="btn btn--small fleet-map-inspector__close" aria-label="Close map information">×</button></header>
<div class="fleet-map-inspector__native" hidden></div><div class="fleet-map-info-content">Truck 54777 · Current load AMF1375</div>
</section></div><script type="module" src="/fixture.js"></script></body></html>`;
const report = {
  artifact,
  scope:
    'Actual production docked controller, routeLayer/routeStops and stationLayer/stationPopup in a representative inspector shell with staged CSS. Only map/marker ports and shell binding are fixtures. No provider, GPU, API writes or Blazor component rendering.',
  cases: [],
  errors: [],
  requests: [],
};
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
try {
  for (const width of [1440, 390])
    for (const theme of ['light', 'dark'])
      for (const scale of [100, 200]) {
        const name = `${width}-${theme}-${scale}`;
        const context = await browser.newContext({
          viewport: { width, height: 1000 },
          colorScheme: theme,
          reducedMotion: 'reduce',
        });
        const themedHtml = html.replace(
          '<html>',
          `<html data-theme="${theme}" style="font-size:${scale}%">`,
        );
        await context.route('**/*', route => {
          const url = new URL(route.request().url());
          const resource =
            url.origin === origin &&
            (url.pathname === '/'
              ? ['text/html', themedHtml]
              : url.pathname === '/styles.css'
                ? ['text/css', css]
                : url.pathname === '/fixture.js'
                  ? ['text/javascript', bundle.outputFiles[0].text]
                  : null);
          if (resource)
            return route.fulfill({
              contentType: resource[0],
              body: resource[1],
            });
          report.requests.push(route.request().url());
          return route.abort();
        });
        const page = await context.newPage();
        page.on('pageerror', error =>
          report.errors.push(`${name}: ${error.message}`),
        );
        await page.goto(origin);
        await page.waitForFunction(() => window.nativeInspector?.ready);
        const map = page.locator('#fleet-map'),
          panel = page.locator('.fleet-map-inspector'),
          native = panel.locator('.fleet-map-inspector__native');
        const mapBefore = await map.evaluate(node => {
          const box = node.getBoundingClientRect();
          return {
            x: box.x + scrollX,
            y: box.y + scrollY,
            width: box.width,
            height: box.height,
          };
        });
        await native.evaluate(node => {
          window.originalNativeHost = node;
        });
        await page
          .getByRole('button', { name: 'Select current stop', exact: true })
          .click();
        await native.locator('.fleet-route-popup').waitFor();
        assert.match(await native.innerText(), /Schaeffler Group USA Inc/);
        assert.match(await native.innerText(), /AMF1375/);
        assert.match(await native.innerText(), /DL654321/);
        assert.match(await native.innerText(), /02:30 PM/);
        await page.evaluate(() => window.nativeInspector.poll());
        assert.match(await native.innerText(), /03:15 PM/);
        await page.screenshot({ path: resolve(output, `${name}-stop.png`) });
        await page
          .getByRole('button', { name: 'Select fuel station', exact: true })
          .click();
        await native.locator('.fleet-station-popup').waitFor();
        assert.equal(await native.locator('.fleet-route-popup').count(), 0);
        assert.equal(
          await native.locator('.fleet-station-popup__discount').innerText(),
          '5.500',
        );
        assert.deepEqual(
          await native.locator('.fleet-fuel-visit__percent').allTextContents(),
          ['20%', '100%'],
        );
        assert.match(
          await native.locator('.fleet-station-popup__cost-value').innerText(),
          /123\.45/,
        );
        await page.evaluate(() => {
          window.activeNativeFuel = document.querySelector(
            '.fleet-station-popup',
          );
        });
        const beforePoll = await page.evaluate(
          () => window.nativeInspector.events.length,
        );
        const copyAddress = native.getByRole('button', {
          name: /3499 Lee Jackson/,
        });
        await copyAddress.focus();
        await page.evaluate(() => window.nativeInspector.poll());
        assert.equal(
          await page.evaluate(() => window.nativeInspector.events.length),
          beforePoll,
          'polling does not open another inspector',
        );
        assert.equal(
          await page.evaluate(
            () =>
              window.activeNativeFuel ===
              document.querySelector('.fleet-station-popup'),
          ),
          true,
        );
        assert.equal(
          await native.locator('.fleet-route-popup').count(),
          0,
          'inactive ETA polling cannot replace fuel',
        );
        assert.equal(
          await copyAddress.evaluate(node => node === document.activeElement),
          true,
          'polling does not move keyboard focus',
        );
        for (const selector of [
          '.fleet-station-popup__prices',
          '.fleet-fuel-visit__levels',
          '.fleet-station-popup__actions',
        ]) {
          const field = native.locator(selector);
          await field.scrollIntoViewIfNeeded();
          await field.evaluate(node => {
            const panel = node.closest('.fleet-map-inspector');
            const headerBottom = panel
              .querySelector('.fleet-map-inspector__header')
              .getBoundingClientRect().bottom;
            const top = node.getBoundingClientRect().top;
            if (top < headerBottom) panel.scrollTop -= headerBottom - top;
          });
          assert.equal(
            await field.evaluate(node => {
              const shell = node.closest('.fleet-map-inspector'),
                bounds = node.getBoundingClientRect(),
                panel = shell.getBoundingClientRect();
              const headerBottom = shell
                .querySelector('.fleet-map-inspector__header')
                .getBoundingClientRect().bottom;
              return (
                bounds.left >= panel.left - 1 &&
                bounds.right <= panel.right + 1 &&
                bounds.top >= headerBottom - 1 &&
                bounds.bottom <= panel.bottom + 1
              );
            }),
            true,
            `${name}: ${selector} stays reachable inside the single inspector`,
          );
          if (width === 390 && selector === '.fleet-fuel-visit__levels')
            await page.screenshot({
              path: resolve(output, `${name}-fuel-levels.png`),
            });
        }
        await page.screenshot({ path: resolve(output, `${name}-fuel.png`) });
        await page.evaluate(() => window.nativeInspector.refreshViewport());
        const geometry = await panel.evaluate(node => {
          const panel = node.getBoundingClientRect(),
            map = document.querySelector('#fleet-map').getBoundingClientRect();
          const rootStyle = getComputedStyle(document.documentElement);
          const sideGap = (map.width - panel.width) / 2;
          const mapAt = x =>
            Boolean(
              document
                .elementFromPoint(
                  x,
                  panel.top + Math.min(panel.height, 100) / 2,
                )
                ?.closest('#fleet-map'),
            );
          return {
            panel: {
              x: panel.x + scrollX,
              y: panel.y + scrollY,
              width: panel.width,
              height: panel.height,
            },
            map: {
              x: map.x + scrollX,
              y: map.y + scrollY,
              width: map.width,
              height: map.height,
            },
            capToken: rootStyle
              .getPropertyValue('--size-map-fuel-inspector')
              .trim(),
            rootFont: parseFloat(rootStyle.fontSize),
            topGapToken: rootStyle.getPropertyValue('--space-md').trim(),
            sideMapInteractive:
              sideGap > 1
                ? [
                    mapAt(map.left + sideGap / 2),
                    mapAt(map.right - sideGap / 2),
                  ]
                : null,
            client: node.clientWidth,
            scroll: node.scrollWidth,
            documentWidth: document.documentElement.scrollWidth,
            sameHost:
              window.originalNativeHost ===
              document.querySelector('.fleet-map-inspector__native'),
            overflow: [...document.body.querySelectorAll('*')]
              .filter(
                element =>
                  element.getBoundingClientRect().right > innerWidth + 1,
              )
              .map(element => ({
                tag: element.tagName,
                className: element.className,
                right: element.getBoundingClientRect().right,
              })),
          };
        });
        assert.equal(geometry.sameHost, true);
        assert.ok(
          geometry.scroll <= geometry.client + 1 &&
            geometry.documentWidth <= width + 1,
          `${name}: horizontal overflow ${JSON.stringify(geometry)}`,
        );
        for (const key of ['x', 'y', 'width', 'height'])
          assert.ok(
            Math.abs(geometry.map[key] - mapBefore[key]) <= 1,
            `${name}: map ${key} changed from ${mapBefore[key]} to ${geometry.map[key]}`,
          );
        const cap = geometry.capToken.match(/^(\d+(?:\.\d+)?)rem$/);
        assert.ok(
          cap,
          `${name}: inspector width uses its named rem-based size token`,
        );
        assert.ok(
          Math.abs(
            geometry.panel.width -
              Math.min(geometry.map.width, Number(cap[1]) * geometry.rootFont),
          ) <= 1,
          `${name}: the inspector must not stretch beyond its named width cap`,
        );
        const topGap = geometry.topGapToken.match(/^(\d+(?:\.\d+)?)rem$/);
        assert.ok(
          topGap,
          `${name}: inspector top gap uses its named rem-based spacing token`,
        );
        const expectedTopGap = Math.min(
          Number(topGap[1]) * geometry.rootFont,
          Math.max(0, (geometry.map.width - geometry.panel.width) / 2),
        );
        assert.ok(
          Math.abs(geometry.panel.y - geometry.map.y - expectedTopGap) <= 1 &&
            Math.abs(
              geometry.panel.x +
                geometry.panel.width / 2 -
                geometry.map.x -
                geometry.map.width / 2,
            ) <= 1,
          'the single inspector is centered horizontally with its top gap bounded by actual side clearance',
        );
        if (geometry.sideMapInteractive !== null)
          assert.deepEqual(
            geometry.sideMapInteractive,
            [true, true],
            `${name}: both uncovered sides of the centered inspector remain interactive map areas`,
          );
        assert.equal(
          await page.locator('.fleet-map-details-card').count(),
          0,
          'no bottom popup shell is created',
        );
        const edit = native.getByRole('button', {
          name: 'Edit fuel plan',
          exact: true,
        });
        await edit.click();
        assert.equal(
          await page.evaluate(() => window.nativeInspector.edits.length),
          1,
        );
        await edit.press('Escape');
        assert.equal(
          await map.evaluate(node => node === document.activeElement),
          true,
          'Escape returns to the visible map',
        );
        assert.equal(await panel.getAttribute('data-inspector-mode'), 'truck');
        assert.equal(await native.locator('*').count(), 0);
        await page.evaluate(() => window.nativeInspector.poll());
        assert.equal(await panel.getAttribute('data-inspector-mode'), 'truck');
        await page
          .getByRole('button', { name: 'Select current stop', exact: true })
          .click();
        await panel
          .getByRole('button', { name: 'Back to truck', exact: true })
          .click();
        assert.equal(await panel.getAttribute('data-inspector-mode'), 'truck');
        assert.equal(
          await map.evaluate(node => node === document.activeElement),
          true,
          'Back returns to the visible map',
        );
        await page
          .getByRole('button', { name: 'Select current stop', exact: true })
          .click();
        await page
          .getByRole('button', { name: 'Blank map', exact: true })
          .click();
        assert.equal(await panel.getAttribute('data-inspector-mode'), 'truck');
        await page
          .getByRole('button', { name: 'Select fuel station', exact: true })
          .click();
        await panel
          .getByRole('button', { name: 'Close map information' })
          .click();
        assert.equal(
          await map.evaluate(node => node === document.activeElement),
          true,
          'Close returns to the visible map',
        );
        assert.equal(await panel.isVisible(), false);
        await page.evaluate(() => window.nativeInspector.poll());
        assert.equal(
          await panel.isVisible(),
          false,
          'closed inspector cannot reopen from price or ETA polling',
        );
        assert.equal(
          await page.evaluate(() => window.nativeInspector.plan.stops.length),
          1,
          'closing inspection retains the route',
        );
        await page.evaluate(() => window.nativeInspector.repeatStops());
        const repeatedButtons = page.getByRole('button', {
          name: /^Select load stop \d$/,
        });
        assert.equal(
          await repeatedButtons.count(),
          5,
          'all five distinct stop occurrences remain selectable',
        );
        for (const [number, time, visit] of [
          [1, '02:00 AM', 'Visit 1 of 3'],
          [2, '11:00 AM', null],
          [3, '01:00 PM', 'Visit 2 of 3'],
          [4, '02:00 PM', 'Visit 3 of 3'],
          [5, '05:00 AM', null],
        ]) {
          await page
            .getByRole('button', {
              name: `Select load stop ${number}`,
              exact: true,
            })
            .click();
          await native.locator('.fleet-route-popup').waitFor();
          assert.match(
            await native.locator('.fleet-route-popup__kind').innerText(),
            new RegExp(`Load stop ${number} of 5`),
          );
          assert.match(
            await native.locator('.fleet-route-popup__appointment').innerText(),
            new RegExp(time),
          );
          assert.match(await native.innerText(), /AMF1383/);
          const badge = native.locator('.fleet-route-popup__visit');
          if (visit) {
            assert.equal(await badge.innerText(), visit);
            assert.equal(
              await badge.getAttribute('title'),
              `${visit} at this address`,
            );
          } else assert.equal(await badge.count(), 0);
        }
        await page
          .getByRole('button', { name: 'Select load stop 4', exact: true })
          .click();
        for (const selector of [
          '.fleet-route-popup__kind',
          '.fleet-route-popup__appointment',
          '.fleet-route-popup__details-link',
        ]) {
          const content = native.locator(selector);
          await content.scrollIntoViewIfNeeded();
          await content.evaluate(node => {
            const panel = node.closest('.fleet-map-inspector');
            const header = panel
              .querySelector('.fleet-map-inspector__header')
              .getBoundingClientRect().bottom;
            if (node.getBoundingClientRect().top < header)
              panel.scrollTop -= header - node.getBoundingClientRect().top;
          });
          assert.equal(
            await content.evaluate(node => {
              const shell = node.closest('.fleet-map-inspector'),
                bounds = node.getBoundingClientRect(),
                panel = shell.getBoundingClientRect();
              const header = shell
                .querySelector('.fleet-map-inspector__header')
                .getBoundingClientRect().bottom;
              return (
                bounds.left >= panel.left - 1 &&
                bounds.right <= panel.right + 1 &&
                bounds.top >= header - 1 &&
                bounds.bottom <= panel.bottom + 1
              );
            }),
            true,
            `${name}: repeated-stop ${selector} remains reachable without clipping`,
          );
        }
        await native
          .locator('.fleet-route-popup__kind')
          .scrollIntoViewIfNeeded();
        await page.screenshot({
          path: resolve(output, `${name}-repeated-stop.png`),
        });
        assert.equal(
          await page.evaluate(() => window.nativeInspector.dispose()),
          0,
        );
        assert.equal(await native.locator('*').count(), 0);
        report.cases.push({ name, geometry, passed: true });
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
console.log(JSON.stringify(report, null, 2));
if (report.errors.length || report.requests.length || report.cases.length !== 8)
  process.exitCode = 1;
