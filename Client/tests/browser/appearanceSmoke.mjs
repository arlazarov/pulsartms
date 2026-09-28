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
// The theme is dark only for now (owner decision of 2026-09-28): an
// account keeps whatever it saved - light, or nothing chosen ('') - and
// the page is dark regardless.
const accounts = new Map([
  ['one', defaults()],
  ['two', defaults()],
  ['three', { ...defaults(), theme: '' }],
]);
const puts = [];
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
  await context.addInitScript(saved => {
    // The device's own copy of the preference, and every theme the page
    // shows from its first paint on.
    if (saved) localStorage.setItem('pulsr.theme', saved);
    window.fixtureThemes = [];
    document.addEventListener('DOMContentLoaded', () => {
      const root = document.documentElement;
      window.fixtureThemes.push(root.dataset.theme ?? null);
      new MutationObserver(() =>
        window.fixtureThemes.push(root.dataset.theme ?? null),
      ).observe(root, { attributes: true, attributeFilter: ['data-theme'] });
    });
    localStorage.setItem(
      'auth_session',
      JSON.stringify({
        Id: '11111111-1111-1111-1111-111111111111',
        AccessToken: 'fixture',
        RefreshToken: 'fixture',
      }),
    );
  }, accounts.get(account).theme);
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
        puts.push({ account, theme: accounts.get(account).theme, update });
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
    // The layout's and the page's own reads, answered read-only: no driver
    // groups or drivers, an empty mailbox (its long poll held briefly).
    if (url.pathname === '/api/driver-groups')
      return route.fulfill({ json: success({ selected: null, groups: [] }) });
    if (url.pathname === '/api/fleet/drivers')
      return route.fulfill({ json: success({ totalCount: 0, items: [] }) });
    if (url.pathname === '/api/messaging/unread')
      return route.fulfill({
        json: success({ conversations: 0, more: false, newest: 0 }),
      });
    if (url.pathname === '/api/messaging/changes') {
      const mailbox = url.searchParams.get('mailbox');
      if (mailbox) await new Promise(done => setTimeout(done, 2000));
      return route.fulfill({
        json: success({
          mailbox: mailbox ?? '00000000-0000-4000-8000-00000000c4a9',
          resync: !mailbox,
          conversations: [],
        }),
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
  await page.getByRole('heading', { name: 'Units', exact: true }).waitFor();
  // Dark from the first paint whatever was saved, and no theme to choose,
  // in the top bar or here.
  const seen = await page.evaluate(() => window.fixtureThemes);
  assert.ok(
    seen.length > 0 && seen.every(theme => theme === 'dark'),
    `${account}: the page is dark from its first paint (${seen})`,
  );
  assert.equal(
    await page
      .getByRole('button', { name: /^Use the (light|dark) theme$/ })
      .count(),
    0,
  );
  assert.equal(await page.getByRole('group', { name: 'Theme' }).count(), 0);
  for (const label of ['Light', 'Dark'])
    assert.equal(
      await page.getByRole('button', { name: label, exact: true }).count(),
      0,
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
  // From 800px the account menu is the top bar's (AccountMenu, Block
  // topbar); a phone keeps it in the navigation menu.
  const menu = width < 800 ? 'sidebar' : 'topbar';
  await page.locator(`.${menu}__account`).click();
  await page
    .getByRole('link', { name: 'Personal settings', exact: true })
    .click();
  assert.equal(await page.locator(`#${menu}-account-actions`).count(), 0);
  return { context, page, writes: () => writes };
}

try {
  for (const width of [1440, 390]) {
    accounts.set('one', defaults());
    accounts.set('three', { ...defaults(), theme: '' });
    puts.length = 0;
    // A saved light theme: dark on screen, nothing written on opening,
    // and a units change saves the theme unchanged.
    const first = await openAccount('one', width);
    assert.equal(first.writes(), 0, 'opening writes nothing');
    await first.page
      .locator('#personal-temperature')
      .selectOption('fahrenheit');
    await first.page.getByText('Preferences saved to your account.').waitFor();
    await first.page.locator('#personal-distance').selectOption('kilometers');
    await first.page.getByText('Preferences saved to your account.').waitFor();
    assert.equal(first.writes(), 2);
    assert.ok(
      puts.every(put => put.update.theme === 'light'),
      `a saved light theme is sent back unchanged: ${JSON.stringify(puts)}`,
    );
    assert.deepEqual(accounts.get('one'), {
      theme: 'light',
      temperatureUnit: 'fahrenheit',
      distanceUnit: 'kilometers',
    });
    assert.equal(
      await first.page.locator('html').getAttribute('data-theme'),
      'dark',
    );
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
    // The card is dark although the account saved light.
    // The glass reports its colour as rgb() or as CSS Color 4
    // color(srgb r g b / a) with channels 0-1; either way it is dark.
    const channels = metrics.background.startsWith('color(srgb')
      ? metrics.background
          .match(/[\d.]+/g)
          .slice(0, 3)
          .map(value => Number(value) * 255)
      : metrics.background.match(/\d+/g).slice(0, 3).map(Number);
    assert.ok(
      channels.every(channel => channel < 128),
      `the settings card is dark: ${metrics.background}`,
    );
    assert.ok(
      metrics.buttons.every(button => button.height >= (width < 800 ? 44 : 32)),
    );
    await first.page.screenshot({
      path: resolve(output, `${width}-saved-light.png`),
      fullPage: true,
    });
    await first.context.close();

    // Another account's light preference is its own; opening it writes
    // nothing and shows dark.
    const other = await openAccount('two', width);
    assert.equal(other.writes(), 0);
    await other.page.screenshot({
      path: resolve(output, `${width}-other-account.png`),
      fullPage: true,
    });
    await other.context.close();

    // No theme chosen yet: dark, nothing written on opening, and a units
    // change sends "dark", the server accepting only light or dark.
    const unchosen = await openAccount('three', width);
    assert.equal(unchosen.writes(), 0);
    const before = puts.length;
    await unchosen.page
      .locator('#personal-distance')
      .selectOption('kilometers');
    await unchosen.page
      .getByText('Preferences saved to your account.')
      .waitFor();
    assert.equal(unchosen.writes(), 1);
    assert.equal(puts.at(-1).update.theme, 'dark');
    assert.equal(puts.length, before + 1);
    await unchosen.context.close();

    // A new device: the light preference is kept, still dark, no write.
    const newDevice = await openAccount('one', width);
    assert.equal(newDevice.writes(), 0);
    assert.equal(accounts.get('one').theme, 'light');
    await newDevice.context.close();
    report.cases.push({ width, metrics, puts: [...puts] });
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
