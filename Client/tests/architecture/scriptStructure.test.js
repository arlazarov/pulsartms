import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';

const root = new URL('../../Scripts/', import.meta.url);

// The map's own README says what each folder holds; someone opening the
// tree for the first time reads that before any of the modules.
test('the browser sources explain themselves', () => {
  const guide = readFileSync(new URL('README.md', root), 'utf8');
  for (const folder of readdirSync(new URL('fleetMap/', root), {
    withFileTypes: true,
  }).filter(entry => entry.isDirectory()))
    assert.match(guide, new RegExp(`${folder.name}/`), folder.name);
  assert.match(guide, /tsconfig\.json/);
  assert.match(guide, /contracts\.d\.ts/);
});
