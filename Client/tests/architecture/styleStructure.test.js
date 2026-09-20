import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const root = new URL('../../Styles/', import.meta.url);
const sheets = readdirSync(root, { recursive: true }).filter(
  x => typeof x === 'string' && x.endsWith('.scss'),
);
const read = file => readFileSync(new URL(file, root), 'utf8');
const folders = new Set(
  sheets.map(f => f.split('/').slice(0, -1).join('/')).filter(Boolean),
);

// The tree is the table of contents: a folder is a thing, a file inside it
// is one part of that thing, and the index says which parts there are and
// in what order they are emitted - because that order is the cascade.
test('every folder names its parts in an index', () => {
  for (const folder of folders) {
    // base/ is the exception: its index is a public API with a deliberate
    // show-list, not a table of contents, and the layer emits no CSS at
    // all - so there is no order to keep.
    if (folder === 'base') continue;
    const index = `${folder}/_index.scss`;
    assert.ok(sheets.includes(index), `${folder}: no _index.scss`);
    const named = [...read(index).matchAll(/@(?:use|forward) '([^']+)'/g)].map(
      ([, name]) => name.replace(/^.*\//, ''),
    );
    for (const file of sheets.filter(
      f => f.startsWith(`${folder}/`) && !f.endsWith('_index.scss'),
    )) {
      const part = file.slice(folder.length + 2, -5);
      if (part.includes('/')) continue; // a folder of its own, named below
      // Either the index emits it, or a sibling in the folder uses it as a
      // helper - what is not allowed is a stylesheet nobody names.
      const usedBySibling = sheets
        .filter(x => x.startsWith(`${folder}/`) && x !== file)
        .some(x => read(x).includes(`'${part}'`));
      assert.ok(
        named.includes(part) ||
          usedBySibling ||
          sheets.some(x => x.startsWith(`${folder}/${part}/`)),
        `${folder}: nothing names ${part}`,
      );
    }
  }
});

// A rule may lean on a name from a layer above it, never the other way
// round. Written as one list so the order is readable.
test('a layer never reaches down into the one below it', () => {
  const order = ['base', 'global', 'layouts', 'components', 'shared', 'pages'];
  for (const file of sheets) {
    const layer = order.indexOf(file.split('/')[0]);
    if (layer < 0) continue;
    for (const [, target] of read(file).matchAll(
      /@(?:use|forward) '((?:\.\.\/)*[a-z][a-z0-9-/]*)'/g,
    )) {
      const resolved = target.startsWith('../')
        ? target.replace(/^(\.\.\/)+/, '')
        : `${file.split('/').slice(0, -1).join('/')}/${target}`;
      const used = order.indexOf(resolved.split('/')[0]);
      if (used < 0) continue;
      assert.ok(
        used <= layer,
        `${file} uses ${target}, which is a layer below it`,
      );
    }
  }
});

// Past about this length a stylesheet is holding two things that want
// separating. Six screens were written as one file each before this.
test('no stylesheet grows back into a screen of its own', () => {
  for (const file of sheets) {
    const lines = read(file).split('\n').length;
    assert.ok(lines <= 280, `${file}: ${lines} lines - split it`);
  }
});

// The map of the tree, in words, beside the tree itself.
test('the tree explains itself to someone who has not seen it', () => {
  const guide = readFileSync(new URL('README.md', root), 'utf8');
  for (const layer of [
    'base/',
    'global/',
    'layouts/',
    'components/',
    'shared/',
    'pages/',
  ])
    assert.match(guide, new RegExp(layer.replace('/', '\\/')), layer);
  for (const token of readdirSync(new URL('base/tokens/', root)).filter(
    f => f !== '_index.scss',
  ))
    assert.match(guide, new RegExp(token), `README does not name ${token}`);
});

// Reusable styles are grouped the way the markup is, so that knowing where
// a component lives tells you where its stylesheet lives.
test('shared groups match the groups the markup uses', () => {
  const markup = new Set(
    readdirSync(new URL('../../Shared/', import.meta.url), {
      withFileTypes: true,
    })
      .filter(entry => entry.isDirectory())
      .map(entry =>
        entry.name.replace(/(?<=[a-z])(?=[A-Z])/g, '-').toLowerCase(),
      ),
  );
  for (const folder of [...folders].filter(f => f.startsWith('shared/'))) {
    const group = folder.split('/')[1];
    assert.ok(
      markup.has(group),
      `shared/${group} names no group under Client/Shared`,
    );
  }
});
