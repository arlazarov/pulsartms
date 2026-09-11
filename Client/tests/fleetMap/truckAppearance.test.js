import test from 'node:test';
import assert from 'node:assert/strict';
import {truckIcon} from '../../Scripts/fleetMap/rendering/truckAppearance.js';

test('GPU trucks use the original north-facing arrow in green with its original heading anchor', () => {
  for (const state of ['on', 'idle', 'off']) {
    const icon = truckIcon(state, 45), svg = decodeURIComponent(icon.url);
    assert.equal(icon.anchorX, 56);
    assert.equal(icon.anchorY, 56);
    assert.equal(icon.width, 112);
    assert.equal(icon.height, 120);
    assert.equal(icon.mask, false);
    assert.match(svg, /viewBox="-1 -1 28 30"/);
    assert.match(svg, /d="M13 1 L24 23 Q25 26 22 25 L13 22 L4 25 Q1 26 2 23 Z" fill="#16a34a"/);
    assert.match(svg, /stroke="white" stroke-width="4"/);
    assert.match(svg, /stroke="#1e293b" stroke-width="2"/);
    assert.doesNotMatch(svg, /linearGradient|filter|blur|#trailer|#cab|<circle/);
  }
});

test('stationary trucks use green idle circles or neutral off circles without a center dot', () => {
  for (const state of ['on', 'running', 'idle', 'idling', ' IDLE ']) {
    const svg = decodeURIComponent(truckIcon(state, 0).url);
    assert.match(svg, /<circle cx="13" cy="13" r="11" fill="#16a34a"/);
    assert.doesNotMatch(svg, /<path|r="2/);
  }
  for (const state of ['off', '', 'unknown', 'driving', null, undefined]) {
    const svg = decodeURIComponent(truckIcon(state, 0).url);
    assert.match(svg, /<circle cx="13" cy="13" r="11" fill="#64748b"/);
    assert.doesNotMatch(svg, /<path|r="2/);
  }
});

test('movement follows speed rather than engine-on and only three visual states are cached', () => {
  const moving = truckIcon('on', 1), idle = truckIcon('on', 0), off = truckIcon('off', 0);
  assert.notEqual(moving, idle);
  assert.notEqual(idle, off);
  for (const state of ['off', 'idle', '', 'unknown', null]) assert.equal(truckIcon(state, 40), moving);
  for (const speed of [0, .5, -.5, NaN, Infinity, undefined]) assert.equal(truckIcon('running', speed), idle);
  assert.equal(truckIcon('IDLE', 0), idle);
  assert.equal(truckIcon(undefined, undefined), off);
});
