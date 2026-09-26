// The Messages reply box as a dispatcher writes in it, in the compiled
// Client: it grows with wrapped and typed lines from one to four, then
// scrolls inside itself; deleting gives the height back; a pasted block of
// lines stops at four; a sent reply returns it to one line; Enter still
// sends and Shift+Enter still starts a line. The API is synthetic and in
// memory: the reply is "sent" to this script, never to WhatsApp.
//
// MAP_TEST_ARTIFACT_DIR=/absolute/publish/wwwroot \
//   node tests/browser/messagesComposerSmoke.mjs
import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'MAP_TEST_ARTIFACT_DIR must identify staged wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('messaging-tabs', process.env.COMPOSER_OUTPUT_DIR);
const origin = 'http://localhost:5079';
const success = response => ({ success: true, response, errors: [] });
const userId = '6a0e5c1e-7d5b-4a61-9d7e-000000000001';
const conversationId = '6a0e5c1e-7d5b-4a61-9d7e-000000000002';
const summary = {
  id: conversationId,
  participant: '+15550000000',
  driverId: null,
  driverName: 'Fixture Driver',
  lastPreview: 'Loaded, heading out.',
  lastMessageAt: new Date(Date.now() - 60_000).toISOString(),
  lastInboundAt: new Date(Date.now() - 60_000).toISOString(),
  windowOpen: true,
  unread: 0,
  claimedBy: null,
  claimedUntil: null,
  revision: 3,
};
const messages = [
  {
    id: '6a0e5c1e-7d5b-4a61-9d7e-000000000010',
    direction: 'in',
    kind: 'text',
    body: 'Loaded, heading out.',
    status: 'received',
    sentAt: new Date(Date.now() - 60_000).toISOString(),
    author: null,
    errorCode: null,
    attachments: [],
  },
];
const sent = [];

function answer(path, method, body) {
  if (path === '/api/auth/me')
    return {
      id: userId,
      name: 'Fixture Dispatcher',
      email: 'fixture@example.invalid',
      isAdmin: true,
    };
  if (path === '/api/settings/appearance') return success({ theme: 'light' });
  if (path === '/api/settings/dispatch')
    return success({ loadNumberPrefix: 'AMF', revision: 1 });
  if (path === '/api/driver-groups')
    return success({ selected: null, groups: [] });
  if (path === '/api/messaging/unread')
    return success({ conversations: 0, more: false, newest: 0 });
  if (path === '/api/messaging/inbox')
    return success({ conversations: [summary], more: false });
  if (path === '/api/messaging/templates') return success([]);
  if (path === '/api/messaging/drivers')
    return success({
      drivers: [],
      configured: true,
      more: false,
      next: null,
    });
  if (path === `/api/messaging/conversations/${conversationId}`)
    return success({
      summary,
      readThrough: 3,
      messages: [...messages].reverse(),
      older: false,
    });
  if (path === `/api/messaging/conversations/${conversationId}/context`)
    return success({
      driverId: null,
      driverName: null,
      state: 'unmatched',
      trucks: [],
      loads: [],
    });
  if (path === `/api/messaging/conversations/${conversationId}/read`)
    return success(true);
  if (path === `/api/messaging/conversations/${conversationId}/claim`)
    return success(true);
  if (
    method === 'POST' &&
    path === `/api/messaging/conversations/${conversationId}/messages`
  ) {
    const request = JSON.parse(body);
    sent.push(request.text);
    const message = {
      id: crypto.randomUUID(),
      direction: 'out',
      kind: 'text',
      body: request.text,
      status: 'sent',
      sentAt: new Date().toISOString(),
      author: 'Fixture Dispatcher',
      errorCode: null,
      attachments: [],
    };
    messages.push(message);
    return success(message);
  }
  return undefined;
}

const report = { artifact, cases: [], errors: [], unexpected: [] };
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.UI_TEST_BROWSER_CHANNEL ?? 'chrome',
});
try {
  // The third case is a browser that cannot size a text box itself: the
  // composer module measures it instead.
  for (const [name, width, height, fallback] of [
    ['1440', 1440, 900, false],
    ['390', 390, 844, false],
    ['1440-measured', 1440, 900, true],
  ]) {
    const context = await browser.newContext({
      viewport: { width, height },
      hasTouch: width < 768,
      locale: 'en-US',
      reducedMotion: 'reduce',
      serviceWorkers: 'block',
    });
    if (fallback)
      await context.addInitScript(() => {
        const supports = CSS.supports.bind(CSS);
        CSS.supports = (...args) =>
          String(args[0]).includes('field-sizing') ? false : supports(...args);
        document.addEventListener('DOMContentLoaded', () => {
          const style = document.createElement('style');
          style.textContent =
            '#messages-text { field-sizing: fixed !important; }';
          document.head.append(style);
        });
      });
    await context.addInitScript(userId => {
      localStorage.setItem(
        'auth_session',
        JSON.stringify({
          Id: userId,
          AccessToken: 'fixture',
          RefreshToken: 'fixture',
        }),
      );
    }, userId);
    await installReleaseArtifact(context, artifact, origin);
    await context.route('**/*', async route => {
      const request = route.request(),
        url = new URL(request.url());
      if (url.origin !== origin) {
        report.unexpected.push(`${request.method()} ${url.href}`);
        return route.abort('blockedbyclient');
      }
      if (url.pathname === '/api/messaging/changes') {
        const mailbox = url.searchParams.get('mailbox');
        if (mailbox) await new Promise(done => setTimeout(done, 5000));
        return route
          .fulfill({
            status: 200,
            json: success({
              mailbox: mailbox ?? '00000000-0000-4000-8000-00000000c4a9',
              resync: !mailbox,
              conversations: [],
            }),
          })
          .catch(() => {});
      }
      if (url.pathname.startsWith('/api/')) {
        const value = answer(
          url.pathname,
          request.method(),
          request.postData(),
        );
        if (value === undefined) {
          report.unexpected.push(`${request.method()} ${url.pathname}`);
          return route.fulfill({ status: 404, json: { success: false } });
        }
        return route.fulfill({ status: 200, json: value });
      }
      return route.fallback();
    });
    const page = await context.newPage();
    page.on('pageerror', error =>
      report.errors.push(`${name}: ${error.message}`),
    );
    await page.goto(`${origin}/messages/${conversationId}`);
    const box = page.locator('#messages-text');
    await box.waitFor({ timeout: 30000 });
    await page.waitForFunction(
      () => !document.querySelector('#messages-text')?.disabled,
    );
    const measure = async step => {
      const state = await box.evaluate(node => {
        const style = getComputedStyle(node);
        const line = parseFloat(style.lineHeight);
        return {
          height: node.getBoundingClientRect().height,
          line,
          lines: Math.round(
            (node.clientHeight -
              parseFloat(style.paddingTop) -
              parseFloat(style.paddingBottom)) /
              line,
          ),
          scrolls: node.scrollHeight > node.clientHeight + 1,
          value: node.value,
        };
      });
      await page.screenshot({
        path: resolve(output, `composer-${name}-${step}.png`),
      });
      return { step, ...state };
    };
    const steps = [];
    const sentBefore = sent.length;
    steps.push(await measure('empty'));
    // Typed lines, one at a time with Shift+Enter.
    await box.click();
    await box.type('First line');
    for (const text of ['second', 'third', 'fourth', 'fifth', 'sixth']) {
      await page.keyboard.press('Shift+Enter');
      await box.type(text);
      steps.push(await measure(`typed-${text}`));
    }
    // Deleting gives the height back, line by line.
    // Four lines and their line breaks: sixth, fifth, fourth, third.
    for (let i = 0; i < 'sixth fifth fourth third '.length; i++)
      await page.keyboard.press('Backspace');
    steps.push(await measure('deleted-to-two'));
    // One long line wraps and grows the box the same way.
    await box.fill('');
    await box.type('A wrapped line '.repeat(width < 768 ? 6 : 20));
    steps.push(await measure('wrapped'));
    // A pasted block of eight lines stops at four and scrolls.
    await box.fill('');
    await box.evaluate(node => {
      const data = new DataTransfer();
      data.setData(
        'text/plain',
        Array.from({ length: 8 }, (_, i) => `Pasted line ${i + 1}`).join('\n'),
      );
      node.focus();
      node.dispatchEvent(
        new ClipboardEvent('paste', { clipboardData: data, bubbles: true }),
      );
    });
    await page.keyboard.insertText(
      Array.from({ length: 8 }, (_, i) => `Pasted line ${i + 1}`).join('\n'),
    );
    steps.push(await measure('pasted-eight'));
    // Enter sends; the box returns to one line and keeps focus.
    await page.keyboard.press('Enter');
    await page.waitForFunction(
      () => document.querySelector('#messages-text')?.value === '',
    );
    await page.waitForTimeout(300);
    steps.push(await measure('after-send'));
    const focused = await box.evaluate(node => document.activeElement === node);
    report.cases.push({
      name,
      steps,
      focused,
      sent: sent.slice(sentBefore),
    });
    await context.close();
  }
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'composer-report.json'),
    JSON.stringify(report, null, 2),
  );
}
const summaryOut = report.cases.map(c => ({
  name: c.name,
  lines: c.steps.map(s => `${s.step}:${s.lines}${s.scrolls ? '+scroll' : ''}`),
  focused: c.focused,
  sent: c.sent.length,
}));
console.log(
  JSON.stringify(
    {
      output,
      cases: summaryOut,
      errors: report.errors,
      unexpected: report.unexpected,
    },
    null,
    2,
  ),
);
for (const c of report.cases) {
  const by = Object.fromEntries(c.steps.map(s => [s.step, s]));
  assert.equal(by.empty.lines, 1, `${c.name}: starts at one line`);
  assert.equal(by['typed-second'].lines, 2, `${c.name}: two lines`);
  assert.equal(by['typed-third'].lines, 3, `${c.name}: three lines`);
  assert.equal(by['typed-fourth'].lines, 4, `${c.name}: four lines`);
  assert.equal(by['typed-fifth'].lines, 4, `${c.name}: capped at four`);
  assert.ok(by['typed-sixth'].scrolls, `${c.name}: scrolls past four`);
  assert.equal(by['deleted-to-two'].lines, 2, `${c.name}: shrinks back`);
  assert.ok(by.wrapped.lines > 1, `${c.name}: a wrapped line grows it`);
  assert.equal(by['pasted-eight'].lines, 4, `${c.name}: paste stops at four`);
  assert.ok(by['pasted-eight'].scrolls, `${c.name}: pasted text scrolls`);
  assert.equal(by['after-send'].lines, 1, `${c.name}: one line after send`);
  assert.equal(c.sent.length, 1, `${c.name}: Enter sent once`);
}
