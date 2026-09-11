import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync, readdirSync} from 'node:fs';

const client = new URL('../../', import.meta.url);
const read = path => readFileSync(new URL(path, client), 'utf8');
const sources = folder => readdirSync(new URL(folder + '/', client), {recursive: true})
  .filter(file => /\.(?:cs|razor)$/.test(file)).map(file => folder + '/' + file);

test('shared UI, DTOs and services do not depend on page namespaces', () => {
  for (const folder of ['Shared', 'Components', 'Models', 'Services'])
    for (const file of sources(folder)) assert.doesNotMatch(read(file), /\bClient\.Pages\b/, file);
});

test('shared component namespaces follow their owning folders', () => {
  for (const file of sources('Shared').filter(file => file.endsWith('.cs'))) {
    const expected = 'Client.' + file.slice(0, file.lastIndexOf('/')).replaceAll('/', '.');
    assert.equal(read(file).match(/\bnamespace\s+([\w.]+)\s*;/)?.[1], expected, file);
  }
});

test('shared Razor and code-behind pairs live in their component folder', () => {
  const files = sources('Shared');
  for (const file of files.filter(file => file.endsWith('.razor.cs'))) {
    const parts = file.split('/');
    const component = parts.at(-1).replace('.razor.cs', '');
    assert.equal(parts.at(-2), component, file);
    assert.ok(files.includes(file.slice(0, -3)), `${file} must have its Razor sibling`);
  }
});

test('DTOs do not depend on UI component namespaces', () => {
  for (const file of sources('Models'))
    assert.doesNotMatch(read(file), /\bClient\.(?:Shared|Components|Layout)\b/, file);
});

test('page-local components are not consumed by another feature or shared UI', () => {
  const pages = sources('Pages').filter(file => file.endsWith('.razor'));
  const consumers = [...pages, ...sources('Shared').filter(file => file.endsWith('.razor'))];
  for (const file of pages) {
    const feature = file.split('/')[1];
    const component = file.split('/').at(-1).replace('.razor', '');
    const tag = new RegExp('<(?:Client\\.Pages\\.' + feature + '\\.)?' + component + '(?=[\\s/>])');
    for (const consumer of consumers.filter(path => !path.startsWith('Pages/' + feature + '/')))
      assert.doesNotMatch(read(consumer), tag, `${consumer} must not consume page-local ${file}`);
  }
});
