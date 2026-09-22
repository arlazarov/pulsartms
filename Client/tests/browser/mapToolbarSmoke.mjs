import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'A staged Client wwwroot is required',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui', process.env.UI_TEST_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const id = '11111111-1111-1111-1111-111111111111';
const trucks = ['11006', '11007'].map((unitNumber, index) => ({
  truckId: `22222222-2222-2222-2222-${String(index + 1).padStart(12, '0')}`,
  unitNumber,
  driverName: 'Example Driver',
  trailerNumber: 'TR-100',
  latitude: 40 + index,
  longitude: -80,
  updatedAt: new Date().toISOString(),
}));
const success = response => ({ success: true, response, errors: [] });
const fixtures = new Map([
  [
    '/api/auth/me',
    {
      id,
      name: 'Example Dispatcher',
      email: 'example@example.invalid',
      isAdmin: true,
    },
  ],
  ['/api/settings/dispatch', success({ loadNumberPrefix: 'AMF', revision: 1 })],
  ['/api/fleet/hos', success({})],
  ['/api/fleet/locations', success({ trucks, points: [] })],
  ['/api/fleet/planning/previews', success([])],
  ['/api/fuel/stations', success([])],
  ['/api/fuel/price-overview', success([])],
  [
    '/api/settings/planning',
    success({ preferences: { useIfta: true }, revision: 1, updatedAt: null }),
  ],
]);
const stub = `export async function createFleetMap(element) {
  window.toolbarMap = element;
  const noop = () => {};
  return {setOptions(value){window.toolbarOptions=value;},
    setTrucks:noop,setStationsVisible:noop,
    setStations:noop,setPriceOverview:noop,setTrafficVisible:noop,setIfta:noop,
    setNextLoadsVisible(value){window.toolbarNextLoads=value;},
    clearNextLoads:noop,clearSelection:noop,setDistanceUnit:noop,
    focusTruck:() => false,dispose:noop};
}`;
const integrity = `sha256-${createHash('sha256').update(stub).digest('base64')}`;
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g,
  (_all, open, json, close) => {
    const map = JSON.parse(json);
    for (const name of Object.keys(map.integrity ?? {}))
      if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(name))
        map.integrity[name] = integrity;
    return open + JSON.stringify(map) + close;
  },
);
const report = {
  cases: [],
  failures: [],
  browserErrors: [],
  unexpectedRequests: [],
  scope:
    'Actual staged Blazor toolbar, native inputs, CSS and themes; deterministic read-only API/map substitutes, no live backend or provider.',
};
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
try {
  for (const width of [1440, 900, 390, 320])
    for (const theme of ['light', 'dark'])
      for (const scale of [100, 200]) {
        const context = await browser.newContext({
          viewport: { width, height: 1000 },
          reducedMotion: 'reduce',
          serviceWorkers: 'block',
        });
        await context.addInitScript(
          ({ id, theme, scale }) => {
            const read = Storage.prototype.getItem;
            Storage.prototype.getItem = function (key) {
              const value = read.call(this, key);
              if (key === `pulsartms.fleet-map.preferences.${id}` && value) {
                return new Promise(resolve => {
                  window.releasePreferences = () => resolve(value);
                });
              }
              return value;
            };
            localStorage.setItem(
              'auth_session',
              JSON.stringify({
                Id: id,
                AccessToken: 'fixture',
                RefreshToken: 'fixture',
              }),
            );
            document.addEventListener('DOMContentLoaded', () => {
              document.documentElement.dataset.theme = theme;
              document.documentElement.style.fontSize = scale + '%';
            });
          },
          { id, theme, scale },
        );
        await installReleaseArtifact(context, artifact, origin);
        await context.route('**/*', async route => {
          const request = route.request(),
            url = new URL(request.url());
          if (url.origin !== origin || request.method() !== 'GET') {
            report.unexpectedRequests.push(
              `${request.method()} ${url.pathname}`,
            );
            return route.abort();
          }
          if (url.pathname.startsWith('/api/')) {
            const json =
              url.pathname === '/api/settings/appearance'
                ? success({ theme })
                : fixtures.get(url.pathname);
            if (!json) report.unexpectedRequests.push(url.pathname);
            return route.fulfill({
              status: json ? 200 : 500,
              json: json ?? {},
            });
          }
          if (
            /\/js\/generated\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(
              url.pathname,
            )
          )
            return route.fulfill({
              contentType: 'text/javascript',
              body: stub,
            });
          if (request.isNavigationRequest())
            return route.fulfill({ contentType: 'text/html', body: html });
          await route.fallback();
        });
        const page = await context.newPage();
        page.on('pageerror', error => report.browserErrors.push(error.message));
        await page.goto(origin + '/fleet/map');
        await page.waitForFunction(() => window.toolbarMap);
        const name = `${width}-${theme}-${scale}`;
        const mapRect = () =>
          page.locator('#fleet-map').evaluate(element => {
            const { x, y, width, height } = element.getBoundingClientRect();
            return {
              x,
              y,
              width,
              height,
              sameNode: element === window.toolbarMap,
            };
          });
        const original = await mapRect();
        const toolbar = page.locator('.fleet-map-toolbar');
        if (width < 768) {
          const header = await toolbar.evaluate(element => {
            const search = element
              .querySelector('.search-input')
              .getBoundingClientRect();
            const filters = element
              .querySelector('.fleet-map-mobile-filters')
              .getBoundingClientRect();
            return {
              separate:
                search.right <= filters.left || search.bottom <= filters.top,
            };
          });
          assert.ok(header.separate, `${name}: search overlaps Filters`);
        }
        await page.screenshot({ path: resolve(output, `${name}.png`) });
        const mobile = width < 768;
        const filterButton = page.getByRole('button', {
          name: 'Filters',
          exact: true,
        });
        if (mobile) {
          assert.equal(
            await page.locator('#fleet-map-filters').isVisible(),
            false,
          );
          await filterButton.click();
          assert.equal(
            await filterButton.getAttribute('aria-expanded'),
            'true',
          );
          assert.deepEqual(
            await mapRect(),
            original,
            `${name}: opening filters moved the map`,
          );
        }
        const geometry = await toolbar.evaluate(element => {
          const root = getComputedStyle(document.documentElement);
          const controls = [
            ...element.querySelectorAll(
              '.filter-toolbar__toggle, input:not([type=checkbox]), .fleet-map-mobile-filters',
            ),
          ]
            .filter(node => node.getBoundingClientRect().width > 0)
            .map(node => {
              const box = node.getBoundingClientRect();
              return {
                name: node.id || node.textContent.trim(),
                left: box.left,
                right: box.right,
                height: box.height,
                client: node.clientWidth,
                scroll: node.scrollWidth,
              };
            });
          return {
            controls,
            rootFont: parseFloat(root.fontSize),
            viewport: innerWidth,
            documentWidth: document.documentElement.scrollWidth,
          };
        });
        assert.ok(
          geometry.documentWidth <= width + 1,
          `${name}: document overflow`,
        );
        assert.deepEqual(
          (
            await toolbar
              .locator('.fleet-map-layer-controls label')
              .allTextContents()
          ).map(value => value.trim()),
          ['Fuel Stations', 'Traffic', 'Next loads'],
          `${name}: the map layers are the only chips`,
        );
        assert.equal(
          await page
            .getByRole('checkbox', { name: 'Trucks', exact: true })
            .count(),
          0,
        );
        assert.equal(
          await page
            .getByRole('checkbox', { name: 'Fuel Stations', exact: true })
            .evaluate(node => node.classList.contains('visually-hidden')),
          true,
        );
        for (const control of geometry.controls) {
          assert.ok(
            control.left >= 0 && control.right <= width + 1,
            `${name}: ${control.name} outside viewport`,
          );
          assert.ok(
            control.height >=
              geometry.rootFont * (width < 800 ? 2.75 : 2.5) - 1,
            `${name}: short control`,
          );
          assert.ok(
            control.scroll <= control.client + 1,
            `${name}: clipped control ${control.name}`,
          );
        }
        for (const label of ['Traffic', 'Fuel Stations', 'Next loads']) {
          const input = page.getByRole('checkbox', {
            name: label,
            exact: true,
          });
          const before = await input.isChecked();
          await page.keyboard.press('Tab');
          await input.focus();
          assert.equal(
            await input.evaluate(
              node => getComputedStyle(node.closest('label')).outlineStyle,
            ),
            'solid',
          );
          await input.press('Space');
          assert.equal(
            await input.isChecked(),
            !before,
            `${name}: keyboard ${label}`,
          );
          const chip = toolbar.locator('label').filter({ has: input });
          await chip.click();
          assert.equal(
            await input.isChecked(),
            before,
            `${name}: pointer ${label}`,
          );
        }
        await page.mouse.move(0, 0);
        await page.evaluate(
          () =>
            new Promise(resolve =>
              requestAnimationFrame(() => requestAnimationFrame(resolve)),
            ),
        );
        const colors = await toolbar
          .locator('.fleet-map-layers label')
          .evaluateAll(labels =>
            labels.map(label => ({
              checked: label.querySelector('input').checked,
              color: getComputedStyle(label).color,
              background: getComputedStyle(label).backgroundColor,
            })),
          );
        (report.chipColors ??= []).push({ name, colors });
        assert.notEqual(
          colors.find(value => value.checked).background,
          colors.find(value => !value.checked).background,
        );
        assert.equal(
          colors[0].background,
          colors[2].background,
          `${name}: inactive layers share a style`,
        );
        await page
          .locator(mobile ? '#fleet-map-filters' : '.fleet-map-toolbar')
          .screenshot({ path: resolve(output, `${name}-controls.png`) });
        if (mobile) {
          await filterButton.click();
          assert.equal(
            await page.locator('#fleet-map-filters').isVisible(),
            false,
          );
        }
        const search = page.getByRole('combobox', {
          name: 'Truck, driver or trailer',
        });
        await search.fill('1100');
        await page.getByRole('option').first().waitFor();
        assert.equal(await page.getByRole('option').count(), 2);
        assert.deepEqual(
          await mapRect(),
          original,
          `${name}: search results changed map bounds`,
        );
        await page.screenshot({ path: resolve(output, `${name}-search.png`) });
        await search.press('Escape');
        await search.fill('');
        assert.deepEqual(
          await mapRect(),
          original,
          `${name}: toolbar interaction replaced or moved map`,
        );
        if (mobile) await filterButton.click();
        for (const label of ['Fuel Stations', 'Next loads']) {
          await toolbar
            .locator('label')
            .filter({
              has: page.getByRole('checkbox', { name: label, exact: true }),
            })
            .click();
        }
        await toolbar
          .locator('label')
          .filter({
            has: page.getByRole('checkbox', { name: 'Traffic', exact: true }),
          })
          .click();
        if (mobile) await filterButton.click();
        await search.fill('1100');
        await page.reload();
        await page.waitForFunction(() => window.releasePreferences);
        const pending = await toolbar.evaluate(element => {
          const layers = element.querySelector('.fleet-map-layer-controls');
          window.retainedLayerControls = layers;
          const { x, y, width, height } = element.getBoundingClientRect();
          return {
            visibility: getComputedStyle(layers).visibility,
            busy: layers.getAttribute('aria-busy'),
            rect: { x, y, width, height },
          };
        });
        assert.equal(pending.visibility, 'hidden');
        assert.equal(pending.busy, 'true');
        await page.evaluate(() => window.releasePreferences());
        await page.waitForFunction(() => window.toolbarNextLoads === true);
        const settled = await toolbar.evaluate(element => {
          const layers = element.querySelector('.fleet-map-layer-controls');
          const { x, y, width, height } = element.getBoundingClientRect();
          return {
            retained: layers === window.retainedLayerControls,
            busy: layers.getAttribute('aria-busy'),
            rect: { x, y, width, height },
          };
        });
        assert.equal(settled.retained, true);
        assert.equal(settled.busy, 'false');
        assert.deepEqual(
          settled.rect,
          pending.rect,
          `${name}: restoring preferences must not move the toolbar`,
        );
        if (mobile) await filterButton.click();
        for (const label of ['Fuel Stations', 'Next loads']) {
          assert.equal(
            await page
              .getByRole('checkbox', {
                name: label,
                exact: true,
              })
              .isChecked(),
            true,
            `${name}: ${label} restored after reload`,
          );
        }
        assert.equal(
          await page
            .getByRole('checkbox', {
              name: 'Traffic',
              exact: true,
            })
            .isChecked(),
          false,
        );
        const restored = await page.evaluate(() => window.toolbarOptions);
        assert.equal(restored.useIfta, true);
        assert.equal(restored.stationsVisible, true);
        assert.equal(restored.trafficVisible, false);
        assert.equal(await search.inputValue(), '');
        report.cases.push({
          width,
          theme,
          scale,
          restoredPreferences: true,
          geometry,
          keyboardAndPointer: true,
          stableMap: true,
          search: true,
        });
        await context.close();
      }
} catch (error) {
  report.failures.push(error.stack ?? String(error));
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'toolbar-report.json'),
    JSON.stringify(report, null, 2),
  );
}
console.log(
  JSON.stringify(
    {
      output,
      cases: report.cases.length,
      failures: report.failures,
      browserErrors: report.browserErrors,
      unexpectedRequests: report.unexpectedRequests,
    },
    null,
    2,
  ),
);
if (
  report.cases.length !== 16 ||
  report.failures.length ||
  report.browserErrors.length ||
  report.unexpectedRequests.length
)
  process.exitCode = 1;
