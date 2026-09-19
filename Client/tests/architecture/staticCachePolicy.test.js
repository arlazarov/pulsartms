import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const { hosting } = JSON.parse(
  readFileSync(new URL('../../../firebase.json', import.meta.url), 'utf8'),
);
const immutable = hosting.headers.filter(rule =>
  rule.headers.some(
    header =>
      header.key === 'Cache-Control' && header.value.includes('immutable'),
  ),
);
const matches = path =>
  immutable.some(rule => new RegExp(rule.regex).test(path));

test('only content-fingerprinted framework assets and generated chunks receive immutable caching', () => {
  assert.equal(immutable.length, 2);
  for (const path of [
    '/_framework/Client.d14p2ofqx6.wasm',
    '/_framework/dotnet.native.nxw7lo0lh5.wasm.br',
    '/_framework/dotnet.1234567890.js',
    '/js/generated/fleetMap/fleetMap.1234567890.js',
    '/js/generated/chunks/chunk-ISBF6YPE.js',
  ])
    assert.ok(matches(path), path);
  for (const path of [
    '/index.html',
    '/fleet/map',
    '/dispatch',
    '/api/fleet/locations',
    '/api/auth/me',
    '/appsettings.json',
    '/css/main.css',
    '/_framework/blazor.webassembly.js',
    '/_framework/dotnet.js',
    '/js/generated/fleetMap/fleetMap.js',
    '/js/generated/shared/authStorage.js',
    '/brand/pulsr.svg',
  ])
    assert.equal(matches(path), false, path);
  assert.deepEqual(hosting.headers[0], {
    source: '**',
    headers: [{ key: 'Cache-Control', value: 'no-cache' }],
  });
});
