import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import {
  artifactEnvironment,
  beginRun,
  policy,
  prune,
  selectExpired,
} from '../../../scripts/artifacts.mjs';

const now = 2000000000000;
const old = now - 10 * 86400000;
const row = (id, time, extra = {}) => ({
  id,
  kind: 'release',
  time,
  bytes: 40,
  ...extra,
});

test('artifact policy targets one GiB and keeps two latest runs per kind', () => {
  assert.equal(policy.maxBytes, 1024 ** 3);
  const entries = [
    row('one', old),
    row('two', old + 1),
    row('three', old + 2),
    row('browser', old, { kind: 'browser-ui' }),
  ];
  assert.deepEqual(
    selectExpired(entries, now).remove.map(x => x.id),
    ['one'],
  );
});

test('size pruning is oldest first without evicting protected or recent output', () => {
  const entries = [
    row('old', old),
    row('pin', old + 1, { protected: true }),
    row('new', now - 100),
    row('latest', now),
  ];
  assert.deepEqual(
    selectExpired(entries, now, { ...policy, maxBytes: 1 }).remove.map(
      x => x.id,
    ),
    ['old'],
  );
});

test('an age threshold never overrides active runs or the latest retained results', () => {
  const entries = [
    row('active', old, { protected: true }),
    row('two', old + 1),
    row('three', old + 2),
  ];
  assert.deepEqual(selectExpired(entries, now).remove, []);
});

test('failed attempts cannot evict the last successful result', () => {
  const entries = [
    row('good', old, { success: true }),
    row('failure', old + 1),
    row('failure2', old + 2),
  ];
  assert.deepEqual(
    selectExpired(entries, now, { ...policy, maxBytes: 1 }).remove,
    [],
  );
});

function fixture(t) {
  const directory = fs.mkdtempSync(
    path.join(tmpdir(), 'pulsartms-artifact-test-'),
  );
  const pool = path.join(directory, 'artifacts/managed');
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  return { directory, pool };
}

function age(run, time = old) {
  run.finish();
  const marker = path.join(run.directory, '.pulsartms-artifact.json');
  const data = JSON.parse(fs.readFileSync(marker));
  fs.writeFileSync(
    marker,
    JSON.stringify({ ...data, pid: 0, createdAt: time, completedAt: time }),
  );
}

function requiresLsof(t) {
  if (
    spawnSync('lsof', ['-nP', '-Fn'], {
      encoding: 'utf8',
      maxBuffer: 64 * 1024 ** 2,
    }).status === 0
  )
    return true;
  t.skip(
    'Open-file inventory unavailable; production cleanup also fails closed.',
  );
  return false;
}

test('managed run identity and finish are explicit and idempotent', t => {
  const { pool } = fixture(t);
  const run = beginRun('scratch', { pool });
  const marker = path.join(run.directory, '.pulsartms-artifact.json');
  assert.ok(!fs.existsSync(path.join(run.directory, '.amftms-artifact.json')));
  assert.equal(JSON.parse(fs.readFileSync(marker)).pid, process.pid);
  run.finish();
  run.finish();
  assert.equal(JSON.parse(fs.readFileSync(marker)).pid, 0);
  assert.throws(() => beginRun('../escape', { pool }));
});

test('child commands receive the canonical managed directory and matching legacy alias', () => {
  const environment = {
    PULSARTMS_ARTIFACT_DIR: '/old-canonical',
    AMFTMS_ARTIFACT_DIR: '/old-legacy',
    OTHER: 'kept',
  };
  assert.deepEqual(artifactEnvironment('/current-run', environment), {
    PULSARTMS_ARTIFACT_DIR: '/current-run',
    AMFTMS_ARTIFACT_DIR: '/current-run',
    OTHER: 'kept',
  });
  assert.equal(environment.PULSARTMS_ARTIFACT_DIR, '/old-canonical');
});

test('legacy managed markers remain eligible under the same retention rules', t => {
  if (!requiresLsof(t)) return;
  const { pool } = fixture(t);
  const expired = beginRun('release', { pool });
  age(expired);
  fs.renameSync(
    path.join(expired.directory, '.pulsartms-artifact.json'),
    path.join(expired.directory, '.amftms-artifact.json'),
  );
  for (let i = 1; i <= 2; i++) age(beginRun('release', { pool }), old + i);
  assert.deepEqual(prune({ pool, now, apply: true }).removed, [
    path.basename(expired.directory),
  ]);
  assert.ok(!fs.existsSync(expired.directory));
});

test('an invalid canonical marker cannot fall back to an older valid marker', t => {
  if (!requiresLsof(t)) return;
  const { pool } = fixture(t);
  const run = beginRun('release', { pool });
  age(run);
  fs.copyFileSync(
    path.join(run.directory, '.pulsartms-artifact.json'),
    path.join(run.directory, '.amftms-artifact.json'),
  );
  fs.writeFileSync(
    path.join(run.directory, '.pulsartms-artifact.json'),
    '{invalid',
  );
  for (let i = 1; i <= 2; i++) age(beginRun('release', { pool }), old + i);
  assert.deepEqual(prune({ pool, now, apply: true }).removed, []);
  assert.ok(fs.existsSync(run.directory));
});

test('linked canonical or legacy marker files never authorize deletion', t => {
  if (!requiresLsof(t)) return;
  const { directory, pool } = fixture(t);
  const runs = ['.pulsartms-artifact.json', '.amftms-artifact.json'].map(
    (name, index) => {
      const run = beginRun('release', { pool });
      age(run);
      const metadata = path.join(directory, `marker-${index}.json`);
      fs.renameSync(
        path.join(run.directory, '.pulsartms-artifact.json'),
        metadata,
      );
      fs.symlinkSync(metadata, path.join(run.directory, name));
      return run;
    },
  );
  for (let i = 1; i <= 2; i++) age(beginRun('release', { pool }), old + i);
  assert.deepEqual(prune({ pool, now, apply: true }).removed, []);
  for (const run of runs) assert.ok(fs.existsSync(run.directory));
});

test('cleanup rejects a broad directory and linked managed roots', t => {
  const { directory, pool } = fixture(t);
  assert.throws(() => prune({ pool: directory, apply: true }));
  fs.mkdirSync(path.dirname(pool));
  fs.symlinkSync(directory, pool);
  assert.throws(() => prune({ pool, apply: true }));
});

test('dry run is read-only; apply removes only owned expired trees', t => {
  if (!requiresLsof(t)) return;
  const { pool } = fixture(t);
  const expired = beginRun('release', { pool });
  age(expired);
  for (let i = 1; i <= 2; i++) age(beginRun('release', { pool }), old + i);
  const unknown = path.join(pool, 'unrecognized');
  fs.mkdirSync(unknown);
  fs.writeFileSync(path.join(unknown, 'keep.txt'), 'not registered');
  const plan = prune({ pool, now });
  assert.deepEqual(plan.candidates, [path.basename(expired.directory)]);
  assert.ok(fs.existsSync(expired.directory));
  assert.deepEqual(prune({ pool, now, apply: true }).removed, plan.candidates);
  assert.ok(!fs.existsSync(expired.directory));
  assert.ok(fs.existsSync(unknown));
});

test('keep markers, live owners, open files and linked trees survive pruning', t => {
  if (!requiresLsof(t)) return;
  const { directory, pool } = fixture(t);
  const pinned = beginRun('release', { pool });
  age(pinned);
  fs.writeFileSync(path.join(pinned.directory, '.keep'), '');
  const active = beginRun('release', { pool });
  const opened = beginRun('release', { pool });
  age(opened);
  const fd = fs.openSync(path.join(opened.directory, 'open.log'), 'w');
  t.after(() => fs.closeSync(fd));
  const linked = beginRun('release', { pool });
  age(linked);
  fs.symlinkSync(directory, path.join(linked.directory, 'external'));
  for (let i = 1; i <= 2; i++) age(beginRun('release', { pool }), now + i);
  assert.deepEqual(prune({ pool, now, apply: true }).removed, []);
  for (const run of [pinned, active, opened, linked])
    assert.ok(fs.existsSync(run.directory));
});

test('invalid marker metadata cannot authorize deletion', t => {
  if (!requiresLsof(t)) return;
  const { pool } = fixture(t);
  const run = beginRun('release', { pool });
  age(run);
  fs.writeFileSync(
    path.join(run.directory, '.pulsartms-artifact.json'),
    JSON.stringify({
      version: 1,
      id: 'elsewhere',
      kind: 'release',
      createdAt: old,
    }),
  );
  assert.deepEqual(prune({ pool, now, apply: true }).removed, []);
  assert.ok(fs.existsSync(run.directory));
});

test('all browser artifact producers use managed defaults with explicit override support', () => {
  const browser = new URL('../browser/', import.meta.url);
  const producers = [
    'uiSmoke.mjs',
    'fuelEditorSmoke.mjs',
    'nativeInspectorSmoke.mjs',
    'stopDetailsSmoke.mjs',
    'mapStartupSmoke.mjs',
    'mapMarkersSmoke.mjs',
    'stationPopupSmoke.mjs',
    'stopCardsSmoke.mjs',
    'hoursForecastSmoke.mjs',
    'mapLifecycle.mjs',
  ];
  for (const name of producers) {
    const source = fs.readFileSync(new URL(name, browser), 'utf8');
    assert.match(
      source,
      /browserOutput\(\s*'[a-z-]+',\s*process\.env\.[A-Z_]+,?\s*\)/,
      name,
    );
    assert.doesNotMatch(
      source,
      /writeFile\(`test-results|screenshot\(\{path: 'test-results/,
      name,
    );
  }
});
