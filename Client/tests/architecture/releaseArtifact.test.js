import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { brotliCompressSync, gzipSync } from 'node:zlib';
import { assetPath, verifyRelease } from '../../build/verifyRelease.mjs';
import { installReleaseArtifact } from '../browser/releaseArtifact.mjs';

const hash = bytes =>
  `sha256-${createHash('sha256').update(bytes).digest('base64')}`;

async function fixture(
  callback,
  { missingImport = false, wrongBootstrapHash = false } = {},
) {
  const directory = await mkdtemp(join(tmpdir(), 'pulsartms-artifact-test-'));
  const root = join(directory, 'wwwroot');
  const files = new Map([
    [
      'js/generated/fleetMap/fleetMap.js',
      `export const open = () => import('./${missingImport ? 'missing' : 'later'}.js');`,
    ],
    ['js/generated/fleetMap/later.js', 'export const value = 1;'],
    ...[
      'fleetMap/rendering/gpuScene.js',
      'shared/popup.js',
      'shared/cameraDialog.js',
      'shared/authStorage.ts',
      'shared/reorderList.js',
      'dispatch/dispatch.js',
    ].map(name => [`js/generated/${name}`, 'export const value = 1;']),
    ['_framework/dotnet.hash.js', 'export const boot = true;'],
    ['_framework/Client.hash.wasm', Buffer.from([0, 97, 115, 109])],
    ['css/main.css', 'body { color: black; }'],
  ]);
  const integrity = wrongBootstrapHash
    ? hash('wrong')
    : hash(files.get('_framework/dotnet.hash.js'));
  files.set(
    'index.html',
    `<link rel="stylesheet" href="css/main.css?v=1"><script src="_framework/dotnet.hash.js" integrity="${integrity}"></script>
    <script type="importmap">${JSON.stringify({ imports: { 'dotnet.js': './_framework/dotnet.hash.js' }, integrity: { './_framework/dotnet.hash.js': integrity } })}</script>`,
  );
  const endpoints = [];
  for (const [name, bytes] of files) {
    const path = join(root, name);
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, bytes);
    endpoints.push({
      AssetFile: name,
      Selectors: [],
      EndpointProperties: [{ Name: 'integrity', Value: hash(bytes) }],
    });
  }
  for (const [encoding, suffix, compress] of [
    ['br', '.br', brotliCompressSync],
    ['gzip', '.gz', gzipSync],
  ]) {
    const name = '_framework/Client.hash.wasm';
    const compressed = compress(files.get(name));
    await writeFile(join(root, name + suffix), compressed);
    endpoints.push({
      AssetFile: name + suffix,
      Selectors: [{ Name: 'Content-Encoding', Value: encoding }],
      EndpointProperties: [{ Name: 'integrity', Value: hash(files.get(name)) }],
    });
    endpoints.push({
      AssetFile: name + suffix,
      Selectors: [],
      EndpointProperties: [{ Name: 'integrity', Value: hash(compressed) }],
    });
  }
  await writeFile(
    join(directory, 'Client.staticwebassets.endpoints.json'),
    JSON.stringify({ Endpoints: endpoints }),
  );
  try {
    await callback({ directory, root, endpoints, files });
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

test('release artifact verifies bootstrap, compressed/uncompressed assets and generated dynamic imports', async () => {
  await fixture(async ({ directory, endpoints }) => {
    assert.deepEqual(await verifyRelease(directory), {
      assets: new Set(endpoints.map(item => item.AssetFile)).size,
      entryPoints: 7,
    });
  });
});

test('release artifact rejects corrupted framework bytes and unresolved JS dependencies', async () => {
  await fixture(async ({ directory, root }) => {
    await writeFile(join(root, '_framework/Client.hash.wasm'), 'corrupted');
    await assert.rejects(
      verifyRelease(directory),
      /Published asset integrity mismatch/,
    );
  });
  await fixture(
    async ({ directory }) => {
      await assert.rejects(verifyRelease(directory), /Could not resolve/);
    },
    { missingImport: true },
  );
  await fixture(
    async ({ directory }) => {
      await assert.rejects(
        verifyRelease(directory),
        /Bootstrap integrity mismatch/,
      );
    },
    { wrongBootstrapHash: true },
  );
});

test('release artifact paths cannot escape the staged root', () => {
  assert.throws(() => assetPath('/stage/wwwroot', '../private.json'), /inside/);
  assert.throws(() => assetPath('/stage/wwwroot', '/private.json'), /inside/);
});

test('release artifact rejects unexpanded Blazor bootstrap placeholders', async () => {
  await fixture(async ({ directory, root, endpoints }) => {
    const index =
      '<script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>';
    await writeFile(join(root, 'index.html'), index);
    endpoints.find(
      item => item.AssetFile === 'index.html',
    ).EndpointProperties[0].Value = hash(index);
    await writeFile(
      join(directory, 'Client.staticwebassets.endpoints.json'),
      JSON.stringify({ Endpoints: endpoints }),
    );
    await assert.rejects(
      verifyRelease(directory),
      /Unresolved Blazor asset placeholder/,
    );
  });
});

test('browser release mode serves the staged artifact and forwards only backend requests', async () => {
  await fixture(async ({ root, files }) => {
    let handler;
    await installReleaseArtifact(
      {
        route: async (_match, callback) => {
          handler = callback;
        },
      },
      root,
      'http://localhost:5067',
    );
    async function request(path, navigation = false) {
      let fulfilled,
        continued = false;
      await handler({
        request: () => ({
          url: () => `http://localhost:5067${path}`,
          method: () => 'GET',
          isNavigationRequest: () => navigation,
        }),
        continue: async () => {
          continued = true;
        },
        fulfill: async result => {
          fulfilled = result;
        },
      });
      return { fulfilled, continued };
    }
    assert.equal(
      (await request('/fleet/map', true)).fulfilled.body.toString(),
      files.get('index.html'),
    );
    assert.equal(
      (await request('/_framework/Client.hash.wasm')).fulfilled.contentType,
      'application/wasm',
    );
    assert.equal((await request('/api/fleet')).continued, true);
    assert.equal(
      (await request('/js/generated/missing.js')).fulfilled.status,
      404,
    );
  });
});
