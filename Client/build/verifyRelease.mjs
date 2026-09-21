import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { resolve, relative, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliDecompressSync, gunzipSync } from 'node:zlib';
import { build } from 'esbuild';
import { builtNames as entryPoints } from './sources.mjs';

export function assetPath(root, name) {
  const path = resolve(root, name);
  const local = relative(root, path);
  assert.ok(
    local && !local.startsWith('..') && !isAbsolute(local),
    'Asset must stay inside the staged wwwroot',
  );
  return path;
}

export async function verifyRelease(publish) {
  const root = resolve(publish, 'wwwroot');
  const manifest = JSON.parse(
    await readFile(
      resolve(publish, 'Client.staticwebassets.endpoints.json'),
      'utf8',
    ),
  );
  assert.ok(
    manifest.Endpoints?.length,
    'Published static asset manifest is missing endpoints',
  );
  const checked = new Map();
  const assets = new Set();
  for (const endpoint of manifest.Endpoints) {
    const encoding = endpoint.Selectors?.find(
      item => item.Name === 'Content-Encoding',
    )?.Value;
    const integrity = endpoint.EndpointProperties?.find(
      item => item.Name === 'integrity',
    )?.Value;
    assert.match(
      integrity ?? '',
      /^sha256-/,
      `Missing integrity for ${endpoint.AssetFile}`,
    );
    const key = `${endpoint.AssetFile}:${encoding ?? ''}`;
    assets.add(endpoint.AssetFile);
    if (!checked.has(key)) {
      let bytes = await readFile(assetPath(root, endpoint.AssetFile));
      if (encoding === 'br') bytes = brotliDecompressSync(bytes);
      else if (encoding === 'gzip') bytes = gunzipSync(bytes);
      else assert.ok(!encoding, `Unsupported asset encoding: ${encoding}`);
      checked.set(
        key,
        `sha256-${createHash('sha256').update(bytes).digest('base64')}`,
      );
    }
    assert.equal(
      checked.get(key),
      integrity,
      `Published asset integrity mismatch: ${endpoint.AssetFile}`,
    );
  }

  const index = await readFile(assetPath(root, 'index.html'), 'utf8');
  assert.doesNotMatch(index, /#\[.*?\]/, 'Unresolved Blazor asset placeholder');
  async function checkReference(reference, integrity) {
    if (/^(?:[a-z]+:|\/\/|#)/i.test(reference)) return;
    const name = reference.replace(/^\//, '').split(/[?#]/, 1)[0];
    if (!name) return;
    const bytes = await readFile(assetPath(root, name));
    if (integrity)
      assert.equal(
        `sha256-${createHash('sha256').update(bytes).digest('base64')}`,
        integrity,
        `Bootstrap integrity mismatch: ${name}`,
      );
  }
  for (const [tag] of index.matchAll(/<(?:script|link)\b[^>]*>/g)) {
    const reference = /(?:src|href)="([^"]+)"/.exec(tag)?.[1];
    if (reference)
      await checkReference(reference, /integrity="([^"]+)"/.exec(tag)?.[1]);
  }
  for (const [, json] of index.matchAll(
    /<script\b[^>]*type="importmap"[^>]*>([\s\S]*?)<\/script>/g,
  )) {
    const map = JSON.parse(json);
    for (const reference of Object.values(map.imports ?? {})) {
      await checkReference(reference, map.integrity?.[reference]);
    }
  }
  await build({
    absWorkingDir: root,
    entryPoints: entryPoints.map(name => `js/generated/${name}`),
    bundle: true,
    format: 'esm',
    platform: 'browser',
    outdir: 'unused',
    write: false,
    logLevel: 'silent',
  });
  return { assets: assets.size, entryPoints: entryPoints.length };
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  assert.ok(
    process.argv[2],
    'Usage: node Client/build/verifyRelease.mjs PUBLISH_DIRECTORY',
  );
  const result = await verifyRelease(resolve(process.argv[2]));
  console.log(
    `Verified ${result.assets} published assets and ${result.entryPoints} JavaScript entry-point dependency graphs.`,
  );
}
