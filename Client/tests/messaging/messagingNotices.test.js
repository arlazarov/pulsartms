import test from 'node:test';
import assert from 'node:assert/strict';

const source = new URL(
  '../../Scripts/shared/messagingNotices.ts',
  import.meta.url,
);
let loads = 0;
const load = () => import(`${source.href}?load=${++loads}`);

function install({ permission = 'default', answer = 'granted', visible }) {
  const shown = [];
  class Notification {
    static permission = permission;
    static async requestPermission() {
      Notification.permission = answer;
      return answer;
    }
    constructor(title, options) {
      shown.push({ title, ...options });
    }
    close() {}
  }
  const store = new Map();
  globalThis.Notification = Notification;
  globalThis.localStorage = {
    getItem: key => store.get(key) ?? null,
    setItem: (key, value) => store.set(key, value),
  };
  globalThis.document = { visibilityState: visible ? 'visible' : 'hidden' };
  return shown;
}

test('nothing is shown until the dispatcher asks for notices', async () => {
  const shown = install({ visible: false });
  const notices = await load();
  assert.equal(notices.permission(), 'default');
  assert.equal(notices.notify(2, false), false);

  assert.equal(await notices.request(), 'granted');
  assert.equal(notices.enabled(), true);
  assert.equal(notices.notify(2, false), true);
  assert.deepEqual(shown, [
    {
      title: 'New driver message',
      body: '2 conversations with unread messages',
      tag: 'pulsr-messages',
    },
  ]);

  notices.setEnabled(false);
  assert.equal(notices.notify(3, true), false);
  assert.equal(shown.length, 1);
});

test('a tab in front, a refusal or no API shows nothing', async () => {
  const front = install({ permission: 'granted', visible: true });
  let notices = await load();
  notices.setEnabled(true);
  assert.equal(notices.notify(1, false), false);
  assert.deepEqual(front, []);

  install({ answer: 'denied', visible: false });
  notices = await load();
  assert.equal(await notices.request(), 'denied');
  assert.equal(notices.notify(1, false), false);

  delete globalThis.Notification;
  notices = await load();
  assert.equal(notices.permission(), 'unsupported');
  assert.equal(await notices.request(), 'unsupported');
});
