import test from 'node:test';
import assert from 'node:assert/strict';
import { revealStop } from '../../Scripts/dispatch/dispatch.ts';

function list(top, height) {
  return {
    isConnected: true,
    clientHeight: 200,
    scrollTop: 50,
    getBoundingClientRect: () => ({ top: 100 }),
    children: [
      {
        dataset: { stopId: 'selected' },
        getBoundingClientRect: () => ({ top, bottom: top + height, height }),
      },
    ],
  };
}

test('selected row is revealed with the smallest list-only scroll', () => {
  const clippedAfter = list(260, 80);
  revealStop(clippedAfter, 'selected');
  assert.equal(clippedAfter.scrollTop, 90);
  const clippedBefore = list(60, 80);
  revealStop(clippedBefore, 'selected');
  assert.equal(clippedBefore.scrollTop, 10);
  const visible = list(160, 80);
  revealStop(visible, 'selected');
  assert.equal(visible.scrollTop, 50);
});

test('oversized rows align at their start and stale identities do nothing', () => {
  const large = list(140, 300);
  revealStop(large, 'selected');
  assert.equal(large.scrollTop, 90);
  const stale = list(260, 80);
  revealStop(stale, 'missing');
  assert.equal(stale.scrollTop, 50);
  stale.isConnected = false;
  revealStop(stale, 'selected');
  assert.equal(stale.scrollTop, 50);
});
