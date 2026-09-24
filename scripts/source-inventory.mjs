// A map of the maintained sources, for a cohesion review to start from.
// It fails nothing and decides nothing: size, partial files and injected
// dependencies are places to look, not verdicts. See
// docs/architecture/cohesion-review.md.
//
//   node scripts/source-inventory.mjs            summary and candidates
//   node scripts/source-inventory.mjs --json     every file, as JSON
//
// Only files git tracks are read. Generated, vendored and built files are
// listed apart with the reason, so what was left out is never silent.
import {execFileSync} from 'node:child_process';
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
const sources = /\.(cs|razor|js|mjs|ts|scss|css|py|sh|sql|cshtml)$/;

// Why a tracked source is not reviewed as maintained code.
const excluded = [
  [/\/Migrations\//, 'generated EF migration'],
  [/\.Designer\.cs$/, 'generated designer'],
  [/(^|\/)wwwroot\/lib\//, 'vendored library'],
  [/\.min\.(js|css)$/, 'minified vendor file'],
  [/\.bundle\.js$/, 'built bundle'],
  [/\.d\.ts$/, 'type declarations'],
  [/(^|\/)(bin|obj|node_modules|artifacts)\//, 'build output'],
];

const area = file =>
  file.startsWith('Server/Domain/') ? 'server domain'
  : file.startsWith('Server/Application/') ? 'server application'
  : file.startsWith('Server/Infrastructure/') ? 'server infrastructure'
  : file.startsWith('Server/API/') ? 'server API'
  : file.startsWith('Server.Tests/') ? 'server tests'
  : file.startsWith('Client.Tests/') ? 'client C# tests'
  : file.startsWith('Client/tests/') ? 'client JS tests'
  : file.startsWith('Client/Styles/') || /\.s?css$/.test(file) ? 'styles'
  : file.startsWith('Client/Scripts/') ? 'browser modules'
  : file.startsWith('Client/') ? 'client C#/Razor'
  : file.startsWith('tools/') ? 'tools'
  : 'scripts and root';

const tracked = execFileSync('git', ['ls-files'], {cwd: root, encoding: 'utf8'})
  .split('\n')
  .filter(file => sources.test(file));
const files = [];
const skipped = new Map();
for (const file of tracked) {
  const reason = excluded.find(([pattern]) => pattern.test(file))?.[1];
  if (reason) {
    skipped.set(reason, (skipped.get(reason) ?? 0) + 1);
    continue;
  }
  const text = readFileSync(path.join(root, file), 'utf8');
  files.push({file, area: area(file), lines: text.split('\n').length, text});
}

// C# types declared as partial across several files, read as one class.
const partials = new Map();
for (const {file, lines, text} of files.filter(x => x.file.endsWith('.cs'))) {
  const space = /^namespace\s+([\w.]+)/m.exec(text)?.[1] ?? '';
  for (const [, name] of text.matchAll(
    /\bpartial\s+(?:class|record|struct)\s+(\w+)/g,
  )) {
    const key = `${space}.${name}`;
    const group = partials.get(key) ?? {type: key, files: [], lines: 0};
    group.files.push({file, lines});
    group.lines += lines;
    partials.set(key, group);
  }
}
const groups = [...partials.values()].filter(x => x.files.length > 1);

// Constructor-injected dependencies of primary constructors: a signal of
// how many owners a class reaches, never a limit.
const injected = [];
for (const {file, text} of files.filter(
  x => x.file.endsWith('.cs') && x.area.startsWith('server'),
)) {
  for (const match of text.matchAll(
    /\bclass\s+(\w+)\s*\(([^)]*)\)/g,
  )) {
    const count = match[2].split(',').filter(x => x.trim()).length;
    if (count) injected.push({type: match[1], file, count});
  }
}

if (process.argv.includes('--json')) {
  console.log(
    JSON.stringify(
      {
        files: files.map(({file, area, lines}) => ({file, area, lines})),
        excluded: Object.fromEntries(skipped),
        partials: groups,
        injected,
      },
      null,
      2,
    ),
  );
} else {
  const byArea = new Map();
  for (const x of files) {
    const a = byArea.get(x.area) ?? {files: 0, lines: 0};
    a.files++;
    a.lines += x.lines;
    byArea.set(x.area, a);
  }
  console.log(`Maintained sources: ${files.length} files`);
  for (const [name, a] of [...byArea].sort((x, y) => y[1].lines - x[1].lines))
    console.log(`  ${name.padEnd(24)} ${String(a.files).padStart(5)} files ${String(a.lines).padStart(7)} lines`);
  console.log('Excluded, by reason:');
  for (const [reason, count] of skipped) console.log(`  ${reason}: ${count}`);
  console.log('\nPartial types across files (largest first):');
  for (const g of groups.sort((x, y) => y.lines - x.lines).slice(0, 40))
    console.log(
      `  ${String(g.lines).padStart(5)}  ${g.files.length} files  ${g.type}  ` +
        `(smallest part ${Math.min(...g.files.map(f => f.lines))} lines)`,
    );
  console.log('\nMost injected dependencies:');
  for (const x of injected.sort((a, b) => b.count - a.count).slice(0, 25))
    console.log(`  ${String(x.count).padStart(3)}  ${x.type}  ${x.file}`);
  console.log('\nLongest files per area:');
  for (const name of byArea.keys()) {
    const top = files
      .filter(x => x.area === name)
      .sort((a, b) => b.lines - a.lines)
      .slice(0, 5);
    console.log(`  ${name}: ${top.map(x => `${x.file} (${x.lines})`).join(', ')}`);
  }
}
