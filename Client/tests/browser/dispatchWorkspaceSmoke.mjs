import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';
import {
  activityFixture,
  dispatchId,
  fixtureId,
  success,
  userId,
  workspaceFixture,
} from './dispatchWorkspaceFixture.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'Set MAP_TEST_ARTIFACT_DIR to a strict staged publish/wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('ui');
const origin = 'http://localhost:5079';
const report = {
  artifact,
  scope:
    'Real staged Blazor Dispatch workspace; synthetic identity and all API ' +
    'responses. Saves, notes, resolves and uploads are intercepted ' +
    'in memory. ' +
    'No live credentials, database, provider or business writes. Root text ' +
    'scaling is not browser zoom or proof of complete visual correctness.',
  cases: [],
  failures: [],
  browserErrors: [],
  unexpectedRequests: [],
};
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  ...(process.env.UI_TEST_BROWSER_CHANNEL
    ? { channel: process.env.UI_TEST_BROWSER_CHANNEL }
    : {}),
});

async function fixtureContext(name, width, theme, scale) {
  const context = await browser.newContext({
    viewport: { width, height: width < 800 ? 900 : 1000 },
    colorScheme: theme,
    reducedMotion: 'reduce',
    hasTouch: width < 800,
    serviceWorkers: 'block',
    locale: 'en-US',
    timezoneId: 'America/Toronto',
  });
  context.setDefaultTimeout(20_000);
  context.setDefaultNavigationTimeout(30_000);
  await context.addInitScript(
    ({ userId, scale, origin }) => {
      if (location.origin !== origin) return;
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
    { userId, scale, origin },
  );
  const state = {
    workspace: workspaceFixture(),
    activity: activityFixture(),
    documents: [],
    writes: [],
    apiRequests: [],
  };
  const resources = (truck, truckId = null) => ({
    truck,
    truckId,
    driver: '',
    coDriver: '',
    trailer: '',
    driverId: null,
    coDriverId: null,
    trailerId: null,
  });
  state.workspace.sourceReviewReason = 'Imported resources need review.';
  state.workspace.sourceAssignment = {
    header: resources('Imported header truck'),
    visits: state.workspace.stops.map(stop => ({
      sequence: stop.sequence,
      job: stop.job,
      name: stop.name,
      resources: resources('Unmatched source truck'),
    })),
  };
  state.workspace.acceptedAssignments = [
    {
      executionLegId: fixtureId(40),
      revision: 2,
      status: 'active',
      from: 'Accepted pickup',
      through: 'Accepted transfer',
      resources: resources('Accepted truck 11005', fixtureId(50)),
    },
  ];
  await installReleaseArtifact(context, artifact, origin);
  await context.route('**/*', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const path = url.pathname;
    const description = `${method} ${url.origin}${path}`;
    if (url.origin !== origin) {
      report.unexpectedRequests.push({ name, request: description });
      return route.abort('blockedbyclient');
    }
    if (!path.startsWith('/api/')) {
      if (/\/appsettings(?:\.[^/]+)?\.json$/.test(path)) {
        return route.fulfill({ json: { GoogleMaps: { ApiKey: '' } } });
      }
      if (method === 'GET' || method === 'HEAD') return route.fallback();
      report.unexpectedRequests.push({ name, request: description });
      return route.abort('blockedbyclient');
    }
    state.apiRequests.push({ method, path });
    const base = `/api/dispatch/${dispatchId}`;
    let value;
    if (method === 'GET') {
      if (path === '/api/auth/me')
        value = {
          id: userId,
          name: 'Fixture Dispatcher',
          email: 'dispatcher@example.invalid',
          isAdmin: true,
        };
      else if (path === '/api/settings/appearance')
        value = success({
          theme,
          distanceUnit: 'both',
          temperatureUnit: 'celsius',
        });
      else if (path === '/api/settings/dispatch')
        value = success({
          loadNumberPrefix: 'AMF',
          revision: 1,
        });
      else if (path === '/api/fleet/planning/previews') value = success([]);
      else if (path === '/api/fleet/drivers')
        value = success({
          totalCount: 1,
          items: [
            { id: fixtureId(91), name: 'Fixture driver', isActive: true },
          ],
        });
      else if (path === `${base}/workspace`) value = success(state.workspace);
      else if (path === `${base}/planning/map`)
        value = success({ dispatchId, segments: [], missingSections: 0 });
      else if (path === `${base}/activity`) value = success(state.activity);
      else if (path === `${base}/documents`) value = success(state.documents);
      else if (path === `${base}/execution/switch-workspace`)
        value = success({
          loads: [
            {
              dispatchId,
              loadNumber: state.workspace.load.loadNumber,
              sourceSignature: state.workspace.sourceFingerprint,
              outgoing: null,
              outgoingLegId: null,
              expectedOutgoingRevision: null,
              visits: state.workspace.stops,
            },
          ],
          trucks: [],
          drivers: [],
          trailers: [],
          operations: [],
        });
    } else if (method === 'PUT' && path === `${base}/workspace`) {
      const draft = request.postDataJSON();
      state.writes.push({ method, path, body: draft });
      assert.equal(draft.expectedRevision, state.workspace.revision);
      state.workspace = {
        ...state.workspace,
        revision: state.workspace.revision + 1,
        metadata: draft.metadata,
        stops: draft.stops,
        load: {
          ...state.workspace.load,
          stops: draft.stops.map(stop => ({
            ...state.workspace.load.stops.find(saved => saved.id === stop.id),
            ...stop,
          })),
        },
        history: [
          {
            revision: state.workspace.revision + 1,
            recordedAt: '2026-09-14T15:00:00Z',
            actorName: 'Fixture Dispatcher',
            summary: 'Load details and stop sequence updated.',
          },
        ],
      };
      value = success(state.workspace);
    } else if (method === 'POST' && path === `${base}/activity`) {
      const body = request.postDataJSON();
      state.writes.push({ method, path, body });
      const item = {
        ...state.activity.items[0],
        id: fixtureId(71),
        createdRevision: ++state.activity.revision,
        revision: state.activity.revision,
        kind: body.kind,
        text: body.text,
        stopId: body.stopId,
        needsAttention: body.needsAttention,
        resolvedBy: null,
        resolvedByName: null,
        resolvedAt: null,
      };
      state.activity.items.unshift(item);
      if (item.needsAttention) state.activity.openItems.push(item);
      state.activity.openCount = state.activity.openItems.length;
      value = success(item);
    } else if (method === 'POST' && path.endsWith('/resolve')) {
      const entryId = path.split('/').at(-2);
      const item = state.activity.items.find(item => item.id === entryId);
      if (item) {
        state.writes.push({ method, path, body: request.postDataJSON() });
        Object.assign(item, {
          revision: ++state.activity.revision,
          resolvedBy: userId,
          resolvedByName: 'Fixture Dispatcher',
          resolvedAt: '2026-09-14T15:10:00Z',
        });
        state.activity.openItems = [];
        state.activity.openCount = 0;
        value = success(item);
      }
    } else if (method === 'POST' && path === `${base}/documents`) {
      const body = request.postDataJSON();
      state.writes.push({ method, path, body });
      const document = {
        id: body.idempotencyKey,
        kind: body.kind,
        fileName: body.fileName,
        contentType: 'application/pdf',
        length: Buffer.from(body.content, 'base64').length,
        recordedAt: '2026-09-14T15:15:00Z',
        actorName: 'Fixture Dispatcher',
      };
      state.documents.push(document);
      value = success(document);
    }
    if (value !== undefined) return route.fulfill({ json: value });
    report.unexpectedRequests.push({ name, request: description });
    return route.fulfill({
      status: 500,
      json: { success: false, errors: ['Unexpected fixture API'] },
    });
  });
  context.on('page', page => {
    page.on('pageerror', error =>
      report.browserErrors.push({ name, message: error.message }),
    );
    page.on('console', message => {
      if (message.type() === 'error')
        report.browserErrors.push({ name, message: message.text() });
    });
  });
  return { context, state };
}

async function bounds(page) {
  return page.evaluate(() => {
    const root = document.documentElement;
    const scope = document.querySelector('.dispatch-details');
    const visible = [
      ...scope.querySelectorAll('input,select,textarea,button'),
    ].filter(node => node.getClientRects().length > 0);
    return {
      width: root.clientWidth,
      documentWidth: root.scrollWidth,
      theme: root.dataset.theme,
      clippedControls: visible
        .filter(node => {
          const strip = node.closest('.stop-workspace__list');
          if (strip) {
            const tile = node
              .closest('.stop-workspace__stop')
              .getBoundingClientRect();
            const stripBox = strip.getBoundingClientRect();
            return (
              tile.width > strip.clientWidth + 1 ||
              stripBox.left < -1 ||
              stripBox.right > root.clientWidth + 1
            );
          }
          const box = node.getBoundingClientRect();
          return box.left < -1 || box.right > root.clientWidth + 1;
        })
        .map(node => node.id || node.textContent.trim()),
    };
  });
}

async function screenshot(page, name) {
  await page.screenshot({
    path: resolve(output, `${name}.png`),
    fullPage: true,
    animations: 'disabled',
  });
}

async function compareAssignments(page, state, name) {
  const writes = state.writes.length;
  await page.getByRole('button', { name: 'Assignments', exact: true }).click();
  const comparison = page.locator('.assignment-review');
  assert.equal(
    await comparison.getByRole('status').textContent(),
    state.workspace.sourceReviewReason,
  );
  assert.ok(
    await comparison.evaluate(node => node.scrollWidth <= node.clientWidth + 1),
    'Assignment comparison must wrap within the scrolling body',
  );
  const accepted = page.getByRole('region', { name: 'Accepted assignments' });
  const imported = page.getByRole('region', {
    name: 'Imported assignment proposal',
  });
  await accepted.getByText('Accepted truck 11005', { exact: true }).waitFor();
  await imported.getByText('Imported header truck', { exact: false }).waitFor();
  assert.equal(await accepted.getByText('Unmatched source truck').count(), 0);
  assert.equal(
    await imported.locator('.assignment-review__unresolved').count(),
    7,
  );
  assert.equal(
    await page
      .locator('.assignment-review button, .assignment-review input')
      .count(),
    0,
  );
  const measured = await bounds(page);
  assert.ok(measured.documentWidth <= measured.width + 1);
  assert.deepEqual(measured.clippedControls, []);
  await screenshot(page, `${name}-assignments`);
  assert.equal(state.writes.length, writes);
  await page.getByRole('button', { name: 'Overview', exact: true }).click();
}

async function runCase(width, theme, scale) {
  if (width < 1000) return runMobileCase(width, theme, scale);
  const name = `dispatch-workspace-${width}-${theme}-${scale}`;
  const { context, state } = await fixtureContext(name, width, theme, scale);
  const page = await context.newPage();
  try {
    await page.goto(`${origin}/dispatch/${dispatchId}?stopId=${fixtureId(13)}`);
    await page.locator('.stop-workspace__editor').waitFor();
    await page.locator('.dispatch-activity textarea').waitFor();
    await page.locator('.dispatch-documents input:enabled').waitFor();
    assert.deepEqual(
      (await page.locator('.dispatch-details__tab').allTextContents()).map(
        label => label.trim(),
      ),
      ['Broker & billing', 'Overview', 'History', 'Assignments'],
    );
    assert.equal(
      await page
        .locator('.dispatch-details__navigation .dispatch-details__tabs')
        .count(),
      1,
    );
    await compareAssignments(page, state, name);
    const operation = page.locator('.stop-operation--inline');
    assert.equal(await operation.locator('select').count(), 2);
    assert.equal(
      await page
        .getByRole('button', { name: 'Stop type', exact: true })
        .count(),
      0,
    );
    const savedAction = await operation.locator('select').first().inputValue();
    await operation.locator('select').first().selectOption('Driver start');
    assert.equal(await operation.locator('select').count(), 1);
    assert.equal(
      await page
        .getByRole('button', { name: '+ Add stop', exact: true })
        .isDisabled(),
      true,
    );
    await page.getByRole('button', { name: 'Discard', exact: true }).click();
    assert.equal(
      await operation.locator('select').first().inputValue(),
      savedAction,
    );
    assert.equal(
      await operation
        .getByRole('button', { name: 'Save', exact: true })
        .count(),
      0,
    );
    assert.equal(state.writes.length, 0);
    const execution = page.locator(
      '.dispatch-details__resources > .dispatch-details__execution',
    );
    const startingStop = execution.getByRole('button', {
      name: 'Truck starting stop',
      exact: true,
    });
    const manageSwitch = page.getByRole('button', {
      name: 'Manage Switch',
      exact: true,
    });
    assert.equal(await startingStop.count(), 0);
    assert.equal(await manageSwitch.count(), 0);
    const selectedEditor = await page
      .locator('.stop-workspace__editor')
      .elementHandle();
    assert.equal(await page.locator('.truck-assignment').count(), 0);
    assert.equal(await page.locator('.dispatch-switch').count(), 0);
    const sectionReads = [...state.apiRequests];
    for (const section of ['Broker & billing', 'History', 'Overview']) {
      await page.getByRole('button', { name: section, exact: true }).click();
      assert.equal(
        await page
          .locator('.dispatch-details__tab[aria-current="page"]')
          .textContent(),
        section,
      );
    }
    for (const [handle, selector] of [
      [selectedEditor, '.stop-workspace__editor'],
    ]) {
      assert.equal(
        await handle.evaluate((element, selector) => {
          return element === document.querySelector(selector);
        }, selector),
        true,
      );
      await handle.dispose();
    }
    assert.deepEqual(state.apiRequests, sectionReads);
    assert.equal(state.writes.length, 0);
    const editorBeforeJump = await page
      .locator('.stop-workspace__editor')
      .elementHandle();
    await page.getByRole('button', { name: '+ Add stop', exact: true }).click();
    await page
      .getByRole('button', {
        name: 'Drop / Hook trailer',
        exact: true,
      })
      .click();
    await page.waitForFunction(
      () =>
        document.activeElement ===
        document.querySelector('.dispatch-switch h2'),
    );
    await screenshot(page, `${name}-transfer-focus`);
    assert.equal(
      await page
        .getByRole('heading', {
          name: 'Transfer at selected stop',
          exact: true,
        })
        .evaluate(heading => {
          const box = heading.getBoundingClientRect();
          return box.top >= -1 && box.bottom <= innerHeight + 1;
        }),
      true,
      'Adding a transfer focuses its contextual editor',
    );
    assert.equal(
      await editorBeforeJump.evaluate(
        element =>
          element === document.querySelector('.stop-workspace__editor'),
      ),
      true,
    );
    await editorBeforeJump.dispose();
    assert.equal(
      await page
        .locator('.dispatch-details__tab[aria-current="page"]')
        .textContent(),
      'Overview',
    );
    assert.equal(
      await page.locator('.stop-workspace__editor').getAttribute('id'),
      `stop-editor-${fixtureId(13)}`,
    );
    assert.equal(state.apiRequests.length, sectionReads.length + 1);
    assert.equal(
      state.apiRequests.at(-1).path,
      `/api/dispatch/${dispatchId}/execution/switch-workspace`,
    );
    await page
      .locator('.dispatch-switch__editor')
      .getByRole('button', { name: 'Cancel draft', exact: true })
      .click();
    assert.equal(state.writes.length, 0);
    assert.equal(await page.locator('dialog').count(), 0);
    assert.equal(await page.locator('.stop-workspace__stop').count(), 6);
    assert.equal(
      await page.locator('.stop-workspace__fields details').count(),
      0,
    );
    assert.equal(
      await page
        .locator('.stop-workspace__fields > .stop-workspace__column')
        .count(),
      2,
    );
    assert.equal(await page.locator('.stop-hours__road').count(), 3);
    assert.equal(
      await page
        .locator('.stop-workspace__editor .stop-hours__arrival-cycle')
        .count(),
      0,
    );
    assert.equal(
      await page.locator('.stop-workspace__editor').getAttribute('id'),
      `stop-editor-${fixtureId(13)}`,
    );
    const initial = await bounds(page);
    assert.equal(initial.theme, theme);
    assert.ok(initial.documentWidth <= initial.width + 1);
    assert.deepEqual(initial.clippedControls, []);
    await screenshot(page, `${name}-initial`);
    const save = page.getByRole('button', {
      name: 'Save changes',
      exact: true,
    });
    assert.equal(await save.isDisabled(), true);
    assert.equal(await page.locator('.dispatch-details__source').count(), 0);
    assert.equal(await page.locator('.dispatch-details__ownership').count(), 0);
    assert.equal(
      await page
        .getByRole('button', { name: /Copy load link|Link copied/ })
        .count(),
      0,
    );
    assert.equal(await page.locator('.dispatch-details__draft').count(), 0);
    const scrollPanes = await page.evaluate(() => ({
      documentHeight: document.documentElement.scrollHeight,
      height: innerHeight,
      aside: getComputedStyle(
        document.querySelector('.dispatch-details__aside'),
      ).overflowY,
      editor: getComputedStyle(
        document.querySelector('.stop-workspace__editor'),
      ).overflowY,
    }));
    assert.ok(scrollPanes.documentHeight <= scrollPanes.height + 1);
    assert.equal(scrollPanes.aside, 'auto');
    assert.equal(scrollPanes.editor, 'auto');
    await page
      .locator('.stop-workspace__table-heading .stop-workspace__add-button')
      .waitFor({ state: 'visible' });
    assert.equal(await page.locator('.stop-workspace__header').count(), 0);
    if (width >= 1000 && scale === 100) {
      const space = await page.evaluate(() => ({
        table: document
          .querySelector('.stop-workspace__list')
          .getBoundingClientRect().height,
        editor: document
          .querySelector('.stop-workspace__working-area')
          .getBoundingClientRect().height,
        identity: document
          .querySelector('.stop-workspace__identity')
          .getBoundingClientRect().width,
        eta: document
          .querySelector('.stop-workspace__row > .stop-workspace__forecast')
          .getBoundingClientRect().width,
      }));
      assert.ok(
        space.editor > space.table * 1.5,
        'The selected-stop editor gets more working height than the table',
      );
      assert.ok(
        space.identity > space.eta * 2,
        'Stop identity has priority over ETA width',
      );
    }
    const dragHandle = page.locator(
      `[data-stop-id="${fixtureId(15)}"] .stop-workspace__drag`,
    );
    const dragTarget = page.locator(`[data-stop-id="${fixtureId(13)}"]`);
    const dataTransfer = await page.evaluateHandle(() => new DataTransfer());
    await dragHandle.dispatchEvent('dragstart', { dataTransfer });
    await dragTarget.dispatchEvent('dragover', { dataTransfer });
    await dragTarget.locator('.stop-workspace__drop-marker').waitFor();
    assert.match(await dragTarget.getAttribute('class'), /is-drop-before/);
    assert.deepEqual(
      await page
        .locator('.stop-workspace__stop')
        .evaluateAll(rows => rows.map(row => row.dataset.stopId)),
      [10, 11, 12, 13, 14, 15].map(fixtureId),
    );
    await dragHandle.dispatchEvent('dragend', { dataTransfer });
    await dataTransfer.dispose();
    const readsBeforeTransfer = state.apiRequests.length;
    const routePositions = () =>
      page.locator('.stop-workspace__stop').evaluateAll(rows =>
        rows.map(row => {
          const box = row.getBoundingClientRect();
          let scrolled = 0;
          let scrolledX = 0;
          for (
            let parent = row.parentElement;
            parent;
            parent = parent.parentElement
          ) {
            scrolled += parent.scrollTop;
            scrolledX += parent.scrollLeft;
          }
          return [box.x + scrolledX, box.y + scrolled, box.width, box.height];
        }),
      );
    const positionsBefore = await routePositions();
    for (const [id, action] of [
      [11, 'Drop trailer'],
      [12, 'Hook trailer'],
    ]) {
      const transferStop = page.locator(`[data-stop-id="${fixtureId(id)}"]`);
      await transferStop.locator('.stop-workspace__select').click();
      const editor = page.locator(`#stop-editor-${fixtureId(id)}`);
      await editor.locator('.stop-workspace__recorded').waitFor();
      const positionsAfter = await routePositions();
      assert.ok(
        positionsAfter.every((row, index) =>
          row.every(
            (value, axis) =>
              Math.abs(value - positionsBefore[index][axis]) <= 1,
          ),
        ),
        'Stop selection must not shift itinerary rows',
      );
      assert.match(
        await transferStop.locator('.stop-workspace__kind').textContent(),
        new RegExp(action),
      );
      const transfer = editor.locator('.stop-workspace__transfer');
      assert.match(await transfer.textContent(), /Trailer transfer/);
      assert.match(await transfer.textContent(), /Confirmed/);
      assert.match(await transfer.textContent(), /11005/);
      assert.match(await transfer.textContent(), /54777/);
      assert.equal(await transferStop.locator('input, textarea').count(), 0);
      assert.equal(
        await transferStop.locator('.stop-workspace__order').count(),
        0,
      );
      const transferBounds = await bounds(page);
      assert.ok(transferBounds.documentWidth <= transferBounds.width + 1);
      assert.deepEqual(transferBounds.clippedControls, []);
    }
    assert.equal(state.apiRequests.length, readsBeforeTransfer);
    assert.equal(state.writes.length, 0);
    await screenshot(page, `${name}-transfer`);
    await page
      .locator(`[data-stop-id="${fixtureId(13)}"]`)
      .locator('.stop-workspace__select')
      .click();
    for (let count = 0; count < 2; count++) {
      const last = page.locator(`[data-stop-id="${fixtureId(15)}"]`);
      await last
        .getByRole('button', { name: /Reorder stop \d/ })
        .press('ArrowUp');
    }
    assert.deepEqual(
      await page
        .locator('.stop-workspace__stop')
        .evaluateAll(nodes => nodes.map(node => node.dataset.stopId)),
      [10, 11, 12, 15, 13, 14].map(fixtureId),
    );
    assert.equal(state.writes.length, 0);
    assert.equal(await page.locator('.stop-hours__road').count(), 0);
    assert.equal(await startingStop.count(), 0);
    assert.equal(await manageSwitch.count(), 0);
    await page.getByRole('button', { name: '+ Add stop', exact: true }).click();
    assert.equal(
      await page
        .getByRole('button', {
          name: 'Drop / Hook trailer',
          exact: true,
        })
        .isDisabled(),
      true,
    );
    await page
      .locator('.stop-workspace__add')
      .getByRole('button', { name: 'Cancel', exact: true })
      .click();
    const target = page.locator(`[data-stop-id="${fixtureId(13)}"]`);
    const handle = page
      .locator(`[data-stop-id="${fixtureId(15)}"]`)
      .locator('.stop-workspace__drag');
    assert.equal(await handle.getAttribute('draggable'), 'true');
    await handle.click();
    await target.locator('.stop-workspace__select').click();
    assert.deepEqual(
      await page
        .locator('.stop-workspace__stop')
        .evaluateAll(nodes => nodes.map(node => node.dataset.stopId)),
      [10, 11, 12, 13, 15, 14].map(fixtureId),
    );
    await page
      .locator(`#stop-${fixtureId(13)}-facility`)
      .fill('Keystone Distribution Center');
    assert.equal(state.writes.length, 0);
    await save.click();
    await page
      .locator('.dispatch-details__save [role="status"]')
      .filter({ hasText: 'All changes saved' })
      .waitFor();
    assert.equal(state.writes.length, 1);
    assert.equal(state.workspace.stops[3].name, 'Keystone Distribution Center');
    await page.locator(`#stop-${fixtureId(13)}-time`).fill('not a time');
    await save.click();
    await page.locator('.stop-workspace__error').first().waitFor();
    assert.equal(state.writes.length, 1);
    await page.getByRole('button', { name: 'Discard', exact: true }).click();
    assert.equal(
      await page.locator(`#stop-${fixtureId(13)}-time`).inputValue(),
      '09:00 AM',
    );
    const activity = page.locator('.dispatch-activity');
    assert.equal(await activity.locator('h2').textContent(), 'Notes');
    assert.equal(await activity.locator('select, input').count(), 0);
    await activity.locator('textarea').fill('Driver needs the DEL number.');
    await activity
      .getByRole('button', { name: 'Add note', exact: true })
      .click();
    await activity
      .getByText('Driver needs the DEL number.', {
        exact: true,
      })
      .waitFor();
    assert.equal(state.writes.length, 2);
    assert.equal(state.writes[1].path.endsWith('/activity'), true);
    assert.equal(state.writes[1].body.kind, 'note');
    assert.equal(state.writes[1].body.stopId, null);
    assert.equal(state.writes[1].body.driverId, null);
    assert.equal(state.writes[1].body.needsAttention, false);
    await activity
      .getByRole('button', { name: 'Resolve', exact: true })
      .click();
    await activity.locator('.dispatch-activity__resolved').waitFor();
    assert.equal(state.writes.length, 3);
    assert.equal(await save.isDisabled(), true);
    const documents = page.locator('.dispatch-documents');
    await documents.locator('input[type="file"]:enabled').setInputFiles({
      name: 'BOL-fixture.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4\n% Synthetic UI fixture\n%%EOF\n'),
    });
    await documents.locator('.dispatch-documents__list li').waitFor();
    assert.equal(state.writes.length, 4);
    assert.equal(state.documents[0].fileName, 'BOL-fixture.pdf');
    await page.getByRole('button', { name: 'Overview', exact: true }).click();
    await page.locator('#load-instructions').fill('Unsaved navigation fixture');
    await page.getByRole('link', { name: '← Back to Dispatch' }).click();
    await page.getByRole('alertdialog', { name: 'Unsaved changes' }).waitFor();
    await page
      .getByRole('button', { name: 'Keep editing', exact: true })
      .click();
    assert.equal(
      await page.locator('#load-instructions').inputValue(),
      'Unsaved navigation fixture',
    );
    assert.equal(state.writes.length, 4);
    await page.getByRole('button', { name: 'Discard', exact: true }).click();
    const final = await bounds(page);
    assert.ok(final.documentWidth <= final.width + 1);
    assert.deepEqual(final.clippedControls, []);
    await screenshot(page, `${name}-complete`);
    await page
      .locator('.stop-workspace__stop.is-selected')
      .evaluate(node => node.scrollIntoView({ block: 'start' }));
    await page.screenshot({
      path: resolve(output, `${name}-expanded-viewport.png`),
      animations: 'disabled',
    });
    await page
      .locator('.stop-workspace__stop.is-selected ' + '.stop-workspace__select')
      .click();
    assert.equal(await page.locator('.stop-workspace__editor').count(), 1);
    assert.equal(
      await page.locator('.stop-workspace__editor').getAttribute('id'),
      `stop-editor-${fixtureId(13)}`,
    );
    await page.evaluate(() => window.scrollTo(0, 0));
    await screenshot(page, `${name}-retained-editor`);
    await page.screenshot({
      path: resolve(output, `${name}-retained-editor-viewport.png`),
      animations: 'disabled',
    });
    await page
      .locator(`[data-stop-id="${fixtureId(13)}"]`)
      .evaluate(node => node.scrollIntoView({ block: 'start' }));
    await page.screenshot({
      path: resolve(output, `${name}-remaining-route-viewport.png`),
      animations: 'disabled',
    });
    await page
      .getByRole('button', {
        name: 'Broker & billing',
        exact: true,
      })
      .click();
    assert.equal(await page.locator('.dispatch-broker details').count(), 0);
    await page
      .getByLabel('Billing email', { exact: true })
      .fill('billing@example.invalid');
    await page
      .getByLabel('Quick Pay email', { exact: true })
      .fill('quick@example.invalid');
    await page.getByLabel('Payment days', { exact: true }).fill('30');
    await page
      .getByRole('button', { name: 'Add adjustment', exact: true })
      .click();
    await page
      .getByRole('combobox', { name: /^Recipient/ })
      .selectOption('driver');
    await page
      .getByRole('combobox', { name: /^Driver/ })
      .selectOption(fixtureId(91));
    await page.getByLabel('Amount (USD)', { exact: true }).fill('25');
    await page.getByLabel('Reason', { exact: true }).fill('Recorded charge');
    assert.equal(state.writes.length, 4);
    await save.click();
    await page
      .locator('.dispatch-details__save [role="status"]')
      .filter({ hasText: 'All changes saved' })
      .waitFor();
    assert.equal(state.writes.length, 5);
    assert.equal(
      state.workspace.metadata.adjustments[0].driverId,
      fixtureId(91),
    );
    assert.equal(state.workspace.metadata.adjustments[0].amount, 25);
    const billing = await bounds(page);
    assert.ok(billing.documentWidth <= billing.width + 1);
    assert.deepEqual(billing.clippedControls, []);
    await page.evaluate(() => window.scrollTo(0, 0));
    await screenshot(page, `${name}-billing`);
    report.cases.push({
      name,
      initial,
      final,
      billing,
      writes: state.writes.length,
      apiRequests: state.apiRequests,
    });
  } catch (error) {
    report.failures.push({ name, message: error.message });
    console.error(`${name}: ${error.message}`);
    await screenshot(page, `${name}-failure`).catch(() => {});
  } finally {
    await context.close();
    await writeFile(
      resolve(output, 'report.json'),
      JSON.stringify(report, null, 2),
    );
  }
}

async function runMobileCase(width, theme, scale) {
  const name = `dispatch-workspace-${width}-${theme}-${scale}`;
  const { context, state } = await fixtureContext(name, width, theme, scale);
  const page = await context.newPage();
  try {
    await page.goto(`${origin}/dispatch/${dispatchId}?stopId=${fixtureId(13)}`);
    const tabs = page.getByRole('navigation', { name: 'Workspace panels' });
    const panel = async label => {
      await tabs.getByRole('button', { name: label, exact: true }).click();
      assert.equal(
        await tabs
          .getByRole('button', { name: label, exact: true })
          .getAttribute('aria-pressed'),
        'true',
      );
    };
    await tabs.waitFor();
    await page.locator('.stop-workspace__editor').waitFor();
    const editor = await page
      .locator('.stop-workspace__editor')
      .elementHandle();
    await compareAssignments(page, state, name);
    const operation = page.locator('.stop-operation--inline');
    await operation.locator('select').first().selectOption('Driver start');
    assert.equal(
      await operation
        .getByRole('button', { name: 'Save', exact: true })
        .count(),
      0,
    );
    await page.getByRole('button', { name: 'Discard', exact: true }).click();
    await page
      .locator(`#stop-${fixtureId(13)}-facility`)
      .fill('Mobile facility draft');
    await panel('Notes & files');
    const activity = page.locator('.dispatch-activity');
    await activity.locator('textarea').fill('Mobile internal note');
    await panel('Route');
    await page.locator('.stop-workspace__route-support').waitFor();
    await panel('Stops');
    await page.locator('.stop-workspace__list').waitFor();
    assert.equal(
      await page.locator('.stop-workspace__working-area').isVisible(),
      false,
    );
    await page
      .locator(`[data-stop-id="${fixtureId(13)}"] .stop-workspace__select`)
      .click();
    assert.equal(
      await tabs
        .getByRole('button', { name: 'Details', exact: true })
        .getAttribute('aria-pressed'),
      'true',
    );
    assert.equal(
      await page.locator(`#stop-${fixtureId(13)}-facility`).inputValue(),
      'Mobile facility draft',
    );
    assert.equal(
      await editor.evaluate(
        element =>
          element === document.querySelector('.stop-workspace__editor'),
      ),
      true,
    );
    await page.getByRole('button', { name: 'Discard', exact: true }).click();
    await panel('Notes & files');
    assert.equal(
      await activity.locator('textarea').inputValue(),
      'Mobile internal note',
    );
    await activity
      .getByRole('button', { name: 'Add note', exact: true })
      .click();
    await activity.getByText('Mobile internal note', { exact: true }).waitFor();
    const documents = page.locator('.dispatch-documents');
    await documents.locator('input[type=file]:enabled').setInputFiles({
      name: 'mobile-BOL.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4\n% Synthetic UI fixture\n%%EOF\n'),
    });
    await documents.locator('.dispatch-documents__list li').waitFor();
    await screenshot(page, `${name}-notes`);
    await panel('Stops');
    const handle = page.locator(
      `[data-stop-id="${fixtureId(15)}"] .stop-workspace__drag`,
    );
    await handle.click();
    await page
      .locator(`[data-stop-id="${fixtureId(14)}"] .stop-workspace__select`)
      .click();
    assert.deepEqual(
      await page
        .locator('.stop-workspace__stop')
        .evaluateAll(rows => rows.map(row => row.dataset.stopId)),
      [10, 11, 12, 13, 15, 14].map(fixtureId),
    );
    await page.getByRole('button', { name: 'Discard', exact: true }).click();
    await screenshot(page, `${name}-stops`);
    await page
      .locator(`[data-stop-id="${fixtureId(14)}"] .stop-workspace__select`)
      .click();
    await page
      .locator(`#stop-${fixtureId(14)}-facility`)
      .fill('Mobile saved facility');
    await page
      .getByRole('button', { name: 'Save changes', exact: true })
      .click();
    await page
      .locator('.dispatch-details__save [role=status]')
      .filter({ hasText: 'All changes saved' })
      .waitFor();
    assert.equal(
      state.workspace.stops.find(stop => stop.id === fixtureId(14)).name,
      'Mobile saved facility',
    );
    for (const label of ['Stops', 'Details', 'Notes & files', 'Route']) {
      await panel(label);
      const size = await page.evaluate(() => ({
        height: document.documentElement.scrollHeight,
        width: document.documentElement.scrollWidth,
        viewportHeight: innerHeight,
        viewportWidth: innerWidth,
      }));
      assert.ok(
        size.height <= size.viewportHeight + 1,
        'The document does not scroll',
      );
      assert.ok(
        size.width <= size.viewportWidth + 1,
        'The document does not overflow horizontally',
      );
    }
    await panel('Details');
    const facility = page.locator(`#stop-${fixtureId(14)}-facility`);
    await facility.scrollIntoViewIfNeeded();
    const fieldVisible = await facility.evaluate(element => {
      const field = element.getBoundingClientRect();
      const pane = element
        .closest('.stop-workspace__details')
        .getBoundingClientRect();
      return field.top >= pane.top && field.bottom <= pane.bottom;
    });
    assert.ok(
      fieldVisible,
      'The full field fits inside the scrolling detail pane',
    );
    await screenshot(page, `${name}-initial`);
    report.cases.push({ name, writes: state.writes.length, mobilePanels: 4 });
  } catch (error) {
    report.failures.push({ name, message: error.message });
    await screenshot(page, `${name}-failure`);
  } finally {
    await context.close();
  }
}

async function resourceSelectionCase() {
  const name = 'dispatch-resource-selection';
  const { context, state } = await fixtureContext(name, 1440, 'light', 100);
  const stop = state.workspace.stops.find(row => row.id === fixtureId(13));
  Object.assign(stop, {
    canCorrect: true,
    truckId: fixtureId(4),
    trailerId: fixtureId(93),
    driverId: fixtureId(94),
    trailerNumber: '44120',
    driverName: 'Fixture Iurii',
  });
  const lists = {
    trucks: [
      { id: fixtureId(3), unitNumber: '11005' },
      { id: fixtureId(4), unitNumber: '54777' },
    ],
    trailers: [
      { id: fixtureId(92), unitNumber: '9P1175' },
      { id: fixtureId(93), unitNumber: '44120' },
    ],
    drivers: [
      { id: fixtureId(91), name: 'Fixture James' },
      { id: fixtureId(94), name: 'Fixture Iurii' },
    ],
  };
  for (const [kind, items] of Object.entries(lists)) {
    await context.route(`**/api/fleet/${kind}`, route =>
      route.fulfill({
        json: success({
          totalCount: items.length,
          items: items.map(item => ({ ...item, isActive: true })),
        }),
      }),
    );
  }
  const page = await context.newPage();
  try {
    await page.goto(`${origin}/dispatch/${dispatchId}?stopId=${stop.id}`);
    await page.locator('#correction-truck:enabled').waitFor();
    for (const [field, value] of [
      ['truck', stop.truckId],
      ['trailer', stop.trailerId],
      ['driver', stop.driverId],
    ]) {
      assert.equal(
        await page.locator(`#correction-${field}`).inputValue(),
        value,
      );
    }
    const completion = page.locator('#correction-status');
    assert.equal(await completion.getAttribute('aria-pressed'), 'false');
    const inactiveColor = await completion.evaluate(
      element => getComputedStyle(element).backgroundColor,
    );
    await completion.click();
    assert.equal(await completion.getAttribute('aria-pressed'), 'true');
    await page.waitForFunction(
      inactive =>
        getComputedStyle(document.querySelector('#correction-status'))
          .backgroundColor !== inactive,
      inactiveColor,
    );
    assert.notEqual(
      await completion.evaluate(
        element => getComputedStyle(element).backgroundColor,
      ),
      inactiveColor,
    );
    await page.getByRole('button', { name: 'Discard', exact: true }).click();
    assert.equal(await completion.getAttribute('aria-pressed'), 'false');
    assert.equal(state.writes.length, 0);
    report.cases.push({ name, writes: 0 });
  } catch (error) {
    report.failures.push({ name, message: error.message });
  } finally {
    await context.close();
  }
}

async function directoryCase(width, theme) {
  const name = `directories-${width}-${theme}`;
  const { context } = await fixtureContext(name, width, theme, 100);
  const page = await context.newPage();
  let company = {
    id: fixtureId(90),
    revision: 2,
    name: 'Demo Broker Logistics',
    contact: 'Demo contact',
    email: 'contact@example.invalid',
    terms: { billingEmail: 'billing@example.invalid', paymentDays: 30 },
  };
  let writes = 0;
  await context.route('**/api/brokers**', async route => {
    if (route.request().method() === 'PUT') {
      const changed = route.request().postDataJSON();
      assert.equal(changed.revision, company.revision);
      company = { ...changed, revision: changed.revision + 1 };
      writes++;
      return route.fulfill({ json: success(company) });
    }
    return route.fulfill({ json: success([company]) });
  });
  await context.route('**/api/settings/fleet/**', route =>
    route.fulfill({
      json: success({
        totalCount: 1,
        items: [
          {
            id: fixtureId(3),
            name: 'Demo unit',
            vin: 'DEMO',
            isActive: true,
            revision: 1,
          },
        ],
      }),
    }),
  );
  try {
    await page.goto(`${origin}/customers`);
    await page.locator('#company-search').fill('Demo');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await page.getByRole('button', { name: 'Edit company' }).click();
    await page.locator('#company-contact').fill('Updated demo contact');
    await page
      .getByRole('button', { name: 'Save changes', exact: true })
      .click();
    await page.getByText('Company saved.', { exact: true }).waitFor();
    assert.equal(writes, 1);
    await page
      .getByLabel('Billing email', { exact: true })
      .scrollIntoViewIfNeeded();
    await screenshot(page, `${name}-company`);
    if (width < 800)
      await page
        .getByRole('button', { name: 'Open menu', exact: true })
        .click();
    const menu = page.getByRole('navigation', { name: 'Main navigation' });
    await menu.getByRole('link', { name: 'Fleet', exact: true }).click();
    const tabs = page.getByRole('navigation', { name: 'Fleet configuration' });
    await tabs.getByRole('link', { name: 'Trucks', exact: true }).waitFor();
    assert.equal(await tabs.getByRole('link').count(), 3);
    for (const kind of ['trucks', 'trailers', 'drivers']) {
      await tabs.locator(`a[href='/settings/fleet/${kind}']`).click();
      await page
        .getByRole('button', { name: 'Edit Demo unit', exact: true })
        .waitFor();
      assert.ok(
        !(
          (await page
            .locator(".sidebar__nav a[href='/settings']")
            .getAttribute('class')) ?? ''
        )
          .split(' ')
          .includes('active'),
      );
      assert.ok(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth + 1,
        ),
      );
    }
    await screenshot(page, `${name}-drivers`);
    report.cases.push({ name, writes });
  } catch (error) {
    report.failures.push({ name, message: error.message });
    await screenshot(page, `${name}-failure`);
  } finally {
    await context.close();
  }
}

try {
  await directoryCase(1440, 'light');
  await directoryCase(390, 'dark');
  await resourceSelectionCase();
  for (const [width, scale] of [
    [1440, 100],
    [390, 100],
    [390, 200],
    [320, 100],
  ])
    for (const theme of ['light', 'dark']) {
      if (
        process.env.DISPATCH_WORKSPACE_CASE &&
        process.env.DISPATCH_WORKSPACE_CASE !== `${width}-${theme}-${scale}`
      )
        continue;
      await runCase(width, theme, scale);
    }
  assert.deepEqual(report.failures, [], 'Dispatch workspace scenarios failed');
  assert.deepEqual(report.browserErrors, [], 'Unexpected browser errors');
  assert.deepEqual(
    report.unexpectedRequests,
    [],
    'Unexpected requests blocked',
  );
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
  console.log(output);
}
