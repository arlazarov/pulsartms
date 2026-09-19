import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn, spawnSync} from 'node:child_process';

export const policy = {maxBytes: 1024 ** 3, maxAgeMs: 7 * 86400000, keepPerKind: 2, graceMs: 3600000};
const root = fileURLToPath(new URL('../', import.meta.url));
const defaultPool = path.join(root, 'artifacts/managed');
const marker = '.pulsartms-artifact.json';
const legacyMarker = '.amftms-artifact.json';
const kinds = new Set(['release', 'scratch', 'diagnostic', 'browser-ui', 'browser-fuel-editor', 'browser-route-editor',
  'browser-native-inspector', 'browser-stop-details', 'browser-map-startup', 'browser-map-markers',
  'browser-station-popup', 'browser-stop-cards', 'browser-hours-forecast', 'browser-map-lifecycle']);
const validKind = value => kinds.has(value);

function preparePool(pool) {
  pool = path.resolve(pool);
  if (path.basename(pool) !== 'managed' || path.basename(path.dirname(pool)) !== 'artifacts') throw new Error('Invalid managed pool');
  for (const directory of [path.dirname(pool), pool]) {
    if (fs.existsSync(directory) && fs.lstatSync(directory).isSymbolicLink()) throw new Error('Managed pool cannot be a symlink');
    fs.mkdirSync(directory, {recursive: true});
  }
  return fs.realpathSync(pool);
}

function alive(pid) {
  if (!Number.isSafeInteger(pid) || pid <= 0) return false;
  try { process.kill(pid, 0); return true; } catch (error) { return error.code !== 'ESRCH'; }
}

function treeBytes(directory) {
  let bytes = 0;
  for (const entry of fs.readdirSync(directory, {withFileTypes: true})) {
    const file = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error('Symlink in managed output');
    bytes += entry.isDirectory() ? treeBytes(file) : fs.lstatSync(file).size;
  }
  return bytes;
}

function readMarker(directory) {
  for (const name of [marker, legacyMarker]) {
    const file = path.join(directory, name);
    try {
      if (fs.lstatSync(file).isSymbolicLink()) throw new Error('Linked artifact marker');
    } catch (error) {
      if (error.code === 'ENOENT') continue;
      throw error;
    }
    return {name, saved: JSON.parse(fs.readFileSync(file, 'utf8'))};
  }
  throw new Error('Artifact marker not found');
}

export function selectExpired(entries, now = Date.now(), limits = policy) {
  const newest = [...entries].sort((a,b) => b.time - a.time);
  const counts = new Map(), protectedIds = new Set(), successfulKinds = new Set();
  for (const item of newest) {
    const count = (counts.get(item.kind) ?? 0) + 1;
    counts.set(item.kind, count);
    if (count <= limits.keepPerKind || item.protected || now - item.time < limits.graceMs) protectedIds.add(item.id);
    if (item.success && !successfulKinds.has(item.kind)) { protectedIds.add(item.id); successfulKinds.add(item.kind); }
  }
  let bytes = entries.reduce((sum, item) => sum + item.bytes, 0);
  const remove = [];
  for (const item of [...newest].reverse()) {
    if (protectedIds.has(item.id)) continue;
    if (now - item.time > limits.maxAgeMs || bytes > limits.maxBytes) { remove.push(item); bytes -= item.bytes; }
  }
  return {remove, remainingBytes: bytes};
}

export function prune({pool = defaultPool, apply = false, now = Date.now(), limits = policy} = {}) {
  pool = preparePool(pool);
  const snapshot = spawnSync('lsof', ['-nP', '-Fn'], {encoding: 'utf8', maxBuffer: 64 * 1024 ** 2, timeout: 10000});
  if (snapshot.status !== 0) return {removed: [], skipped: 'Open-file inventory unavailable; nothing deleted.'};
  const opened = snapshot.stdout.split('\n').filter(x => x.startsWith('n')).map(x => x.slice(1));
  const inUse = directory => opened.some(x => x === directory || x.startsWith(directory + '/'));
  const entries = [];
  for (const entry of fs.readdirSync(pool, {withFileTypes: true})) {
    if (!entry.isDirectory() || entry.isSymbolicLink()) continue;
    const directory = path.join(pool, entry.name);
    try {
      const {name, saved} = readMarker(directory);
      if (saved.version !== 1 || saved.id !== entry.name || !validKind(saved.kind)
        || !Number.isFinite(saved.createdAt) || (saved.completedAt != null && !Number.isFinite(saved.completedAt))) continue;
      entries.push({id: entry.name, marker: name, kind: saved.kind, time: saved.completedAt ?? saved.createdAt, success: saved.success === true,
        bytes: treeBytes(directory), protected: fs.existsSync(path.join(directory, '.keep')) || alive(saved.pid) || inUse(directory)});
    } catch { /* Unrecognized, changing or linked trees are never cleanup targets. */ }
  }
  const plan = selectExpired(entries, now, limits);
  const removed = [];
  if (apply) for (const item of plan.remove) {
    const directory = path.join(pool, item.id);
    try {
      if (fs.lstatSync(directory).isSymbolicLink() || fs.existsSync(path.join(directory, '.keep'))) continue;
      const {name, saved} = readMarker(directory);
      if (name !== item.marker || saved.version !== 1 || saved.id !== item.id || saved.kind !== item.kind
        || (saved.completedAt ?? saved.createdAt) !== item.time || alive(saved.pid) || inUse(directory)) continue;
      treeBytes(directory);
      fs.rmSync(directory, {recursive: true});
      removed.push(item.id);
    } catch { /* A concurrent run or changed output wins over housekeeping. */ }
  }
  return {removed, candidates: plan.remove.map(x => x.id), remainingBytes: apply
    ? entries.filter(x => !removed.includes(x.id)).reduce((sum,x) => sum + x.bytes, 0) : plan.remainingBytes};
}

function autoPrune() {
  try {
    const result = prune({apply: true});
    if (result.removed.length) console.error(`[artifacts] Removed ${result.removed.length} expired generated runs.`);
    if (result.skipped) console.error(`[artifacts] ${result.skipped}`);
    if (result.remainingBytes > policy.maxBytes) console.error('[artifacts] Retained outputs exceed 1 GiB; protected/recent runs were kept.');
  } catch { console.error('[artifacts] Cleanup unavailable; existing files were retained.'); }
}

export function beginRun(kind, {pool = defaultPool} = {}) {
  if (!validKind(kind)) throw new Error('Invalid artifact kind');
  pool = preparePool(pool);
  const directory = fs.mkdtempSync(path.join(pool, kind + '-'));
  const saved = {version: 1, id: path.basename(directory), kind, createdAt: Date.now(), pid: process.pid};
  fs.writeFileSync(path.join(directory, marker), JSON.stringify(saved));
  let finished = false;
  return {directory, finish(success = false) {
    if (finished) return;
    finished = true;
    fs.writeFileSync(path.join(directory, marker), JSON.stringify({...saved, pid: 0, completedAt: Date.now(), success}));
  }};
}

export function browserOutput(kind, override) {
  if (override) return path.resolve(override);
  autoPrune();
  const run = beginRun('browser-' + kind);
  process.once('exit', code => { run.finish(code === 0); autoPrune(); });
  console.log(`[artifacts] ${run.directory}`);
  return run.directory;
}

export function artifactEnvironment(directory, environment = process.env) {
  return {...environment, PULSARTMS_ARTIFACT_DIR: directory, AMFTMS_ARTIFACT_DIR: directory};
}

async function main(args) {
  if (args[0] === 'prune' && args.slice(1).every(x => x === '--apply')) {
    console.log(JSON.stringify(prune({apply: args.includes('--apply')}), null, 2));
    return;
  }
  if (args[0] !== 'run' || !validKind(args[1]) || args[2] !== '--' || !args[3]) throw new Error('Usage: node scripts/artifacts.mjs prune [--apply] | run KIND -- COMMAND [ARGS using {artifacts}]');
  autoPrune();
  const run = beginRun(args[1]);
  console.error(`[artifacts] ${run.directory}`);
  let success = false;
  try {
    const [command, ...commandArgs] = args.slice(3).map(x => x.replaceAll('{artifacts}', run.directory));
    const child = spawn(command, commandArgs, {stdio: 'inherit', env: artifactEnvironment(run.directory)});
    const forward = signal => child.kill(signal);
    const onInt = () => forward('SIGINT'), onTerm = () => forward('SIGTERM');
    process.on('SIGINT', onInt); process.on('SIGTERM', onTerm);
    try {
      process.exitCode = await new Promise((resolve, reject) => {
        child.once('error', reject); child.once('exit', (code, signal) => resolve(code ?? (signal === 'SIGINT' ? 130 : 143)));
      });
      success = process.exitCode === 0;
    } finally { process.off('SIGINT', onInt); process.off('SIGTERM', onTerm); }
  } finally { run.finish(success); autoPrune(); }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).catch(error => { console.error(error.message); process.exitCode = 1; });
}
