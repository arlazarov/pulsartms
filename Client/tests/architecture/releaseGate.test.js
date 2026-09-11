import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync, readFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';

const root = fileURLToPath(new URL('../../../', import.meta.url));
const repository = 'us-east4-docker.pkg.dev/amftms/amftms/api';
const digest = `sha256:${'a'.repeat(64)}`;
const stubs = `
record() { printf '%s\\n' "$*" >> "$TEST_LOG"; [[ "$*" != "\${TEST_FAIL:-}" ]]; }
npm() { record npm "$@"; }
dotnet() { record dotnet "$@"; }
node() { record node "$@" && [[ "$TEST_ARTIFACT_FAIL" != 1 ]]; }
firebase() { record firebase "$@"; }
mktemp() { printf '%s\\n' "$TEST_RELEASE_DIR"; }
gcloud() {
  record gcloud "$@" || return 1
  if [[ "$1 $2" == 'builds submit' ]]; then printf '%s\\n' "\${TEST_BUILD_ID:-build-123}";
  elif [[ "$1 $2" == 'builds describe' ]]; then printf '%s\\n' "$TEST_IMAGE_RESULT"; fi
}
export -f record npm dotnet node firebase mktemp gcloud
`;

function run(script, {phase, fail, imageResult, buildId, failArtifact = false} = {}) {
  const directory = mkdtempSync(join(tmpdir(), 'amftms-release-test-'));
  try {
    const result = spawnSync('bash', ['-c', `${stubs}\nscript="$1"; shift; source "$script" "$@"`, 'gate', script, ...(phase ? [phase] : [])], {
      cwd: root, encoding: 'utf8', env: {...process.env,
        TEST_LOG: join(directory, 'calls'), TEST_RELEASE_DIR: directory,
        AMFTMS_RELEASE_DIR: directory, AMFTMS_RELEASE_BROWSER: '0', AMFTMS_RELEASE_UI: '0',
        TEST_FAIL: fail ?? '', TEST_BUILD_ID: buildId ?? 'build-123',
        TEST_ARTIFACT_FAIL: failArtifact ? '1' : '0',
        TEST_IMAGE_RESULT: imageResult ?? `SUCCESS\t${repository}:build-123\t${digest}`}
    });
    let calls = '';
    try { calls = readFileSync(join(directory, 'calls'), 'utf8'); } catch {}
    return {...result, calls, directory};
  } finally { rmSync(directory, {recursive: true, force: true}); }
}

test('release gate checks typed JS, all Node suites and both .NET assemblies before publishing and checking artifacts', () => {
  const result = run('verify-release.sh');
  assert.equal(result.status, 0, result.stderr);
  const calls = result.calls.trim().split('\n');
  assert.deepEqual(calls.slice(0, 5), ['npm ci --prefix Client', 'npm run js:check --prefix Client',
    'npm run styles:build --prefix Client', 'npm run js:build --prefix Client', 'npm test --prefix Client']);
  assert.match(calls[5], /^dotnet build AMFTMS\.slnx -c Release -warnaserror/);
  assert.equal(calls[6], 'dotnet test AMFTMS.slnx -c Release --no-build');
  assert.match(calls[7], /^dotnet publish Client\/Client\.csproj -c Release -warnaserror --no-restore/);
  assert.match(calls[8], /^node Client\/build\/verifyRelease\.mjs /);
  assert.match(result.stdout, /Browser verification not run/);
});

test('each release gate failure prevents publish/deploy continuation', () => {
  for (const fail of ['npm run js:check --prefix Client', 'npm test --prefix Client',
    'dotnet test AMFTMS.slnx -c Release --no-build']) {
    const result = run('deploy-client.sh', {fail});
    assert.notEqual(result.status, 0, fail);
    assert.doesNotMatch(result.calls, /dotnet publish|firebase deploy/);
  }
});

test('Firebase receives the exact unique staged artifact after verification', () => {
  const result = run('deploy-client.sh');
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.calls.includes(`node Client/build/verifyRelease.mjs ${result.directory}/publish\n`));
  assert.ok(result.calls.endsWith(`firebase deploy --only hosting --public ${result.directory}/publish/wwwroot\n`));
});

test('failed artifact verification stops Firebase deployment', () => {
  const result = run('deploy-client.sh', {failArtifact: true});
  assert.notEqual(result.status, 0);
  assert.match(result.calls, /node Client\/build\/verifyRelease\.mjs/);
  assert.doesNotMatch(result.calls, /firebase deploy/);
});

test('unknown release gate phase is rejected without running any tools', () => {
  const result = run('verify-release.sh', {phase: 'unknown'});
  assert.equal(result.status, 2);
  assert.equal(result.calls, '');
});

test('Cloud Run deploys only the successful build result digest', () => {
  const result = run('deploy-server.sh');
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.calls, /gcloud builds submit --config cloudbuild.yaml --suppress-logs --format=value\(id\) \./);
  assert.ok(result.calls.includes(`gcloud run deploy amftms-api --image ${repository}@${digest}`));
  assert.doesNotMatch(result.calls, /:latest/);
});

test('invalid build identities, mismatched images, failed builds and missing digests never deploy', () => {
  const cases = [{buildId: 'unexpected output'},
    {imageResult: `SUCCESS\t${repository}:another-build\t${digest}`},
    {imageResult: `FAILURE\t${repository}:build-123\t${digest}`},
    {imageResult: `SUCCESS\t${repository}:build-123\t`},
    {fail: 'gcloud builds submit --config cloudbuild.yaml --suppress-logs --format=value(id) .'}];
  for (const options of cases) {
    const result = run('deploy-server.sh', options);
    assert.notEqual(result.status, 0);
    assert.doesNotMatch(result.calls, /gcloud run deploy/);
  }
});

test('CI and Cloud Build compose the same gate before building a uniquely tagged image', () => {
  const cloud = readFileSync(join(root, 'cloudbuild.yaml'), 'utf8');
  assert.match(readFileSync(join(root, '.github/workflows/ci.yml'), 'utf8'), /run: bash verify-release\.sh/);
  assert.match(cloud, /args: \[verify-release\.sh, node\]/);
  assert.match(cloud, /args: \[verify-release\.sh, dotnet\]/);
  assert.match(cloud, /args: \[verify-release\.sh, artifact\]/);
  assert.ok(cloud.indexOf('verify-release.sh, artifact') < cloud.indexOf('gcr.io/cloud-builders/docker'));
  assert.equal(cloud.match(/api:\$BUILD_ID/g)?.length, 2);
  assert.doesNotMatch(cloud, /:latest/);
});
