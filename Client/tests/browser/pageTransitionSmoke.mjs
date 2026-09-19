import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(process.env.MAP_TEST_ARTIFACT_DIR);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui', process.env.PAGE_TRANSITION_TEST_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const id = '11111111-1111-1111-1111-111111111111';
const success = response => ({ success: true, response, errors: [] });
const emptyPage = success({
  items: [],
  page: 1,
  pageSize: 20,
  totalCount: 0,
  totalPages: 0,
});
const fixtures = new Map([
  [
    '/api/auth/me',
    {
      id,
      name: 'Fixture Administrator',
      email: 'fixture@example.invalid',
      isAdmin: true,
    },
  ],
  ['/api/users', emptyPage],
  [
    '/api/settings/dispatch',
    success({
      loadNumberPrefix: 'AMF',
      revision: 1,
      updatedAt: null,
    }),
  ],
  ['/api/settings/integrations', success([])],
  [
    '/api/settings/planning',
    success({
      preferences: { useIfta: true },
      revision: 1,
      updatedAt: null,
    }),
  ],
  ['/api/fleet/locations', success({ trucks: [], points: [] })],
  ['/api/fleet/hos', success({})],
  ['/api/fleet/planning/previews', success([])],
  ['/api/fuel/price-overview', success([])],
  ['/api/dispatch/board', emptyPage],
  ['/api/dispatch/board/telemetry', success([])],
  ['/api/dispatch/board/enrichment', success([])],
]);
const mapStub = `export async function createFleetMap(element) {
  element.dataset.transitionFixture = 'offline-map';
  return {setOptions(){},setTrucks(){},setStationsVisible(){},
    setTrafficVisible(){},setIfta(){},clearSelection(){},clearNextLoads(){},
    setNextLoadsVisible(){},setDistanceUnit(){},setPriceOverview(){},
    dispose(){delete element.dataset.transitionFixture;}};
}`;
const integrity =
  'sha256-' + createHash('sha256').update(mapStub).digest('base64');
const html = (await readFile(resolve(artifact, 'index.html'), 'utf8')).replace(
  /(<script\b[^>]*type="importmap"[^>]*>)([\s\S]*?)(<\/script>)/g,
  (_all, open, json, close) => {
    const map = JSON.parse(json);
    for (const name of Object.keys(map.integrity ?? {})) {
      if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(name)) {
        map.integrity[name] = integrity;
      }
    }
    return open + JSON.stringify(map) + close;
  },
);
const report = {
  artifact,
  scope:
    'Staged Blazor navigation with synthetic APIs and map provider. ' +
    'No live authentication, database, provider calls or business writes.',
  cases: [],
  errors: [],
  unexpectedRequests: [],
};
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});

async function runCase(width, theme, reducedMotion) {
  const name = `${width}-${theme}-${reducedMotion}`;
  const context = await browser.newContext({
    viewport: { width, height: 1000 },
    colorScheme: theme,
    reducedMotion,
    serviceWorkers: 'block',
  });
  let releaseSettings;
  const settingsReady = new Promise(resolve => {
    releaseSettings = resolve;
  });
  await context.addInitScript(
    ({ id, theme }) => {
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
      });
      window.pageTransitionEvents = [];
      document.addEventListener('animationstart', event => {
        if (!event.animationName.startsWith('page-enter-')) return;
        window.pageTransitionEvents.push(event.animationName);
        const animation = event.target.getAnimations()[0];
        animation.pause();
      });
    },
    { id, theme },
  );
  await installReleaseArtifact(context, artifact, origin);
  await context.route('**/*', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const readPlanning =
      request.method() === 'POST' &&
      url.pathname === '/api/dispatch/board/planning';
    if (
      url.origin !== origin ||
      (!['GET', 'HEAD'].includes(request.method()) && !readPlanning)
    ) {
      report.unexpectedRequests.push(`${name}: ${request.method()} ${url}`);
      return route.abort('blockedbyclient');
    }
    if (url.pathname.startsWith('/api/')) {
      if (url.pathname === '/api/settings/planning') await settingsReady;
      const fixture =
        url.pathname === '/api/settings/appearance'
          ? success({ theme })
          : readPlanning
            ? success([])
            : fixtures.get(url.pathname);
      if (!fixture) {
        report.unexpectedRequests.push(`${name}: ${url.pathname}`);
        return route.fulfill({ status: 500, json: success(null) });
      }
      return route.fulfill({ status: 200, json: fixture });
    }
    if (/\/fleetMap\/fleetMap(?:\.[\w]+)?\.js$/.test(url.pathname)) {
      return route.fulfill({
        status: 200,
        contentType: 'text/javascript',
        body: mapStub,
      });
    }
    if (request.isNavigationRequest()) {
      return route.fulfill({
        status: 200,
        contentType: 'text/html',
        body: html,
      });
    }
    return route.fallback();
  });
  const page = await context.newPage();
  page.on('pageerror', error => {
    report.errors.push(`${name}: ${error.message}`);
  });
  page.on('console', message => {
    if (message.type() === 'error') {
      report.errors.push(`${name}: ${message.text()}`);
    }
  });
  try {
    await page.goto(`${origin}/users`);
    await page.locator('.users-page .data-table-pagination').waitFor();
    await page.evaluate(() => {
      window.transitionMain = document.querySelector('main');
      window.transitionSidebar = document.querySelector('.sidebar');
    });
    const main = page.locator('main');
    assert.equal(await main.getAttribute('data-page-transition'), null);
    const transitions = [];
    for (const [index, target] of [
      ['Settings', '.settings-page'],
      ['Fleet Map', '#fleet-map'],
      ['Dispatch', '#dispatch-search'],
      ['Users', '.users-page'],
    ].entries()) {
      const menu = page.getByRole('button', { name: 'Open menu', exact: true });
      if (await menu.isVisible()) await menu.click();
      await page.getByRole('link', { name: target[0], exact: true }).focus();
      await page.keyboard.press('Enter');
      await page.locator(target[1]).waitFor();
      await page.waitForFunction(
        expected => {
          const phase = document.querySelector('main').dataset.pageTransition;
          return phase === expected;
        },
        index % 2 === 0 ? 'a' : 'b',
      );
      if (reducedMotion !== 'reduce') {
        await page.waitForFunction(count => {
          return window.pageTransitionEvents.length === count;
        }, index + 1);
      }
      const metrics = await main.evaluate(element => {
        const rect = () => {
          const bounds = element.getBoundingClientRect();
          return [bounds.x, bounds.y, bounds.width, bounds.height];
        };
        const animation = element.getAnimations()[0];
        const frames = [];
        if (animation) {
          for (const time of [0, 90, 180]) {
            animation.currentTime = time;
            frames.push({
              rect: rect(),
              opacity: getComputedStyle(element).opacity,
            });
          }
          animation.currentTime = 90;
        }
        return {
          sameMain: element === window.transitionMain,
          sameSidebar:
            document.querySelector('.sidebar') === window.transitionSidebar,
          mainCount: document.querySelectorAll('main').length,
          animation: getComputedStyle(element).animationName,
          duration: animation?.effect.getTiming().duration,
          transform: getComputedStyle(element).transform,
          frames,
          eventCount: window.pageTransitionEvents.length,
        };
      });
      assert.equal(metrics.sameMain && metrics.sameSidebar, true);
      assert.equal(metrics.mainCount, 1);
      assert.equal(metrics.transform, 'none');
      if (reducedMotion === 'reduce') {
        assert.equal(metrics.animation, 'none');
        assert.equal(metrics.eventCount, 0);
      } else {
        assert.equal(metrics.duration, 180);
        assert.equal(metrics.frames[0].opacity, '0');
        assert.equal(metrics.frames[2].opacity, '1');
        assert.deepEqual(metrics.frames[0].rect, metrics.frames[2].rect);
      }
      await page.screenshot({ path: resolve(output, `${name}-${index}.png`) });
      await main.evaluate(async element => {
        const animation = element.getAnimations()[0];
        if (animation) {
          animation.play();
          await animation.finished;
        }
      });
      if (index === 0) {
        releaseSettings();
        await page.locator('#settings-ifta').waitFor();
        assert.equal(
          await page.evaluate(() => window.pageTransitionEvents.length),
          metrics.eventCount,
        );
      }
      if (index === 1) {
        await page.locator('[data-transition-fixture]').waitFor();
      }
      transitions.push(metrics);
    }
    report.cases.push({ name, transitions });
  } finally {
    releaseSettings();
    await context.close();
  }
}

try {
  for (const width of [1440, 390]) {
    for (const theme of ['light', 'dark']) {
      for (const motion of ['no-preference', 'reduce']) {
        await runCase(width, theme, motion);
      }
    }
  }
  assert.deepEqual(report.errors, []);
  assert.deepEqual(report.unexpectedRequests, []);
} catch (error) {
  report.errors.push(error.stack ?? String(error));
  process.exitCode = 1;
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  console.log(
    JSON.stringify(
      {
        cases: report.cases.length,
        errors: report.errors,
        unexpectedRequests: report.unexpectedRequests,
        output,
      },
      null,
      2,
    ),
  );
}
