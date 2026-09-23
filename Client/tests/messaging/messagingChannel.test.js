import test from 'node:test';
import assert from 'node:assert/strict';

const source = new URL(
  '../../Scripts/shared/messagingChannel.ts',
  import.meta.url,
);
let tabs = 0;

// Each tab is its own module instance, as in a browser.
const openTab = async () => {
  const module = await import(`${source.href}?tab=${++tabs}`);
  const calls = [];
  const ref = {
    invokeMethodAsync: async (...args) => {
      calls.push(args);
    },
  };
  return {
    calls,
    join: scope => module.join(ref, scope),
    post: signal => module.post(signal),
    leave: () => module.leave(),
    leads: () => calls.filter(([name]) => name === 'Lead').length,
    received: () => calls.filter(([name]) => name === 'Receive'),
    counts: () => calls.filter(([name]) => name === 'Unread'),
  };
};

// One browser: broadcast channels and locks shared by name.
function install({ channels = true, locks = true } = {}) {
  const open = new Set();
  class Channel {
    constructor(name) {
      this.name = name;
      open.add(this);
    }
    postMessage(data) {
      for (const other of open)
        if (other !== this && other.name === this.name && other.onmessage)
          other.onmessage({ data });
    }
    close() {
      open.delete(this);
    }
  }
  const held = new Map();
  const lockManager = {
    request(name, callback) {
      return new Promise(resolve => {
        const grant = () => {
          held.set(name, held.get(name) ?? []);
          Promise.resolve(callback()).then(() => {
            const queue = held.get(name);
            const next = queue.shift();
            if (next) next();
            else held.delete(name);
            resolve();
          });
        };
        if (held.has(name)) held.get(name).push(grant);
        else grant();
      });
    },
  };
  globalThis.BroadcastChannel = channels ? Channel : undefined;
  Object.defineProperty(globalThis, 'navigator', {
    value: locks ? { locks: lockManager } : {},
    configurable: true,
  });
}

const settle = () => new Promise(resolve => setTimeout(resolve, 0));
const id = '11111111-2222-4333-8444-555555555555';
const scope = 'user-1:session-1';

test('one tab per account leads, relays, and hands over on leave', async () => {
  install();
  const first = await openTab();
  const second = await openTab();
  first.join(scope);
  second.join(scope);
  await settle();
  assert.deepEqual([first.leads(), second.leads()], [1, 0]);

  first.post({ kind: 'change', id });
  assert.deepEqual(first.received(), [['Receive', 'change', id]]);
  assert.deepEqual(second.received(), [['Receive', 'change', id]]);

  // Malformed or foreign signals are ignored.
  first.post({ kind: 'change', id: 'not-a-guid' });
  first.post({ kind: 'delete-everything' });
  assert.equal(second.received().length, 1);

  first.leave();
  await settle();
  assert.equal(second.leads(), 1);
  second.post({ kind: 'resync' });
  assert.equal(first.received().length, 1);
  second.leave();
});

test('another account in the same browser has its own leader', async () => {
  install();
  const mine = await openTab();
  const theirs = await openTab();
  mine.join(scope);
  theirs.join('user-2:session-9');
  await settle();
  assert.deepEqual([mine.leads(), theirs.leads()], [1, 1]);

  theirs.post({ kind: 'change', id });
  assert.deepEqual(mine.received(), []);

  // Signing in again rejoins under the new scope and frees the old lock.
  const other = await openTab();
  other.join(scope);
  mine.join('user-1:session-2');
  await settle();
  assert.deepEqual([mine.leads(), other.leads()], [2, 1]);
  for (const tab of [mine, theirs, other]) tab.leave();
});

test('the unread count and read marks reach every tab of the account', async () => {
  install();
  const leader = await openTab();
  const other = await openTab();
  const stranger = await openTab();
  leader.join(scope);
  other.join(scope);
  stranger.join('user-2:session-9');
  await settle();

  leader.post({ kind: 'unread', count: 1, more: false, newest: 7 });
  other.post({ kind: 'read' });
  assert.deepEqual(other.counts(), [['Unread', 1, false, 7]]);
  assert.deepEqual(leader.received(), [['Receive', 'read', null]]);
  assert.deepEqual([stranger.counts(), stranger.received()], [[], []]);

  // A count that is not one is never delivered.
  for (const bad of [
    { kind: 'unread', count: -1, more: false, newest: 7 },
    { kind: 'unread', count: 1.5, more: false, newest: 7 },
    { kind: 'unread', count: 100, more: false, newest: 7 },
    { kind: 'unread', count: 1, more: 'no', newest: 7 },
    { kind: 'unread', count: 1, more: false, newest: -1 },
    { kind: 'unread', count: 1, more: false, newest: 2 ** 60 },
    { kind: 'unread', count: 1, more: false, newest: '7' },
  ])
    leader.post(bad);
  assert.equal(other.counts().length, 1);
  for (const tab of [leader, other, stranger]) tab.leave();
});

// The same .NET reference rejoins after a sign-in change. A lock request
// still queued for the old scope must not lead, or take over the new
// join's release, when it is finally granted.
test('a lock granted to an ended join is given straight back', async () => {
  install();
  const leader = await openTab();
  const follower = await openTab();
  leader.join(scope);
  follower.join(scope);
  await settle();
  assert.deepEqual([leader.leads(), follower.leads()], [1, 0]);

  follower.join('user-1:session-2');
  await settle();
  assert.equal(follower.leads(), 1);
  leader.leave();
  await settle();
  assert.equal(follower.leads(), 1, "the old scope's lock must not lead");

  // The new join's lock is still the one released on leave: another tab
  // of the new scope takes over.
  const next = await openTab();
  next.join('user-1:session-2');
  await settle();
  assert.equal(next.leads(), 0);
  follower.leave();
  await settle();
  assert.equal(next.leads(), 1);
  next.leave();
});

for (const missing of ['channels', 'locks'])
  test(`without ${missing} every tab reads its own stream`, async () => {
    install({ [missing]: false });
    const first = await openTab();
    const second = await openTab();
    first.join(scope);
    second.join(scope);
    await settle();
    assert.deepEqual([first.leads(), second.leads()], [1, 1]);

    first.post({ kind: 'poll' });
    assert.deepEqual(first.received(), [['Receive', 'poll', null]]);
    assert.deepEqual(second.received(), []);
    first.leave();
    second.leave();
  });

test('a malformed scope never shares a leader', async () => {
  install();
  const first = await openTab();
  const second = await openTab();
  first.join('');
  second.join('user 1/../x');
  await settle();
  assert.deepEqual([first.leads(), second.leads()], [1, 1]);
  first.leave();
  second.leave();
});
