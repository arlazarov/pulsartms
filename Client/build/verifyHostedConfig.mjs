import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// The Client's settings file is ignored by git, so a release built from a
// clean checkout publishes without it and the maps fail in production. A
// hosted artifact must fill every setting the tracked example declares.
// Messages name settings, never values.
function settings(value, path = []) {
  if (value === null || typeof value !== 'object' || Array.isArray(value))
    return [path];
  return Object.entries(value).flatMap(([key, item]) =>
    settings(item, [...path, key]),
  );
}

export async function verifyHostedConfig(wwwroot) {
  const read = async name =>
    JSON.parse(await readFile(resolve(wwwroot, name), 'utf8'));
  const example = await read('appsettings.example.json');
  let hosted;
  try {
    hosted = await read('appsettings.json');
  } catch {
    assert.fail(
      'The artifact has no readable appsettings.json; copy the local Client ' +
        'settings into the release checkout before publishing',
    );
  }
  const required = settings(example);
  for (const path of required) {
    const value = path.reduce((item, key) => item?.[key], hosted);
    assert.ok(
      typeof value === 'string' ? value.trim() !== '' : value != null,
      `Hosted setting ${path.join(':')} is empty or missing`,
    );
  }
  return { settings: required.length };
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  assert.ok(
    process.argv[2],
    'Usage: node Client/build/verifyHostedConfig.mjs WWWROOT',
  );
  const result = await verifyHostedConfig(resolve(process.argv[2]));
  console.log(`Verified ${result.settings} hosted Client settings.`);
}
