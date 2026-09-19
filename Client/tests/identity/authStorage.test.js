import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import * as storage from '../../Scripts/shared/authStorage.js';

const session = (id = randomUUID(), label = 'first') => ({
  Id: id,
  AccessToken: `access-${label}`,
  RefreshToken: `refresh-${label}`,
});

async function environment(run) {
  const original = Object.fromEntries(
    ['localStorage', 'navigator'].map(name => [
      name,
      Object.getOwnPropertyDescriptor(globalThis, name),
    ]),
  );
  const values = new Map();
  let tail = Promise.resolve();
  const names = [];
  const locks = {
    request(name, callback) {
      names.push(name);
      const next = tail.then(() => callback({ name }));
      tail = next.catch(() => {});
      return next;
    },
  };
  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    value: {
      getItem: key => values.get(key) ?? null,
      setItem: (key, value) => values.set(key, value),
      removeItem: key => values.delete(key),
    },
  });
  Object.defineProperty(globalThis, 'navigator', {
    configurable: true,
    value: { locks },
  });
  try {
    await run({ values, locks, names });
  } finally {
    for (const [name, descriptor] of Object.entries(original)) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  }
}

test('legacy token pairs migrate once under the same cross-tab lock as new login', async () => {
  await environment(async ({ values, names }) => {
    values.set('access_token', 'legacy-access');
    values.set('refresh_token', 'legacy-refresh');
    const first = JSON.parse(await storage.readSession());
    const second = JSON.parse(await storage.readSession());
    assert.equal(first.Id, second.Id);
    assert.equal(first.AccessToken, 'legacy-access');
    assert.equal(values.has('access_token'), false);
    assert.equal(values.has('refresh_token'), false);
    const next = session();
    await Promise.all([
      storage.readSession(),
      storage.setSession(JSON.stringify(next)),
    ]);
    assert.deepEqual(JSON.parse(await storage.readSession()), next);
    assert.ok(names.every(name => name === 'amftms:auth-session'));
  });
});

test('a queued stale refresh from another tab cannot overwrite or clear a newer login', async () => {
  await environment(async ({ locks }) => {
    const first = session();
    const next = session(undefined, 'second');
    await storage.setSession(JSON.stringify(first));
    let release;
    const held = locks.request(
      'amftms:auth-session',
      () =>
        new Promise(resolve => {
          release = resolve;
        }),
    );
    await Promise.resolve();
    const login = storage.setSession(JSON.stringify(next));
    const replaced = storage.replaceSession(
      JSON.stringify(first),
      JSON.stringify(session(first.Id, 'rotated')),
    );
    const cleared = storage.clearSession(first.Id);
    release();
    await held;
    await login;
    assert.equal(await replaced, false);
    assert.equal(await cleared, false);
    assert.deepEqual(JSON.parse(await storage.readSession()), next);
  });
});

test('refresh comparison uses both tokens while logout follows the same login identity', async () => {
  await environment(async () => {
    const first = session();
    const rotated = session(first.Id, 'rotated');
    await storage.setSession(JSON.stringify(first));
    assert.equal(
      await storage.replaceSession(
        JSON.stringify(first),
        JSON.stringify(rotated),
      ),
      true,
    );
    assert.equal(
      await storage.replaceSession(JSON.stringify(first), null),
      false,
    );
    assert.equal(await storage.clearSession(first.Id), true);
    assert.equal(await storage.readSession(), null);
    assert.throws(
      () =>
        storage.replaceSession(
          JSON.stringify(first),
          JSON.stringify(session()),
        ),
      /cannot change/,
    );
  });
});

test('session identifiers have a canonical form shared with the C# envelope', async () => {
  await environment(async ({ values }) => {
    const original = session();
    values.set(
      'auth_session',
      JSON.stringify({ ...original, Id: original.Id.toUpperCase() }),
    );
    assert.deepEqual(JSON.parse(await storage.readSession()), original);
    assert.equal(await storage.clearSession(original.Id), true);
  });
});

test('malformed envelopes fail closed without resurrecting legacy tokens', async () => {
  await environment(async ({ values }) => {
    for (const json of [
      '{',
      '{}',
      'null',
      '[]',
      JSON.stringify({ Id: 'invalid', AccessToken: 'a', RefreshToken: 'r' }),
      JSON.stringify({ ...session(), AccessToken: '' }),
      JSON.stringify({ ...session(), RefreshToken: null }),
    ]) {
      values.set('auth_session', json);
      values.set('access_token', 'stale-access');
      values.set('refresh_token', 'stale-refresh');
      assert.equal(await storage.readSession(), null);
    }
  });
});

test('a browser without Web Locks cannot perform unsafe storage mutations or reads', async () => {
  await environment(async ({ values }) => {
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: {},
    });
    const current = session();
    values.set('auth_session', JSON.stringify(current));
    assert.throws(() => storage.readSession(), /requires Web Locks/);
    assert.throws(
      () => storage.setSession(JSON.stringify(session())),
      /requires Web Locks/,
    );
    assert.throws(() => storage.clear(), /requires Web Locks/);
    assert.deepEqual(JSON.parse(values.get('auth_session')), current);
  });
});
