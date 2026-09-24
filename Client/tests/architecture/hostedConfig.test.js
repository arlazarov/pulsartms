import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyHostedConfig } from '../../build/verifyHostedConfig.mjs';

const secret = 'hosted-value-that-must-not-be-printed';

async function hosted(settings, callback) {
  const root = await mkdtemp(join(tmpdir(), 'pulsartms-hosted-config-'));
  try {
    await writeFile(
      join(root, 'appsettings.example.json'),
      JSON.stringify({ GoogleMaps: { ApiKey: '' } }),
    );
    if (settings !== undefined)
      await writeFile(join(root, 'appsettings.json'), settings);
    await callback(root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

test('a hosted artifact carries every setting its example declares', async () => {
  await hosted(JSON.stringify({ GoogleMaps: { ApiKey: secret } }), async root =>
    assert.deepEqual(await verifyHostedConfig(root), { settings: 1 }),
  );
});

test('an artifact published from a clean checkout is refused', async () => {
  await hosted(undefined, root =>
    assert.rejects(verifyHostedConfig(root), /no readable appsettings\.json/),
  );
  await hosted('<!doctype html>', root =>
    assert.rejects(verifyHostedConfig(root), /no readable appsettings\.json/),
  );
});

test('an empty or missing setting is refused by name, not value', async () => {
  for (const settings of [
    { GoogleMaps: { ApiKey: '  ' } },
    { GoogleMaps: {} },
    { Other: secret },
  ])
    await hosted(JSON.stringify(settings), root =>
      assert.rejects(verifyHostedConfig(root), error => {
        assert.match(error.message, /GoogleMaps:ApiKey is empty or missing/);
        assert.doesNotMatch(error.message, new RegExp(secret));
        return true;
      }),
    );
});
