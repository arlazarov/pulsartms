import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui');
const origin = 'http://localhost:5079';
const defaults = () => ({
  theme: 'light',
  temperatureUnit: 'both',
  distanceUnit: 'both',
});
const accounts = new Map([
  ['one', defaults()],
  ['two', defaults()],
]);
const success = response => ({ success: true, response, errors: [] });
const report = {
  artifact,
  cases: [],
  errors: [],
  unexpected: [],
  scope:
    'Staged Client with account-scoped synthetic API persistence. ' +
    'No live authentication, database, providers or writes.',
};
const browser = await chromium.launch({ headless: true, channel: 'chrome' });

async function openAccount(account, width) {
  const context = await browser.newContext({
    viewport: { width, height: 900 },
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
  });
  await context.addInitScript(() => {
    localStorage.setItem(
      'auth_session',
      JSON.stringify({
        Id: '11111111-1111-1111-1111-111111111111',
        AccessToken: 'fixture',
        RefreshToken: 'fixture',
      }),
    );
  });
  await installReleaseArtifact(context, artifact, origin);
  let writes = 0;
  await context.route('**/*', async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.origin !== origin) {
      report.unexpected.push(url.href);
      return route.abort();
    }
    if (url.pathname === '/api/settings/appearance') {
      if (request.method() === 'PUT') {
        const update = request.postDataJSON();
        assert.deepEqual(Object.keys(update), [
          'theme',
          'temperatureUnit',
          'distanceUnit',
        ]);
        const { theme, temperatureUnit, distanceUnit } = update;
        assert.ok(['light', 'dark'].includes(theme));
        const previous = accounts.get(account);
        accounts.set(account, {
          theme,
          temperatureUnit: temperatureUnit ?? previous.temperatureUnit,
          distanceUnit: distanceUnit ?? previous.distanceUnit,
        });
        writes++;
      } else assert.equal(request.method(), 'GET');
      return route.fulfill({
        json: success(accounts.get(account)),
      });
    }
    if (url.pathname === '/api/auth/me') {
      return route.fulfill({
        json: {
          id:
            account === 'one'
              ? '22222222-2222-2222-2222-222222222222'
              : '33333333-3333-3333-3333-333333333333',
          name: `Dispatcher ${account}`,
          email: 'fixture@example.invalid',
          isAdmin: false,
        },
      });
    }
    if (url.pathname === '/api/settings/dispatch') {
      return route.fulfill({
        json: success({
          loadNumberPrefix: 'AMF',
          revision: 1,
          temperatureUnit: 'fahrenheit',
          distanceUnit: 'miles',
        }),
      });
    }
    if (url.pathname.startsWith('/api/')) {
      report.unexpected.push(`${request.method()} ${url.pathname}`);
      return route.abort();
    }
    return route.fallback();
  });
  const page = await context.newPage();
  page.on('pageerror', error => report.errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') report.errors.push(message.text());
  });
  await page.goto(`${origin}/settings/personal`);
  await page
    .getByRole('heading', { name: 'Appearance', exact: true })
    .waitFor();
  assert.equal(
    await page.locator('html').getAttribute('data-theme'),
    accounts.get(account).theme,
  );
  assert.equal(
    await page.locator('#personal-temperature').inputValue(),
    accounts.get(account).temperatureUnit === 'fahrenheit'
      ? 'fahrenheit'
      : 'celsius',
  );
  assert.deepEqual(
    await page
      .locator('#personal-temperature option')
      .evaluateAll(options => options.map(option => option.value)),
    ['fahrenheit', 'celsius'],
  );
  assert.equal(
    await page.locator('#personal-distance').inputValue(),
    accounts.get(account).distanceUnit,
  );
  assert.equal(await page.locator('#settings-ifta').count(), 0);
  assert.equal(await page.locator('a[href="/settings"]').count(), 0);
  if (width < 800) {
    await page.getByRole('button', { name: 'Open menu', exact: true }).click();
  }
  await page.locator('.sidebar__account').click();
  await page
    .getByRole('link', { name: 'Personal settings', exact: true })
    .click();
  assert.equal(await page.locator('#sidebar-account-actions').count(), 0);
  return { context, page, writes: () => writes };
}

try {
  for (const width of [1440, 390]) {
    accounts.set('one', defaults());
    const first = await openAccount('one', width);
    await first.page.getByRole('button', { name: 'Dark', exact: true }).click();
    await first.page.getByText('Preferences saved to your account.').waitFor();
    assert.equal(
      await first.page.locator('html').getAttribute('data-theme'),
      'dark',
    );
    assert.equal(first.writes(), 1);
    await first.page
      .locator('#personal-temperature')
      .selectOption('fahrenheit');
    await first.page.getByText('Preferences saved to your account.').waitFor();
    await first.page.locator('#personal-distance').selectOption('kilometers');
    await first.page.getByText('Preferences saved to your account.').waitFor();
    assert.equal(first.writes(), 3);
    assert.deepEqual(accounts.get('one'), {
      theme: 'dark',
      temperatureUnit: 'fahrenheit',
      distanceUnit: 'kilometers',
    });
    await first.page.evaluate(
      () =>
        new Promise(resolve => {
          requestAnimationFrame(() => requestAnimationFrame(resolve));
        }),
    );
    const metrics = await first.page
      .locator('.settings-page__card')
      .first()
      .evaluate(element => ({
        background: getComputedStyle(element).backgroundColor,
        text: getComputedStyle(element).color,
        width: element.getBoundingClientRect().width,
        overflow: document.documentElement.scrollWidth > innerWidth,
        buttons: [...element.querySelectorAll('button')].map(button => ({
          height: button.getBoundingClientRect().height,
          font: getComputedStyle(button).fontSize,
        })),
      }));
    assert.equal(metrics.overflow, false);
    assert.notEqual(metrics.background, metrics.text);
    assert.ok(
      metrics.background
        .match(/\d+/g)
        .slice(0, 3)
        .every(channel => Number(channel) < 128),
    );
    assert.ok(
      metrics.buttons.every(button => button.height >= (width < 800 ? 44 : 32)),
    );
    await first.page.screenshot({
      path: resolve(output, `${width}-dark.png`),
      fullPage: true,
    });
    await first.context.close();

    const other = await openAccount('two', width);
    assert.equal(other.writes(), 0);
    await other.page.screenshot({
      path: resolve(output, `${width}-light.png`),
      fullPage: true,
    });
    await other.context.close();

    const newDevice = await openAccount('one', width);
    assert.equal(newDevice.writes(), 0);
    await newDevice.page
      .getByRole('button', { name: 'Light', exact: true })
      .click();
    await newDevice.page
      .getByText('Preferences saved to your account.')
      .waitFor();
    assert.deepEqual(accounts.get('one'), {
      theme: 'light',
      temperatureUnit: 'fahrenheit',
      distanceUnit: 'kilometers',
    });
    await newDevice.context.close();
    report.cases.push({ width, metrics, restoredOnNewDevice: true });
  }
  assert.deepEqual(report.errors, []);
  assert.deepEqual(report.unexpected, []);
} catch (error) {
  report.errors.push(error.stack ?? String(error));
  process.exitCode = 1;
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  console.log(JSON.stringify({ ...report, output }, null, 2));
}
