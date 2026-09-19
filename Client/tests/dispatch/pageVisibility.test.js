import test from 'node:test';
import assert from 'node:assert/strict';
import { observeVisibility } from '../../Scripts/shared/pageVisibility.js';

test('visibility observer reports the current document and releases its callback', async () => {
  const document = new EventTarget();
  document.hidden = true;
  const calls = [];
  const observer = observeVisibility(
    {
      invokeMethodAsync: async (...args) => {
        calls.push(args);
      },
    },
    document,
  );
  assert.equal(observer.isVisible(), false);
  document.hidden = false;
  document.dispatchEvent(new Event('visibilitychange'));
  assert.deepEqual(calls, [['VisibilityChanged', true]]);
  document.hidden = true;
  document.dispatchEvent(new Event('visibilitychange'));
  assert.deepEqual(calls.at(-1), ['VisibilityChanged', false]);
  observer.dispose();
  document.hidden = false;
  document.dispatchEvent(new Event('visibilitychange'));
  assert.equal(calls.length, 2);
});
