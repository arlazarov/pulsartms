import {build} from 'esbuild';
import {mkdir, writeFile} from 'node:fs/promises';
import {dirname, resolve, relative} from 'node:path';
import {fileURLToPath} from 'node:url';

const client = fileURLToPath(new URL('../', import.meta.url));
const output = resolve(client, 'wwwroot/js/generated');
const result = await build({
  absWorkingDir: client,
  entryPoints: ['Scripts/fleetMap/fleetMap.js', 'Scripts/fleetMap/rendering/gpuScene.js',
    'Scripts/shared/popup.js', 'Scripts/shared/cameraDialog.js', 'Scripts/shared/authStorage.js',
    'Scripts/shared/reorderList.js', 'Scripts/shared/loadDialog.js', 'Scripts/shared/visibility.js', 'Scripts/dispatch/dispatch.js'],
  outbase: 'Scripts', outdir: output, bundle: true, splitting: true, format: 'esm',
  minify: true, chunkNames: 'chunks/[name]-[hash]', write: false
});

await mkdir(output, {recursive: true});
const current = new Set(result.outputFiles.map(file => relative(output, file.path)));
for (const file of result.outputFiles) {
  await mkdir(dirname(file.path), {recursive: true});
  await writeFile(file.path, file.contents);
}
// Open tabs may still import content-hashed chunks from an earlier build.
// Keep them until a clean output directory is created outside a running session.
console.log(`Built ${current.size} JavaScript assets.`);
