import { createHash } from 'node:crypto';
import { readFile, readdir } from 'node:fs/promises';
import { relative, resolve } from 'node:path';

export async function inputFingerprint(root, paths) {
  const hash = createHash('sha256');
  for (const path of [...paths].sort()) {
    hash.update(relative(root, path));
    hash.update('\0');
    hash.update(await readFile(path));
    hash.update('\0');
  }
  return hash.digest('hex');
}

export async function filesUnder(root, extensions) {
  const found = [];
  async function visit(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = resolve(directory, entry.name);
      if (entry.isDirectory()) await visit(path);
      else if (extensions.some(extension => entry.name.endsWith(extension)))
        found.push(path);
    }
  }
  await visit(root);
  return found;
}

export async function outputsCurrent(root, expected) {
  try {
    for (const output of expected) {
      const bytes = await readFile(resolve(root, output.path));
      const actual = createHash('sha256').update(bytes).digest('hex');
      if (actual !== output.hash) return false;
    }
    return expected.length > 0;
  } catch (error) {
    if (error?.code === 'ENOENT') return false;
    throw error;
  }
}
