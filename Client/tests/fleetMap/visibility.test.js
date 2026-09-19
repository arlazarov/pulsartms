import test from 'node:test';
import assert from 'node:assert/strict';
import {watch} from '../../Scripts/shared/visibility.js';

function fixture(t) {
  const previous = globalThis.document;
  const listeners = new Set();
  globalThis.document = {
    visibilityState: 'visible',
    addEventListener(name, callback) { if (name === 'visibilitychange') listeners.add(callback); },
    removeEventListener(name, callback) { if (name === 'visibilitychange') listeners.delete(callback); }
  };
  t.after(() => { globalThis.document = previous; });
  return {listeners, change(state) { globalThis.document.visibilityState = state; for (const callback of listeners) callback(); }};
}

test('visibility watcher reports the initial state and every change, then stops on dispose', t => {
  const page = fixture(t);
  const calls = [];
  const receiver = {invokeMethodAsync(name, hidden) { calls.push([name, hidden]); return Promise.resolve(); }};
  const watcher = watch(receiver);
  assert.deepEqual(calls, [['OnVisibilityChanged', false]]);
  page.change('hidden');
  page.change('visible');
  assert.deepEqual(calls.slice(1), [['OnVisibilityChanged', true], ['OnVisibilityChanged', false]]);
  watcher.dispose();
  assert.equal(page.listeners.size, 0);
  page.change('hidden');
  assert.equal(calls.length, 3);
});
