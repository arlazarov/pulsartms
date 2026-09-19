import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'Set MAP_TEST_ARTIFACT_DIR to the staged publish/wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui');
const origin = 'http://localhost:5079';
const userId = '11111111-1111-1111-1111-111111111111';
const accountName = 'Fixture Administrator';
const success = response => ({ success: true, response, errors: [] });
const fixtures = new Map([
  [
    '/api/auth/me',
    {
      id: userId,
      name: accountName,
      email: 'admin@example.invalid',
      isAdmin: true,
    },
  ],
  [
    '/api/users',
    success({
      items: [
        {
          id: '22222222-2222-2222-2222-222222222222',
          name: 'Fixture Dispatcher',
          email: 'dispatcher@example.invalid',
          role: 'Dispatch',
          isActive: true,
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    }),
  ],
  [
    '/api/settings/dispatch',
    success({ loadNumberPrefix: 'AMF', revision: 1, updatedAt: null }),
  ],
  ['/api/fleet/planning/previews', success([])],
]);
const report = {
  artifact,
  scope:
    'Actual staged Login, Users/sidebar and brand guide. Synthetic identity ' +
    'and read-only API fixtures; all other requests blocked. Root font ' +
    'scaling is not browser zoom. Anonymous dark artwork uses a theme fixture.',
  pixelScope:
    'Screenshots measure painted lettering/pulse regions and small-icon ' +
    'colors, including antialiased pulse pixels on white. They do not ' +
    'establish exact image fidelity or complete visual correctness.',
  cases: [],
  failures: [],
  browserErrors: [],
  unexpectedRequests: [],
  apiRequests: [],
};
const check = (condition, name, message) => {
  if (!condition) report.failures.push(`${name}: ${message}`);
};
const screenshot = (page, name) =>
  page.screenshot({
    path: resolve(output, `${name}.png`),
    fullPage: true,
    animations: 'disabled',
  });

await Promise.all(
  ['index.html', 'brand/index.html', 'brand/pulsr.svg', 'favicon.svg'].map(
    name => readFile(resolve(artifact, name)),
  ),
);
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  ...(process.env.UI_TEST_BROWSER_CHANNEL
    ? { channel: process.env.UI_TEST_BROWSER_CHANNEL }
    : {}),
});

async function newContext(name, width, theme, scale) {
  const context = await browser.newContext({
    viewport: { width, height: 1000 },
    deviceScaleFactor: 1,
    colorScheme: theme,
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    locale: 'en-US',
    timezoneId: 'America/Toronto',
  });
  context.setDefaultTimeout(15_000);
  context.setDefaultNavigationTimeout(30_000);
  await context.addInitScript(
    ({ theme, scale }) => {
      document.addEventListener('DOMContentLoaded', () => {
        document.documentElement.dataset.theme = theme;
        document.documentElement.style.fontSize = `${scale}%`;
      });
    },
    { theme, scale },
  );
  await installReleaseArtifact(context, artifact, origin);
  // Intercept before the artifact helper can forward an API request.
  await context.route('**/*', async route => {
    const request = route.request(),
      url = new URL(request.url());
    const description = `${request.method()} ${url.origin}${url.pathname}`;
    if (url.origin !== origin || !['GET', 'HEAD'].includes(request.method())) {
      report.unexpectedRequests.push({ case: name, request: description });
      return route.abort('blockedbyclient');
    }
    if (url.pathname === '/api' || url.pathname.startsWith('/api/')) {
      const fixture =
        url.pathname === '/api/settings/appearance'
          ? success({ theme })
          : fixtures.get(url.pathname);
      report.apiRequests.push({
        case: name,
        method: request.method(),
        path: url.pathname,
      });
      if (!fixture) {
        report.unexpectedRequests.push({ case: name, request: description });
        return route.fulfill({
          status: 500,
          json: { success: false, errors: ['Unexpected fixture API'] },
        });
      }
      return route.fulfill({ status: 200, json: fixture });
    }
    return route.fallback();
  });
  context.on('page', page => {
    page.on('pageerror', error =>
      report.browserErrors.push({ case: name, message: error.message }),
    );
    page.on('console', message => {
      if (message.type() === 'error')
        report.browserErrors.push({ case: name, message: message.text() });
    });
    page.on('response', response => {
      if (response.status() >= 400) {
        const url = new URL(response.url());
        report.unexpectedRequests.push({
          case: name,
          request: `HTTP ${response.status()} ${url.origin}${url.pathname}`,
        });
      }
    });
  });
  return context;
}

async function documentBounds(page, name) {
  const metrics = await page.evaluate(() => ({
    width: document.documentElement.clientWidth,
    documentWidth: document.documentElement.scrollWidth,
    bodyWidth: document.body.scrollWidth,
    rootFont: parseFloat(getComputedStyle(document.documentElement).fontSize),
    theme: document.documentElement.dataset.theme,
  }));
  check(
    metrics.documentWidth <= metrics.width + 1 &&
      metrics.bodyWidth <= metrics.width + 1,
    name,
    'no document-level horizontal overflow',
  );
  return metrics;
}

async function paintedPixels(page, bytes, colors) {
  return page.evaluate(
    async ({ png, colors }) => {
      const image = new Image();
      image.src = `data:image/png;base64,${png}`;
      await image.decode();
      const canvas = document.createElement('canvas');
      canvas.width = image.naturalWidth;
      canvas.height = image.naturalHeight;
      const context = canvas.getContext('2d', { willReadFrequently: true });
      const targets = colors.map(({ color, background, ...region }) => {
        context.clearRect(0, 0, 1, 1);
        context.fillStyle = color;
        context.fillRect(0, 0, 1, 1);
        const rgb = [...context.getImageData(0, 0, 1, 1).data].slice(0, 3);
        let backgroundRgb;
        if (background) {
          context.fillStyle = background;
          context.fillRect(0, 0, 1, 1);
          backgroundRgb = [...context.getImageData(0, 0, 1, 1).data].slice(
            0,
            3,
          );
        }
        return {
          ...region,
          color,
          rgb,
          backgroundRgb,
          count: 0,
        };
      });
      context.clearRect(0, 0, canvas.width, canvas.height);
      context.drawImage(image, 0, 0);
      const pixels = context.getImageData(
        0,
        0,
        canvas.width,
        canvas.height,
      ).data;
      for (let y = 0; y < canvas.height; y++)
        for (let x = 0; x < canvas.width; x++) {
          const offset = (y * canvas.width + x) * 4;
          for (const target of targets) {
            if (
              x < target.left * canvas.width ||
              x >= target.right * canvas.width ||
              y < (target.top ?? 0) * canvas.height ||
              y >= (target.bottom ?? 1) * canvas.height
            )
              continue;
            const opacities = target.backgroundRgb
              ? [0.5, 0.6, 0.7, 0.8, 0.9, 1]
              : [1];
            const matches = opacities.some(opacity =>
              target.rgb.every((value, channel) => {
                const painted =
                  value * opacity +
                  (target.backgroundRgb?.[channel] ?? 0) * (1 - opacity);
                return Math.abs(pixels[offset + channel] - painted) <= 24;
              }),
            );
            if (pixels[offset + 3] >= 240 && matches) target.count++;
          }
        }
      return { width: canvas.width, height: canvas.height, colors: targets };
    },
    { png: bytes.toString('base64'), colors },
  );
}

async function logoMetrics(page, selector, name, reversed) {
  const logo = page.locator(selector);
  await logo.waitFor({ state: 'visible' });
  await logo.scrollIntoViewIfNeeded();
  const metrics = await logo.evaluate(element => {
    const box = element.getBoundingClientRect(),
      parent = element.parentElement.getBoundingClientRect();
    const style = getComputedStyle(element);
    return {
      x: box.x,
      y: box.y,
      width: box.width,
      height: box.height,
      right: box.right,
      parent: { left: parent.left, right: parent.right },
      viewport: document.documentElement.clientWidth,
      role: element.getAttribute('role'),
      label: element.getAttribute('aria-label'),
      focusable: element.getAttribute('focusable'),
      href: element.querySelector('use')?.getAttribute('href'),
      reversed: element.classList.contains('brand-logo--reversed'),
      color: style.color,
    };
  });
  check(
    metrics.role === 'img' &&
      metrics.label.startsWith('PulsR TMS') &&
      metrics.focusable === 'false',
    name,
    'accessible non-focusable logo',
  );
  check(
    metrics.href?.endsWith('pulsr.svg?v=pulse-red-2#wordmark'),
    name,
    'shared wordmark reference',
  );
  check(metrics.reversed === reversed, name, 'expected reversed variant');
  check(
    metrics.width > 0 &&
      metrics.height > 0 &&
      Math.abs(metrics.width / metrics.height - 480 / 104) < 0.02,
    name,
    'logo preserves its aspect ratio',
  );
  check(
    metrics.x >= -1 &&
      metrics.right <= metrics.viewport + 1 &&
      metrics.x >= metrics.parent.left - 1 &&
      metrics.right <= metrics.parent.right + 1,
    name,
    'logo fits its container and viewport',
  );
  const crop = resolve(output, `${name}-logo.png`);
  const pixels = await paintedPixels(
    page,
    await logo.screenshot({ path: crop, animations: 'disabled' }),
    [
      {
        name: 'wordmark-body',
        color: metrics.color,
        left: 0.21,
        right: 0.8,
        minimumCoverage: 0.1,
      },
      {
        name: 'tms',
        color: metrics.color,
        left: 0.81,
        right: 1,
        minimumCoverage: 0.02,
      },
      {
        name: 'pulse-red',
        color: '#ec354b',
        left: 0,
        right: 0.09,
        minimumCoverage: 0.04,
      },
      {
        name: 'pulse-coral',
        color: '#ff6371',
        left: 0.09,
        right: 0.18,
        minimumCoverage: 0.04,
      },
    ],
  );
  for (const color of pixels.colors)
    check(
      color.count >
        pixels.width *
          pixels.height *
          (color.right - color.left) *
          color.minimumCoverage,
      name,
      `${color.name} paints visible pixels; an empty SVG reference must fail`,
    );
  return { ...metrics, pixels, screenshot: crop };
}

async function loginControls(page, name) {
  const metrics = await page.locator('.login-page form').evaluate(form => {
    const box = form.getBoundingClientRect();
    return [...form.querySelectorAll('input, button')].map(element => {
      const rect = element.getBoundingClientRect();
      return {
        name: element.id || element.textContent.trim(),
        tag: element.tagName,
        left: rect.left,
        right: rect.right,
        width: rect.width,
        height: rect.height,
        formLeft: box.left,
        formRight: box.right,
        viewport: document.documentElement.clientWidth,
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
      };
    });
  });
  check(
    metrics.length === 3,
    name,
    'email, password and sign-in controls remain present',
  );
  for (const control of metrics) {
    check(
      control.width > 0 &&
        control.height > 0 &&
        control.left >= -1 &&
        control.right <= control.viewport + 1 &&
        control.left >= control.formLeft - 1 &&
        control.right <= control.formRight + 1,
      name,
      `${control.name} fits the form and viewport`,
    );
    if (control.tag === 'BUTTON')
      check(
        control.scrollWidth <= control.clientWidth + 1,
        name,
        'sign-in label is not horizontally clipped',
      );
  }
  for (const selector of ['#email', '#password', 'button[type=submit]']) {
    const control = page.locator(`.login-page ${selector}`);
    await control.scrollIntoViewIfNeeded();
    check(
      await control.isVisible(),
      name,
      `${selector} remains reachable by scrolling`,
    );
  }
  return metrics;
}

async function appCase(width, theme, scale) {
  const name = `${width}-${theme}-${scale}`;
  const context = await newContext(name, width, theme, scale);
  const page = await context.newPage();
  try {
    await page.goto(`${origin}/login`);
    await page.locator('.login-page form').waitFor();
    // Anonymous sessions reset to Light; exercise dark artwork in the fixture.
    await page.evaluate(theme => {
      document.documentElement.dataset.theme = theme;
    }, theme);
    const loginName = `${name}-login`;
    const loginLogo = await logoMetrics(
      page,
      '.login-page__brand .brand-logo',
      loginName,
      false,
    );
    const controls = await loginControls(page, loginName);
    const loginBounds = await documentBounds(page, loginName);
    check(
      loginBounds.theme === theme &&
        Math.abs(loginBounds.rootFont - (16 * scale) / 100) < 0.1,
      loginName,
      'requested theme and root font scale apply',
    );
    await page.evaluate(() => window.scrollTo(0, 0));
    await screenshot(page, loginName);
    report.cases.push({
      name: loginName,
      logo: loginLogo,
      controls,
      bounds: loginBounds,
      screenshot: resolve(output, `${loginName}.png`),
    });

    await page.evaluate(
      userId =>
        localStorage.setItem(
          'auth_session',
          JSON.stringify({
            Id: userId,
            AccessToken: 'fixture',
            RefreshToken: 'fixture',
          }),
        ),
      userId,
    );
    await page.goto(`${origin}/users`);
    await page.locator('.users-page__user').waitFor();
    const usersName = `${name}-users`;
    const sidebarLogo = await logoMetrics(
      page,
      '.sidebar__brand .brand-logo',
      usersName,
      true,
    );
    check(
      sidebarLogo.width >= 112,
      usersName,
      'compact wordmark remains legible',
    );
    const userBounds = await documentBounds(page, usersName);
    await page.evaluate(() => window.scrollTo(0, 0));
    await screenshot(page, usersName);
    const menu = page.getByRole('button', { name: 'Open menu', exact: true });
    const hasMenu = await menu.isVisible();
    if (hasMenu) {
      await menu.click();
      const header = await page
        .locator('.sidebar__header')
        .evaluate(element => {
          const rect = element.getBoundingClientRect();
          const logo = element
            .querySelector('.brand-logo')
            .getBoundingClientRect();
          const toggle = element
            .querySelector('button')
            .getBoundingClientRect();
          const panel = document
            .querySelector('.sidebar__panel')
            .getBoundingClientRect();
          return {
            logoVisible: element.contains(
              document.elementFromPoint(
                logo.x + logo.width / 2,
                logo.bottom - 2,
              ),
            ),
            toggleVisible: element.contains(
              document.elementFromPoint(
                toggle.x + toggle.width / 2,
                toggle.bottom - 2,
              ),
            ),
            headerBottom: rect.bottom,
            panelTop: panel.top,
            panelBottom: panel.bottom,
            viewportHeight: innerHeight,
          };
        });
      check(
        header.logoVisible && header.toggleVisible,
        usersName,
        'open menu backdrop does not cover the header',
      );
      check(
        header.panelTop >= header.headerBottom - 1 &&
          header.panelBottom <= header.viewportHeight + 1,
        usersName,
        'menu scroll region fits below the actual header',
      );
      await documentBounds(page, `${usersName}-menu`);
    }
    const account = page.locator('.sidebar__account');
    await account.scrollIntoViewIfNeeded();
    check(
      (await account.locator('strong').innerText()) === accountName,
      usersName,
      'account name is the fixture identity',
    );
    check(
      (await account.locator('.sidebar__account-text > span').innerText()) ===
        'Administrator',
      usersName,
      'account role is preserved',
    );
    check(
      (await page.locator('.sidebar__avatar').innerText()) === 'FA',
      usersName,
      'product mark does not replace account initials',
    );
    check(
      (await page.locator('.sidebar__monogram, .sidebar__wordmark').count()) ===
        0,
      usersName,
      'old text monogram is absent',
    );
    if (hasMenu) {
      await screenshot(page, `${usersName}-menu`);
      await page.keyboard.press('Escape');
    }
    report.cases.push({
      name: usersName,
      logo: sidebarLogo,
      bounds: userBounds,
      accountName,
      screenshot: resolve(output, `${usersName}.png`),
      ...(hasMenu
        ? { menuScreenshot: resolve(output, `${usersName}-menu.png`) }
        : {}),
    });
  } catch (error) {
    report.failures.push(`${name}: ${error.message}`);
    await screenshot(page, `${name}-failure`).catch(() => {});
  } finally {
    await context.close();
  }
}

async function guideCase(width) {
  const name = `${width}-brand-guide`;
  const context = await newContext(name, width, 'light', 100);
  const page = await context.newPage();
  try {
    await page.goto(`${origin}/brand/index.html`);
    await page.locator('.brand-guide').waitFor();
    const primary = await logoMetrics(
      page,
      '.brand-guide__hero .brand-logo',
      `${name}-primary`,
      false,
    );
    const reversed = await logoMetrics(
      page,
      '.brand-guide__reversed .brand-logo',
      `${name}-reversed`,
      true,
    );
    await page.waitForFunction(() =>
      [...document.querySelectorAll('.brand-guide img')].every(
        image => image.complete && image.naturalWidth > 0,
      ),
    );
    const bounds = await documentBounds(page, name);
    check(
      bounds.theme === 'light',
      name,
      'standalone guide stays in its fixed light theme',
    );
    await page.evaluate(() => window.scrollTo(0, 0));
    await screenshot(page, name);
    report.cases.push({
      name,
      primary,
      reversed,
      bounds,
      screenshot: resolve(output, `${name}.png`),
    });
  } catch (error) {
    report.failures.push(`${name}: ${error.message}`);
    await screenshot(page, `${name}-failure`).catch(() => {});
  } finally {
    await context.close();
  }
}

async function faviconCase() {
  const name = 'favicon-16-32';
  const context = await newContext(name, 480, 'light', 100);
  const page = await context.newPage();
  try {
    await page.setContent(`<html lang="en"><head>
      <title>PulsR favicon size fixture</title></head>
      <body style="margin:32px;font:16px system-ui;
        background:#f5f7fb;color:#172438">
      <h1 style="font-size:20px">Staged favicon / actual CSS sizes</h1>
      <div style="display:flex;gap:48px;align-items:start">
      ${[16, 32]
        .map(
          size => `<figure style="margin:0">
        <img id="icon-${size}" src="${origin}/favicon.svg?v=pulse-red-2"
          width="${size}" height="${size}" alt="PulsR pulse at ${size}px">
        <figcaption>${size} × ${size}</figcaption></figure>`,
        )
        .join('')}
      </div></body></html>`);
    await page.waitForFunction(
      () =>
        [...document.images].length === 2 &&
        [...document.images].every(
          image => image.complete && image.naturalWidth > 0,
        ),
    );
    const icons = [];
    for (const size of [16, 32]) {
      const icon = page.locator(`#icon-${size}`),
        box = await icon.boundingBox();
      check(
        box?.width === size && box?.height === size,
        name,
        `favicon displays at ${size}px without scaling the fixture`,
      );
      const path = resolve(output, `favicon-${size}.png`);
      const pixels = await paintedPixels(
        page,
        await icon.screenshot({ path, animations: 'disabled' }),
        [
          { name: 'white-surface', color: '#ffffff', left: 0, right: 1 },
          {
            name: 'pulse-red',
            color: '#ec354b',
            background: '#ffffff',
            left: 0.2,
            right: 0.5,
            top: 0.2,
            bottom: 0.8,
          },
          {
            name: 'pulse-coral',
            color: '#ff6371',
            background: '#ffffff',
            left: 0.5,
            right: 0.85,
            top: 0.2,
            bottom: 0.8,
          },
        ],
      );
      check(
        pixels.colors[0].count > size * size * 0.2,
        name,
        `${size}px icon paints the white surface`,
      );
      check(
        pixels.colors[1].count > size * size * 0.01,
        name,
        `${size}px icon paints red pulse pixels inside its central area`,
      );
      check(
        pixels.colors[2].count > size * size * 0.01,
        name,
        `${size}px icon paints coral pulse pixels inside its central area`,
      );
      icons.push({ size, pixels, screenshot: path });
    }
    await screenshot(page, name);
    report.cases.push({
      name,
      scope: 'Test-only sizing fixture using the unchanged staged favicon SVG.',
      icons,
      screenshot: resolve(output, `${name}.png`),
    });
  } catch (error) {
    report.failures.push(`${name}: ${error.message}`);
    await screenshot(page, `${name}-failure`).catch(() => {});
  } finally {
    await context.close();
  }
}

try {
  for (const width of [1440, 768, 390, 320])
    for (const theme of ['light', 'dark'])
      for (const scale of [100, 200]) {
        await appCase(width, theme, scale);
      }
  for (const width of [1440, 390]) await guideCase(width);
  await faviconCase();
  check(
    report.cases.length === 35,
    'matrix',
    'all 32 application, two guide and one favicon cases completed',
  );
  assert.deepEqual(
    report.failures,
    [],
    'Brand layout or rendered-pixel assertions failed',
  );
  assert.deepEqual(report.browserErrors, [], 'Unexpected browser errors');
  assert.deepEqual(
    report.unexpectedRequests,
    [],
    'Unexpected requests were blocked or staged assets were missing',
  );
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  console.log(output);
}
