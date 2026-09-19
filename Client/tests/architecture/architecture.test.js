import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';

const client = new URL('../../', import.meta.url);
const read = path => readFileSync(new URL(path, client), 'utf8');

test('browser sources do not depend on generated output and relative imports resolve', () => {
  const root = new URL('Scripts/', client);
  for (const file of readdirSync(root, { recursive: true }).filter(n =>
    n.endsWith('.js'),
  )) {
    const url = new URL(file, root);
    const source = readFileSync(url, 'utf8');
    assert.doesNotMatch(source, /wwwroot|\.bundle\.js/, file);
    for (const [, target] of source.matchAll(
      /(?:from\s*|import\s*\()\s*['"]([^'"]+)['"]/g,
    )) {
      if (target.startsWith('.'))
        assert.ok(existsSync(new URL(target, url)), `${file}: ${target}`);
      else
        assert.equal(
          file,
          'fleetMap/rendering/gpuScene.js',
          `Vendor import outside GPU adapter: ${file}`,
        );
    }
  }
});

test('every generated module referenced by Client C# or Razor has a JavaScript build entry point', () => {
  const build = read('build/javascript.mjs');
  const entryBlock = build.match(/entryPoints:\s*\[([\s\S]*?)\]/)?.[1];
  assert.ok(entryBlock, 'JavaScript build must declare its entry points');
  const entries = new Set(
    [...entryBlock.matchAll(/['"]Scripts\/([^'"]+\.js)['"]/g)].map(
      match => match[1],
    ),
  );
  let checked = 0;
  for (const folder of [
    'Components',
    'Layout',
    'Pages',
    'Services',
    'Shared',
  ]) {
    const directory = new URL(`${folder}/`, client);
    for (const name of readdirSync(directory, { recursive: true }).filter(
      value => /\.(?:cs|razor)$/.test(value),
    )) {
      const source = read(`${folder}/${name}`);
      for (const [, module] of source.matchAll(
        /["'](?:\.\/)?js\/generated\/([^"'?]+\.js)/g,
      )) {
        assert.ok(
          entries.has(module),
          `${folder}/${name}: ${module} is missing from build entryPoints`,
        );
        assert.ok(
          existsSync(new URL(`Scripts/${module}`, client)),
          `${module} has no source module`,
        );
        checked++;
      }
    }
  }
  assert.ok(
    checked >= 7,
    'The architecture scan must cover all existing interop modules',
  );
});

test('fleet renderer has no legacy fallback imports, DOM stop observation or inline CSS blocks', () => {
  const directories = ['Scripts/fleetMap/'];
  for (const dir of directories) {
    for (const name of readdirSync(new URL(dir, client), {
      recursive: true,
    }).filter(n => n.endsWith('.js'))) {
      const source = read(dir + name);
      assert.doesNotMatch(
        source,
        /(?:truckOverlay|truckLabels|stationPointLayer)\.js/,
        dir + name,
      );
      assert.doesNotMatch(
        source,
        /style\.cssText|new MutationObserver/,
        dir + name,
      );
    }
  }
  assert.doesNotMatch(
    read('Scripts/fleetMap/ui/detailsCard.js'),
    /InfoWindow|dataset|querySelector/,
  );
});

test('shared component logic stays in code-behind', () => {
  for (const path of [
    'Shared/Search/SearchInput/SearchInput',
    'Shared/PageHeader/PageHeader',
    'Shared/DriverStatus/ArrivalEstimate/ArrivalEstimate',
    'Shared/DriverStatus/DriverDutySummary/DriverDutySummary',
    'Shared/RedirectToLogin/RedirectToLogin',
    'Shared/DriverStatus/DriverHours/DriverHours',
  ]) {
    assert.doesNotMatch(read(path + '.razor'), /@code|@inject/);
    assert.ok(existsSync(new URL(path + '.razor.cs', client)));
  }
});
