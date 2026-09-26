import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import test from 'node:test';
import {
  filesUnder,
  inputFingerprint,
  outputsCurrent,
} from '../../build/incremental.mjs';

test('the JavaScript build identity notices source deletion', async () => {
  const root = await mkdtemp(resolve(tmpdir(), 'pulsr-js-build-'));
  try {
    const scripts = resolve(root, 'Scripts');
    await mkdir(scripts);
    const first = resolve(scripts, 'first.ts');
    const second = resolve(scripts, 'second.ts');
    await writeFile(first, 'export const first = 1;');
    await writeFile(second, 'export const second = 2;');
    const before = await inputFingerprint(
      root,
      await filesUnder(scripts, ['.ts']),
    );
    await rm(second);
    const after = await inputFingerprint(
      root,
      await filesUnder(scripts, ['.ts']),
    );
    assert.notEqual(after, before);
  } finally {
    await rm(root, { recursive: true });
  }
});

test('a missing or changed output invalidates the JavaScript build', async () => {
  const root = await mkdtemp(resolve(tmpdir(), 'pulsr-js-output-'));
  try {
    const bytes = Buffer.from('built');
    const expected = [
      {
        path: 'entry.js',
        hash: createHash('sha256').update(bytes).digest('hex'),
      },
    ];
    await writeFile(resolve(root, 'entry.js'), bytes);
    assert.equal(await outputsCurrent(root, expected), true);
    await writeFile(resolve(root, 'entry.js'), 'changed');
    assert.equal(await outputsCurrent(root, expected), false);
    await rm(resolve(root, 'entry.js'));
    assert.equal(await outputsCurrent(root, expected), false);
  } finally {
    await rm(root, { recursive: true });
  }
});
