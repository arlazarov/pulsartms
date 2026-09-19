import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const config = JSON.parse(readFileSync(new URL('../../../firebase.json', import.meta.url), 'utf8'));
const rules = config.hosting.headers.map(rule => ({
  source: rule.source,
  cache: rule.headers.find(header => header.key === 'Cache-Control')?.value ?? ''
}));

test('every request revalidates by default so navigations and entry points never serve a stale index', () => {
  assert.equal(rules[0].source, '**');
  assert.equal(rules[0].cache, 'no-cache');
});

test('only content-fingerprinted paths are immutable and their rules follow the default so they win', () => {
  const immutable = rules.filter(rule => rule.cache.includes('immutable'));
  assert.deepEqual(immutable.map(rule => rule.source).sort(), ['/_framework/**', '/js/generated/chunks/**']);
  for (const rule of immutable) assert.ok(rules.indexOf(rule) > 0, `${rule.source} must come after the "**" rule`);
  assert.ok(rules.every(rule => rule.cache), 'Each header rule sets Cache-Control');
});

test('JavaScript entry points and stylesheets keep query or content revalidation instead of immutability', () => {
  const build = readFileSync(new URL('../../build/javascript.mjs', import.meta.url), 'utf8');
  assert.match(build, /chunkNames:\s*'chunks\/\[name\]-\[hash\]'/, 'chunks are fingerprinted');
  for (const source of rules.filter(rule => rule.cache.includes('immutable')).map(rule => rule.source)) {
    assert.doesNotMatch(source, /^\/js\/generated\/(?:shared|dispatch|fleetMap)/, 'entry points are not fingerprinted');
    assert.doesNotMatch(source, /css/, 'main.css is versioned by query string only');
  }
});
