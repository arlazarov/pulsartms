import { build } from 'esbuild';
import { createHash } from 'node:crypto';
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  filesUnder,
  inputFingerprint,
  outputsCurrent,
} from './incremental.mjs';
import { sources } from './sources.mjs';

const client = fileURLToPath(new URL('../', import.meta.url));
const output = resolve(client, 'wwwroot/js/generated');
const stateArgument = process.argv.indexOf('--state');
const statePath = resolve(
  client,
  stateArgument >= 0 && process.argv[stateArgument + 1]
    ? process.argv[stateArgument + 1]
    : 'obj/javascript-build.json',
);
const entryPoints = sources.map(name =>
  existsSync(resolve(client, `Scripts/${name}.ts`))
    ? `Scripts/${name}.ts`
    : `Scripts/${name}.js`,
);
const inputs = [
  ...(await filesUnder(resolve(client, 'Scripts'), ['.js', '.ts'])),
  resolve(client, 'build/javascript.mjs'),
  resolve(client, 'build/incremental.mjs'),
  resolve(client, 'build/sources.mjs'),
  resolve(client, 'package.json'),
  resolve(client, 'package-lock.json'),
  resolve(client, 'tsconfig.json'),
];
const fingerprint = await inputFingerprint(client, inputs);
try {
  const saved = JSON.parse(await readFile(statePath, 'utf8'));
  if (
    saved.fingerprint === fingerprint &&
    (await outputsCurrent(output, saved.outputs ?? []))
  ) {
    console.log(`JavaScript assets unchanged (${saved.outputs.length} files).`);
    process.exit(0);
  }
} catch (error) {
  if (error?.code !== 'ENOENT' && !(error instanceof SyntaxError)) throw error;
}
const result = await build({
  absWorkingDir: client,
  entryPoints,
  outbase: 'Scripts',
  outdir: output,
  bundle: true,
  splitting: true,
  format: 'esm',
  minify: true,
  chunkNames: 'chunks/[name]-[hash]',
  write: false,
});

await mkdir(output, { recursive: true });
const current = new Set(
  result.outputFiles.map(file => relative(output, file.path)),
);
for (const file of result.outputFiles) {
  await mkdir(dirname(file.path), { recursive: true });
  await writeFile(file.path, file.contents);
}
const state = {
  fingerprint,
  outputs: result.outputFiles.map(file => ({
    path: relative(output, file.path),
    hash: createHash('sha256').update(file.contents).digest('hex'),
  })),
};
await mkdir(dirname(statePath), { recursive: true });
const pendingState = `${statePath}.${process.pid}.tmp`;
await writeFile(pendingState, `${JSON.stringify(state)}\n`);
await rename(pendingState, statePath);
// Open tabs may still import content-hashed chunks from an earlier build.
// Keep them until a clean output directory is created outside a running session.
console.log(`Built ${current.size} JavaScript assets.`);
