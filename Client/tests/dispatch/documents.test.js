import test from 'node:test';
import assert from 'node:assert/strict';
import { download } from '../../Scripts/dispatch/documents.js';

async function withDownloadDom(run, failure) {
  const original = {
    document: globalThis.document,
    create: URL.createObjectURL,
    revoke: URL.revokeObjectURL,
    timeout: globalThis.setTimeout,
  };
  const calls = { appended: 0, clicked: 0, removed: 0, revoked: [] };
  const timers = [];
  const link = {
    click() {
      calls.clicked++;
      if (failure === 'click') throw new Error('Browser blocked download');
    },
    remove() {
      calls.removed++;
    },
  };
  globalThis.document = {
    createElement: tag => {
      assert.equal(tag, 'a');
      return link;
    },
    body: {
      append: () => {
        calls.appended++;
        if (failure === 'append') throw new Error('Missing body');
      },
    },
  };
  URL.createObjectURL = blob => {
    calls.blob = blob;
    return 'blob:fixture';
  };
  URL.revokeObjectURL = url => calls.revoked.push(url);
  globalThis.setTimeout = callback => timers.push(callback);
  try {
    await run(calls, link, timers);
  } finally {
    globalThis.document = original.document;
    URL.createObjectURL = original.create;
    URL.revokeObjectURL = original.revoke;
    globalThis.setTimeout = original.timeout;
  }
}

test('attachment download releases its temporary URL', async () => {
  await withDownloadDom(async (calls, link, timers) => {
    const bytes = new Uint8Array([1, 2, 3]);
    download('rate.pdf', bytes);
    assert.equal(link.href, 'blob:fixture');
    assert.equal(link.download, 'rate.pdf');
    assert.equal(link.hidden, true);
    assert.equal(calls.blob.type, 'application/octet-stream');
    assert.deepEqual(new Uint8Array(await calls.blob.arrayBuffer()), bytes);
    assert.equal(calls.clicked, 1);
    assert.equal(calls.removed, 1);
    assert.equal(timers.length, 1);
    timers[0]();
    assert.deepEqual(calls.revoked, ['blob:fixture']);
  });
});

for (const failure of ['append', 'click']) {
  test(`failed ${failure} releases the link and URL`, async () => {
    await withDownloadDom((calls, _, timers) => {
      assert.throws(() => download('file.pdf', new Uint8Array([1])));
      assert.equal(calls.removed, 1);
      assert.equal(timers.length, 1);
      timers[0]();
      assert.deepEqual(calls.revoked, ['blob:fixture']);
    }, failure);
  });
}
