import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';
import {
  activityFixture,
  dispatchId,
  success,
  userId,
  workspaceFixture,
} from './dispatchWorkspaceFixture.mjs';

const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui');
const origin = 'http://localhost:5079';
const report = { cases: [], errors: [], unexpected: [] };
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
try {
  for (const [width, theme, scale] of [
    [1440, 'light', 100],
    [390, 'dark', 100],
    [320, 'light', 100],
    [390, 'dark', 200],
  ]) {
    const name = `${width}-${theme}-${scale}`;
    const context = await browser.newContext({
      viewport: { width, height: 1000 },
      reducedMotion: 'reduce',
      serviceWorkers: 'block',
    });
    await context.addInitScript(
      ({ userId, scale }) => {
        localStorage.setItem(
          'auth_session',
          JSON.stringify({
            Id: userId,
            AccessToken: 'fixture',
            RefreshToken: 'fixture',
          }),
        );
        document.addEventListener('DOMContentLoaded', () => {
          document.documentElement.style.fontSize = `${scale}%`;
        });
      },
      { userId, scale },
    );
    await installReleaseArtifact(context, artifact, origin);
    const writes = [];
    let lookups = 0;
    let saved = workspaceFixture();
    await context.route('**/*', async route => {
      const request = route.request();
      const url = new URL(request.url());
      const path = url.pathname;
      const method = request.method();
      if (url.origin !== origin) return route.abort('blockedbyclient');
      if (!path.startsWith('/api/')) {
        if (/\/appsettings(?:\.[^/]+)?\.json$/.test(path))
          return route.fulfill({ json: { GoogleMaps: { ApiKey: '' } } });
        return route.fallback();
      }
      const base = `/api/dispatch/${dispatchId}`;
      let value;
      if (method === 'POST' && path === '/api/dispatch/verify-address') {
        lookups++;
        value = success({
          address: `${lookups} Fixture Street`,
          city: 'Toronto',
          province: 'ON',
          country: 'CA',
          zipCode: 'M5V 1A1',
          latitude: 43.65 + lookups / 100,
          longitude: -79.38,
        });
      } else if (method === 'POST' && path === '/api/dispatch') {
        const body = request.postDataJSON();
        writes.push(body);
        assert.equal(body.stops.length, 2);
        assert.ok(body.stops.every(stop => stop.latitude && stop.name));
        saved = {
          ...saved,
          sourceName: '',
          sourceUpdatedAt: null,
          load: { ...saved.load, id: dispatchId, status: 'unassigned' },
          stops: body.stops.map(stop => ({ ...stop, isNew: false })),
        };
        value = success(saved);
      } else if (method === 'GET') {
        if (path === '/api/auth/me')
          value = {
            id: userId,
            name: 'Fixture Dispatcher',
            email: 'fixture@example.invalid',
            isAdmin: true,
          };
        else if (path === '/api/settings/appearance')
          value = success({
            theme,
            distanceUnit: 'both',
            temperatureUnit: 'celsius',
          });
        else if (path === '/api/settings/dispatch')
          value = success({ loadNumberPrefix: 'AMF', revision: 1 });
        else if (path === '/api/fleet/planning/previews') value = success([]);
        else if (path === `${base}/workspace`) value = success(saved);
        else if (path === `${base}/activity`)
          value = success(activityFixture());
        else if (path === `${base}/documents`) value = success([]);
        else if (path === `${base}/planning/map`)
          value = success({ dispatchId, segments: [], missingSections: 0 });
        else if (path === `${base}/execution/switch-workspace`)
          value = success({
            loads: [],
            trucks: [],
            drivers: [],
            trailers: [],
            operations: [],
          });
        else if (path.startsWith('/api/fleet/'))
          value = success({ items: [], totalCount: 0 });
        else if (path.startsWith(`${base}/mileage`))
          value = success({ dispatchId, movements: [], totals: [] });
      }
      if (value !== undefined) return route.fulfill({ json: value });
      report.unexpected.push(`${name}: ${method} ${path}`);
      return route.abort('blockedbyclient');
    });
    const page = await context.newPage();
    page.on('pageerror', error =>
      report.errors.push(`${name}: ${error.message}`),
    );
    await page.goto(`${origin}/dispatch/new`);
    await page
      .getByRole('heading', { name: 'New load', exact: true })
      .waitFor();
    await page.locator('#new-order').fill('Independent load');
    await page.locator('#new-customer').fill('Fixture customer');
    assert.equal(writes.length, 0);
    for (let index = 0; index < 2; index++) {
      if (width < 800)
        await page.getByRole('button', { name: 'Stops', exact: true }).click();
      await page.locator('.stop-workspace__select').nth(index).click();
      await page
        .getByLabel('Facility', { exact: true })
        .fill(`Facility ${index + 1}`);
      await page
        .getByLabel('Search address with Google')
        .fill(`${index + 1} Fixture Street`);
      assert.equal(lookups, index);
      await page.getByRole('button', { name: 'Search', exact: true }).click();
      await page.getByRole('button', { name: /Use address/ }).click();
    }
    assert.equal(writes.length, 0);
    assert.equal(lookups, 2);
    assert.ok(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth + 1,
      ),
    );
    await page.screenshot({ path: resolve(output, `${name}.png`) });
    await page
      .getByRole('button', { name: 'Create load', exact: true })
      .click();
    await page.waitForURL(`${origin}/dispatch/${dispatchId}`);
    assert.equal(writes.length, 1);
    assert.equal(writes[0].orderNumber, 'Independent load');
    report.cases.push({ name, created: true, lookups });
    await context.close();
  }
  assert.deepEqual(report.errors, []);
  assert.deepEqual(report.unexpected, []);
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  console.log(output);
}
