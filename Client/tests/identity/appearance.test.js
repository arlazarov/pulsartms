import test from 'node:test';
import assert from 'node:assert/strict';
import { applyTheme } from '../../Scripts/shared/appearance.ts';

test('appearance applies only supported root themes without browser storage', t => {
  const original = Object.getOwnPropertyDescriptor(globalThis, 'document');
  Object.defineProperty(globalThis, 'document', {
    configurable: true,
    value: {
      documentElement: { dataset: {} },
    },
  });
  t.after(() => {
    if (original) Object.defineProperty(globalThis, 'document', original);
    else delete globalThis.document;
  });
  applyTheme('dark');
  assert.equal(document.documentElement.dataset.theme, 'dark');
  applyTheme('light');
  assert.equal(document.documentElement.dataset.theme, 'light');
  applyTheme('unexpected');
  assert.equal(document.documentElement.dataset.theme, 'light');
});
