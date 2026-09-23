// Two tabs of one browser on the release build, with synthetic fixtures
// only: no real messages, accounts or notification grants. It checks the
// real channel module, Web Locks and BroadcastChannel together: one tab
// reads the stream and the other hears it, closing the leader hands the
// stream to the other tab, signing out stops it, and signing in as
// someone else starts it again under the new account's lock.
import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from 'playwright';
import { browserOutput } from '../../../scripts/artifacts.mjs';
import { installReleaseArtifact } from './releaseArtifact.mjs';

assert.ok(
  process.env.MAP_TEST_ARTIFACT_DIR,
  'Set MAP_TEST_ARTIFACT_DIR to a release wwwroot',
);
const artifact = resolve(process.env.MAP_TEST_ARTIFACT_DIR);
const output = browserOutput('messaging-tabs', process.env.UI_TEST_OUTPUT_DIR);
// Web Locks need a secure context; localhost is one. Every request is
// fulfilled here, so nothing needs to listen on it.
const origin = 'http://localhost:5079';
const conversation = '6b0e5c1e-7d5b-4a61-9d7e-000000000001';
const accounts = {
  'fixture-a': {
    id: '6b0e5c1e-7d5b-4a61-9d7e-00000000000a',
    name: 'Fixture Dispatcher A',
  },
  'fixture-b': {
    id: '6b0e5c1e-7d5b-4a61-9d7e-00000000000b',
    name: 'Fixture Dispatcher B',
  },
};
const success = response => ({ success: true, response, errors: [] });
const report = {
  scope:
    'Release build, two tabs, synthetic API fixtures. No real messages, ' +
    'accounts, providers or notification permission.',
  steps: [],
  streams: [],
  threadReads: [],
  unmocked: [],
  errors: [],
};
const pages = new Map();
const tabOf = request => pages.get(request.frame().page()) ?? 'unknown';
let revision = 3;

const summary = () => ({
  id: conversation,
  participant: '+15550000000',
  driverId: null,
  driverName: 'Fixture Driver',
  lastPreview: 'Loaded.',
  lastMessageAt: new Date().toISOString(),
  lastInboundAt: new Date().toISOString(),
  windowOpen: true,
  unread: 0,
  claimedBy: null,
  claimedUntil: null,
  revision,
});

async function api(route) {
  const request = route.request();
  const url = new URL(request.url());
  const token = (request.headers().authorization ?? '').replace('Bearer ', '');
  const account = accounts[token];
  const json = body => route.fulfill({ status: 200, json: body });
  const path = url.pathname;
  if (path === '/api/auth/login' && request.method() === 'POST')
    return json({
      tokenType: 'Bearer',
      accessToken: 'fixture-b',
      expiresIn: 3600,
      refreshToken: 'fixture-b',
    });
  if (path === '/api/auth/logout')
    return route.fulfill({ status: 204, body: '' });
  if (!account) return route.fulfill({ status: 401, body: '' });
  if (path === '/api/auth/me')
    return json({
      id: account.id,
      name: account.name,
      email: 'fixture@example.invalid',
      isAdmin: false,
    });
  if (path === '/api/settings/appearance')
    return json(success({ theme: 'light' }));
  if (path === '/api/settings/dispatch')
    return json(
      success({ loadNumberPrefix: 'AMF', revision: 1, updatedAt: null }),
    );
  if (path === '/api/messaging/events') {
    report.streams.push({
      tab: tabOf(request),
      account: token,
      at: Date.now(),
    });
    // One change per connection: the leader relays it to the other tab.
    revision++;
    return route.fulfill({
      status: 200,
      contentType: 'text/event-stream',
      body:
        ': ready\n\n' +
        `data: {"conversationId":"${conversation}","revision":${revision}}\n\n`,
    });
  }
  if (path === '/api/messaging/unread')
    return json(success({ conversations: 0, more: false, newest: 0 }));
  if (path === '/api/messaging/templates') return json(success([]));
  if (path === '/api/messaging/inbox')
    return json(success({ conversations: [summary()], more: false }));
  if (path === `/api/messaging/conversations/${conversation}/context`)
    return json(
      success({
        driverId: null,
        driverName: null,
        state: 'unmatched',
        trucks: [],
        loads: [],
      }),
    );
  if (path === `/api/messaging/conversations/${conversation}`) {
    report.threadReads.push({ tab: tabOf(request), at: Date.now() });
    return json(
      success({
        summary: summary(),
        messages: [
          {
            id: '6b0e5c1e-7d5b-4a61-9d7e-000000000010',
            direction: 'in',
            kind: 'text',
            body: 'Loaded.',
            status: 'received',
            sentAt: new Date().toISOString(),
            author: null,
            errorCode: null,
            attachments: [],
          },
        ],
        older: false,
      }),
    );
  }
  if (path.endsWith('/read') || path.endsWith('/claim'))
    return json(success(true));
  report.unmocked.push(`${request.method()} ${path}`);
  return route.fulfill({
    status: 404,
    json: { success: false, errors: ['Not in this fixture'] },
  });
}

const until = async (condition, message, timeout = 15_000) => {
  const start = Date.now();
  while (!(await condition())) {
    assert.ok(Date.now() - start < timeout, message);
    await new Promise(resolve => setTimeout(resolve, 100));
  }
};
const streamsOf = tab => report.streams.filter(x => x.tab === tab);
const locks = page =>
  page.evaluate(async () =>
    (await navigator.locks.query()).held.map(x => x.name),
  );

const browser = await chromium.launch({ headless: true });
try {
  const context = await browser.newContext({
    viewport: { width: 1280, height: 900 },
    serviceWorkers: 'block',
  });
  await context.addInitScript(() => {
    if (!localStorage.getItem('auth_session'))
      localStorage.setItem(
        'auth_session',
        JSON.stringify({
          Id: '6b0e5c1e-7d5b-4a61-9d7e-0000000000a1',
          AccessToken: 'fixture-a',
          RefreshToken: 'fixture-a',
        }),
      );
  });
  await installReleaseArtifact(context, artifact, origin);
  await context.route(
    url => url.origin === origin && url.pathname.startsWith('/api/'),
    api,
  );
  await context.route(
    url => url.origin !== origin,
    route => {
      report.unmocked.push(`external ${route.request().url()}`);
      return route.abort();
    },
  );
  const open = async name => {
    const page = await context.newPage();
    pages.set(page, name);
    page.on('pageerror', error => report.errors.push(`${name}: ${error}`));
    await page.goto(`${origin}/messages/${conversation}`);
    await page.locator('.messages__composer').waitFor();
    return page;
  };

  // One stream for two tabs; the other tab hears it.
  const first = await open('first');
  const second = await open('second');
  await until(
    () => report.streams.length >= 2,
    'The leader reconnects after each fixture stream ends',
  );
  const leaderName = report.streams[0].tab;
  const followerName = leaderName === 'first' ? 'second' : 'first';
  const leader = leaderName === 'first' ? first : second;
  const follower = leader === first ? second : first;
  assert.deepEqual(
    streamsOf(followerName),
    [],
    'Only one tab reads the stream',
  );
  await until(
    () => report.threadReads.filter(x => x.tab === followerName).length >= 2,
    "The other tab reads the conversation again on the leader's signal",
  );
  const leaseA = (await locks(follower)).filter(x =>
    x.startsWith('pulsr-messaging-stream:'),
  );
  assert.equal(leaseA.length, 1, 'One stream lock for the account');
  assert.ok(
    leaseA[0].includes(accounts['fixture-a'].id.replaceAll('-', '')),
    'The lock is scoped to the signed-in account',
  );
  report.steps.push({ step: 'one leader', leader: leaderName, leaseA });

  // Closing the leader hands the stream over.
  await leader.close();
  pages.delete(leader);
  await until(
    () => streamsOf(followerName).length >= 1,
    'The remaining tab takes the stream when the leader closes',
  );
  report.steps.push({ step: 'hand-over', to: followerName });

  // Signing out stops the stream.
  await follower.locator('.sidebar__account').click();
  await follower.getByRole('button', { name: 'Logout', exact: true }).click();
  await follower.waitForURL(/\/login/);
  const afterLogout = report.streams.length;
  await new Promise(resolve => setTimeout(resolve, 5_000));
  assert.equal(
    report.streams.length,
    afterLogout,
    'No stream is read while signed out',
  );
  assert.deepEqual(
    (await locks(follower)).filter(x =>
      x.startsWith('pulsr-messaging-stream:'),
    ),
    [],
    'Signing out gives the lock back',
  );
  report.steps.push({ step: 'signed out', streams: afterLogout });

  // Signing in as someone else starts it under the new account.
  await follower.locator('#email').fill('b@example.invalid');
  await follower.locator('#password').fill('fixture-password');
  await follower.locator('button[type=submit]').click();
  await follower.waitForURL(url => !url.pathname.startsWith('/login'));
  await follower.goto(`${origin}/messages/${conversation}`);
  await follower.locator('.messages__composer').waitFor();
  await until(
    () =>
      report.streams.slice(afterLogout).some(x => x.account === 'fixture-b'),
    'The stream starts again for the new account',
  );
  assert.ok(
    report.streams.slice(afterLogout).every(x => x.account === 'fixture-b'),
    'Nothing is read with the old account after signing in again',
  );
  const leaseB = (await locks(follower)).filter(x =>
    x.startsWith('pulsr-messaging-stream:'),
  );
  assert.equal(leaseB.length, 1);
  assert.ok(leaseB[0].includes(accounts['fixture-b'].id.replaceAll('-', '')));
  assert.ok(!leaseB[0].includes(accounts['fixture-a'].id.replaceAll('-', '')));
  report.steps.push({ step: 'signed in as B', leaseB });
  await context.close();
} catch (error) {
  report.errors.push(error.stack ?? String(error));
} finally {
  await browser.close();
  await writeFile(
    resolve(output, 'report.json'),
    JSON.stringify(report, null, 2),
  );
}
console.log(
  JSON.stringify(
    {
      steps: report.steps.map(x => x.step),
      streams: report.streams.length,
      unmocked: [...new Set(report.unmocked)],
      errors: report.errors,
      output,
    },
    null,
    2,
  ),
);
assert.deepEqual(report.errors, []);
assert.deepEqual([...new Set(report.unmocked)], []);
assert.equal(report.steps.length, 4);
