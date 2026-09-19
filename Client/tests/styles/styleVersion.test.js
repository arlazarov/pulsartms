import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { stampStyleVersion } from '../../build/styleVersion.mjs';

test('CSS address changes with its contents and remains stable for an unchanged build', () => {
  const source =
    '<link href="css/main.css?v=old" rel="stylesheet"><script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>';
  const first = stampStyleVersion(source, 'body {color: green}');
  assert.match(first, /css\/main\.css\?v=[a-f0-9]{16}/);
  assert.equal(stampStyleVersion(first, 'body {color: green}'), first);
  assert.notEqual(stampStyleVersion(first, 'body {color: orange}'), first);
  assert.ok(
    first.endsWith(
      '<script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>',
    ),
  );
  assert.throws(() => stampStyleVersion('', ''), /one main stylesheet/);
});

test('split-toolchain releases stamp CSS in the Node phase and skip Node during the dotnet phase', () => {
  const project = readFileSync(
    new URL('../../Client.csproj', import.meta.url),
    'utf8',
  );
  const target = project.match(/<Target Name="StampStyleVersion"[^>]*>/)?.[0];
  assert.ok(target);
  assert.ok(target.includes("Condition=\"'$(SkipStyleBuild)' != 'true'\""));
  const scripts = JSON.parse(
    readFileSync(new URL('../../package.json', import.meta.url), 'utf8'),
  ).scripts;
  assert.match(scripts['styles:build'], /node build\/styleVersion\.mjs/);
});
