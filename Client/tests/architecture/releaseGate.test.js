import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = fileURLToPath(new URL('../../../', import.meta.url));
const repository = 'us-east4-docker.pkg.dev/amftms/amftms/api';
const digest = `sha256:${'a'.repeat(64)}`;
const revision = 'amftms-api-b-build-123';
const ready = [{ type: 'Ready', status: 'True' }];
const revisionResult = {
  metadata: { name: revision },
  spec: { containers: [{ image: `${repository}@${digest}` }] },
  status: { imageDigest: `${repository}@${digest}`, conditions: ready },
};
const traffic = [{ revisionName: revision, percent: 100 }];
const serviceResult = {
  metadata: { name: 'amftms-api', generation: 2 },
  spec: { traffic },
  status: { traffic, conditions: ready, observedGeneration: 2 },
};
const stubs = `
record() { printf '%s\\n' "$*" >> "$TEST_LOG"; [[ "$*" != "\${TEST_FAIL:-}" ]]; }
npm() { record npm "$@"; }
dotnet() { record dotnet "$@"; }
node() {
  record node "$@" && [[ "$TEST_ARTIFACT_FAIL" != 1 ]] || return 1
  if [[ "$1" == scripts/cloud-run-release.mjs ]]; then command node "$@"; fi
}
firebase() { record firebase "$@"; }
mktemp() { printf '%s\\n' "$TEST_RELEASE_DIR"; }
gcloud() {
  record gcloud "$@" || return 1
  if [[ "$1 $2" == 'builds submit' ]]; then printf '%s\\n' "\${TEST_BUILD_ID:-build-123}";
  elif [[ "$1 $2" == 'builds describe' ]]; then
    printf '%s\\n' "$TEST_IMAGE_RESULT"
  elif [[ "$1 $2 $3" == 'run revisions describe' ]]; then
    if [[ "$*" == *'spec.containers[0].image'* ]]; then
      printf '%s\\n' "$TEST_REVISION_RESULT"
    else
      printf '%s\\n' "$TEST_REVISION_UNINDEXED_RESULT"
    fi
  elif [[ "$1 $2 $3" == 'run services describe' ]]; then
    printf '%s\\n' "$TEST_SERVICE_RESULT"
  fi
}
export -f record npm dotnet node firebase mktemp gcloud
`;

function run(
  script,
  {
    phase,
    fail,
    imageResult,
    revisionData = revisionResult,
    serviceData = serviceResult,
    buildId,
    failArtifact = false,
    environment = () => ({}),
  } = {},
) {
  const directory = mkdtempSync(join(tmpdir(), 'pulsartms-release-test-'));
  try {
    const env = { ...process.env };
    for (const name of Object.keys(env)) {
      if (name.startsWith('AMFTMS_') || name.startsWith('PULSARTMS_'))
        delete env[name];
    }
    Object.assign(env, {
      TEST_LOG: join(directory, 'calls'),
      TEST_RELEASE_DIR: directory,
      PULSARTMS_RELEASE_DIR: directory,
      PULSARTMS_RELEASE_BROWSER: '0',
      PULSARTMS_RELEASE_UI: '0',
      TEST_FAIL: fail ?? '',
      TEST_BUILD_ID: buildId ?? 'build-123',
      TEST_ARTIFACT_FAIL: failArtifact ? '1' : '0',
      TEST_IMAGE_RESULT:
        imageResult ?? `SUCCESS\t${repository}:build-123\t${digest}`,
      TEST_REVISION_RESULT: JSON.stringify(revisionData),
      TEST_REVISION_UNINDEXED_RESULT: JSON.stringify({
        metadata: revisionData.metadata,
        status: revisionData.status,
      }),
      TEST_SERVICE_RESULT: JSON.stringify(serviceData),
      ...environment(directory),
    });
    for (const name of Object.keys(env))
      if (env[name] === undefined) delete env[name];
    const result = spawnSync(
      'bash',
      [
        '-c',
        `${stubs}\nscript="$1"; shift; source "$script" "$@"`,
        'gate',
        script,
        ...(phase ? [phase] : []),
      ],
      {
        cwd: root,
        encoding: 'utf8',
        env,
      },
    );
    let calls = '';
    try {
      calls = readFileSync(join(directory, 'calls'), 'utf8');
    } catch {}
    return { ...result, calls, directory };
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test('release gate checks typed JS, all Node suites and both .NET assemblies before publishing and checking artifacts', () => {
  const result = run('verify-release.sh');
  assert.equal(result.status, 0, result.stderr);
  const calls = result.calls.trim().split('\n');
  assert.deepEqual(calls.slice(0, 6), [
    'npm ci --prefix Client',
    'npm run format:check --prefix Client',
    'npm run js:check --prefix Client',
    'npm run styles:build --prefix Client',
    'npm run js:build --prefix Client',
    'npm test --prefix Client',
  ]);
  assert.match(
    calls[6],
    /^dotnet build pulsartms\.slnx -c Release -warnaserror/,
  );
  assert.equal(calls[7], 'dotnet test pulsartms.slnx -c Release --no-build');
  assert.match(
    calls[8],
    /^dotnet publish Client\/Client\.csproj -c Release -warnaserror --no-restore/,
  );
  assert.match(calls[9], /^node Client\/build\/verifyRelease\.mjs /);
  assert.match(result.stdout, /Browser verification not run/);
});

test('each release gate failure prevents publish/deploy continuation', () => {
  for (const fail of [
    'npm run format:check --prefix Client',
    'npm run js:check --prefix Client',
    'npm test --prefix Client',
    'dotnet test pulsartms.slnx -c Release --no-build',
  ]) {
    const result = run('deploy-client.sh', { fail });
    assert.notEqual(result.status, 0, fail);
    assert.doesNotMatch(result.calls, /dotnet publish|firebase deploy/);
  }
});

test('Firebase receives the exact unique staged artifact after verification', () => {
  const result = run('deploy-client.sh');
  assert.equal(result.status, 0, result.stderr);
  assert.ok(
    result.calls.includes(
      `node Client/build/verifyRelease.mjs ${result.directory}/publish\n`,
    ),
  );
  assert.ok(
    result.calls.endsWith(
      `firebase deploy --only hosting --public ${result.directory}/publish/wwwroot\n`,
    ),
  );
});

test('legacy release directories remain accepted only when the canonical variable is unset', () => {
  const legacy = run('deploy-client.sh', {
    environment: directory => ({
      PULSARTMS_RELEASE_DIR: undefined,
      AMFTMS_RELEASE_DIR: directory,
    }),
  });
  assert.equal(legacy.status, 0, legacy.stderr);
  assert.ok(
    legacy.calls.endsWith(
      `firebase deploy --only hosting --public ${legacy.directory}/publish/wwwroot\n`,
    ),
  );

  const canonical = run('deploy-client.sh', {
    environment: directory => ({ AMFTMS_RELEASE_DIR: `${directory}/legacy` }),
  });
  assert.equal(canonical.status, 0, canonical.stderr);
  assert.ok(
    canonical.calls.endsWith(
      `firebase deploy --only hosting --public ${canonical.directory}/publish/wwwroot\n`,
    ),
  );
  assert.doesNotMatch(canonical.calls, /\/legacy\/publish/);

  const empty = run('verify-release.sh', {
    phase: 'artifact',
    environment: directory => ({
      PULSARTMS_RELEASE_DIR: '',
      AMFTMS_RELEASE_DIR: directory,
    }),
  });
  assert.equal(empty.status, 2);
  assert.equal(empty.calls, '');
});

test('release flags use canonical values and preserve explicitly opted-in legacy invocations', () => {
  const canonical = run('verify-release.sh', {
    phase: 'artifact',
    environment: () => ({
      AMFTMS_RELEASE_UI: '1',
      AMFTMS_RELEASE_BROWSER: '1',
    }),
  });
  assert.equal(canonical.status, 0, canonical.stderr);
  assert.doesNotMatch(canonical.calls, /npm run test:(ui|browser)/);

  const legacy = run('verify-release.sh', {
    phase: 'artifact',
    environment: () => ({
      PULSARTMS_RELEASE_UI: undefined,
      PULSARTMS_RELEASE_BROWSER: undefined,
      AMFTMS_RELEASE_UI: '1',
      AMFTMS_RELEASE_BROWSER: '1',
    }),
  });
  assert.equal(legacy.status, 0, legacy.stderr);
  assert.match(legacy.calls, /npm run test:ui --prefix Client/);
  assert.match(legacy.calls, /npm run test:browser --prefix Client/);

  const enabled = run('verify-release.sh', {
    phase: 'artifact',
    environment: () => ({
      PULSARTMS_RELEASE_UI: '1',
      PULSARTMS_RELEASE_BROWSER: '1',
      AMFTMS_RELEASE_UI: '0',
      AMFTMS_RELEASE_BROWSER: '0',
    }),
  });
  assert.equal(enabled.status, 0, enabled.stderr);
  assert.match(enabled.calls, /npm run test:ui --prefix Client/);
  assert.match(enabled.calls, /npm run test:browser --prefix Client/);
});

test('failed artifact verification stops Firebase deployment', () => {
  const result = run('deploy-client.sh', { failArtifact: true });
  assert.notEqual(result.status, 0);
  assert.match(result.calls, /node Client\/build\/verifyRelease\.mjs/);
  assert.doesNotMatch(result.calls, /firebase deploy/);
});

test('unknown release gate phase is rejected without running any tools', () => {
  const result = run('verify-release.sh', { phase: 'unknown' });
  assert.equal(result.status, 2);
  assert.equal(result.calls, '');
});

test('Cloud Run deploys only the successful build result digest', () => {
  const result = run('deploy-server.sh');
  assert.equal(result.status, 0, result.stderr);
  assert.match(
    result.calls,
    /gcloud builds submit --project amftms --config cloudbuild.yaml --suppress-logs --format=value\(id\) \./,
  );
  assert.ok(
    result.calls.includes(
      `gcloud run deploy amftms-api --image ${repository}@${digest}`,
    ),
  );
  assert.doesNotMatch(result.calls, /:latest/);
  assert.match(result.calls, /--revision-suffix b-build-123 --no-traffic/);
  const revisionCheck = result.calls.indexOf(
    `node scripts/cloud-run-release.mjs revision ${revision}`,
  );
  const routeTraffic = result.calls.indexOf(
    'gcloud run services update-traffic amftms-api',
  );
  const trafficCheck = result.calls.indexOf(
    `node scripts/cloud-run-release.mjs traffic amftms-api ${revision}`,
  );
  assert.ok(revisionCheck > 0 && revisionCheck < routeTraffic);
  assert.ok(routeTraffic < trafficCheck);
  assert.match(result.calls, /--to-revisions=amftms-api-b-build-123=100/);
  assert.match(result.stdout, /Verified 100% traffic on revision/);
  assert.doesNotMatch(result.calls, /json\(.*(?:env|spec\.containers,)/);
  assert.ok(
    result.calls.includes(
      '--format=json(metadata.name,spec.containers[0].image,' +
        'status.imageDigest,status.conditions)',
    ),
  );
});

test('gcloud indexed image projection preserves the container array', () => {
  const result = run('deploy-server.sh', {
    revisionData: {
      metadata: { name: revision },
      spec: { containers: [{ image: `${repository}@${digest}` }] },
      status: {
        imageDigest: `${repository}@${digest}`,
        conditions: [{ status: 'True', type: 'Ready' }],
      },
    },
  });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /Verified 100% traffic/);

  const omitted = run('deploy-server.sh', {
    revisionData: {
      metadata: revisionResult.metadata,
      status: revisionResult.status,
    },
  });
  assert.notEqual(omitted.status, 0);
  assert.doesNotMatch(omitted.calls, /update-traffic/);
});

test('deployment environment file uses the canonical name with an unset-only legacy fallback', () => {
  const canonical = run('deploy-server.sh', {
    environment: directory => ({
      PULSARTMS_DEPLOY_ENV_FILE: `${directory}/canonical.yaml`,
      AMFTMS_DEPLOY_ENV_FILE: `${directory}/legacy.yaml`,
    }),
  });
  assert.equal(canonical.status, 0, canonical.stderr);
  assert.ok(
    canonical.calls.includes(
      `--env-vars-file ${canonical.directory}/canonical.yaml`,
    ),
  );
  assert.doesNotMatch(canonical.calls, /legacy\.yaml/);

  const legacy = run('deploy-server.sh', {
    environment: directory => ({
      AMFTMS_DEPLOY_ENV_FILE: `${directory}/legacy.yaml`,
    }),
  });
  assert.equal(legacy.status, 0, legacy.stderr);
  assert.ok(
    legacy.calls.includes(`--env-vars-file ${legacy.directory}/legacy.yaml`),
  );

  const empty = run('deploy-server.sh', {
    environment: directory => ({
      PULSARTMS_DEPLOY_ENV_FILE: '',
      AMFTMS_DEPLOY_ENV_FILE: `${directory}/legacy.yaml`,
    }),
  });
  assert.equal(empty.status, 0, empty.stderr);
  assert.doesNotMatch(empty.calls, /--env-vars-file/);
});

test('invalid build identities, mismatched images, failed builds and missing digests never deploy', () => {
  const cases = [
    { buildId: 'unexpected output' },
    { buildId: 'BUILD-123' },
    { buildId: 'a'.repeat(51) },
    { imageResult: `SUCCESS\t${repository}:another-build\t${digest}` },
    { imageResult: `FAILURE\t${repository}:build-123\t${digest}` },
    { imageResult: `SUCCESS\t${repository}:build-123\t` },
    {
      fail: 'gcloud builds submit --project amftms --config cloudbuild.yaml --suppress-logs --format=value(id) .',
    },
  ];
  for (const options of cases) {
    const result = run('deploy-server.sh', options);
    assert.notEqual(result.status, 0);
    assert.doesNotMatch(result.calls, /gcloud run deploy/);
  }
});

test('Cloud Run revision mismatches and unreadiness never switch traffic', () => {
  const cases = [
    {},
    { ...revisionResult, metadata: { name: 'another-revision' } },
    {
      ...revisionResult,
      status: { ...revisionResult.status, conditions: [] },
    },
    {
      ...revisionResult,
      status: {
        ...revisionResult.status,
        conditions: [{ type: 'Ready', status: 'False' }],
      },
    },
    {
      ...revisionResult,
      status: { ...revisionResult.status, imageDigest: `${repository}:latest` },
    },
    {
      ...revisionResult,
      spec: { containers: [{ image: `${repository}:latest` }] },
    },
  ];
  for (const revisionData of cases) {
    const result = run('deploy-server.sh', { revisionData });
    assert.notEqual(result.status, 0);
    assert.doesNotMatch(result.calls, /update-traffic/);
    assert.doesNotMatch(result.stdout, /Deployed build/);
  }
});

test('unreadable Cloud Run verification responses cannot change traffic', () => {
  const cases = [
    { environment: () => ({ TEST_REVISION_RESULT: 'invalid JSON' }) },
    {
      fail:
        `gcloud run revisions describe ${revision} --project amftms ` +
        '--region us-east4 --format=json(metadata.name,spec.containers[0].image,' +
        'status.imageDigest,status.conditions)',
    },
  ];
  for (const options of cases) {
    const result = run('deploy-server.sh', options);
    assert.notEqual(result.status, 0);
    assert.doesNotMatch(result.calls, /update-traffic/);
    assert.doesNotMatch(result.stdout, /Deployed build/);
  }
});

test('Cloud Run rejects stale or split traffic instead of reporting success', () => {
  const stale = [{ revisionName: 'old-pinned-revision', percent: 100 }];
  const split = [
    { revisionName: revision, percent: 50 },
    { revisionName: 'old-pinned-revision', percent: 50 },
  ];
  const cases = [
    {},
    { ...serviceResult, metadata: { name: 'wrong-service', generation: 2 } },
    { ...serviceResult, spec: { traffic: stale } },
    {
      ...serviceResult,
      status: { ...serviceResult.status, traffic: stale },
    },
    {
      ...serviceResult,
      status: { ...serviceResult.status, traffic: split },
    },
    {
      ...serviceResult,
      status: { ...serviceResult.status, observedGeneration: 1 },
    },
    {
      ...serviceResult,
      spec: { traffic: [{ ...traffic[0], latestRevision: true }] },
    },
    {
      ...serviceResult,
      status: { ...serviceResult.status, conditions: [] },
    },
  ];
  for (const serviceData of cases) {
    const result = run('deploy-server.sh', { serviceData });
    assert.notEqual(result.status, 0);
    assert.match(result.calls, /update-traffic/);
    assert.doesNotMatch(result.stdout, /Deployed build/);
  }
});

test('failed traffic update cannot report a completed deployment', () => {
  const result = run('deploy-server.sh', {
    fail:
      'gcloud run services update-traffic amftms-api --project amftms ' +
      `--region us-east4 --to-revisions=${revision}=100 --quiet --format=none`,
  });
  assert.notEqual(result.status, 0);
  assert.doesNotMatch(
    result.calls,
    /node scripts\/cloud-run-release.mjs traffic/,
  );
  assert.doesNotMatch(result.stdout, /Deployed build/);
});

test('CI and Cloud Build compose the same gate before building a uniquely tagged image', () => {
  const cloud = readFileSync(join(root, 'cloudbuild.yaml'), 'utf8');
  const ci = readFileSync(join(root, '.github/workflows/ci.yml'), 'utf8');
  assert.match(ci, /run: bash verify-release\.sh/);
  assert.match(ci, /PULSARTMS_RELEASE_UI: '1'/);
  assert.equal(
    cloud.match(/PULSARTMS_RELEASE_DIR=\/workspace\/artifacts\/release/g)
      ?.length,
    2,
  );
  assert.doesNotMatch(ci + cloud, /AMFTMS_RELEASE_/);
  assert.match(
    cloud,
    /args: \['-c', 'cp \/usr\/local\/bin\/node \/toolchain\/node && bash verify-release\.sh node'\]/,
  );
  assert.match(
    cloud,
    /args: \['-c', 'PATH=\/toolchain:\$\$PATH bash verify-release\.sh dotnet'\]/,
  );
  assert.equal(
    cloud.match(/name: node-toolchain\s+path: \/toolchain/g)?.length,
    2,
  );
  assert.match(cloud, /args: \[verify-release\.sh, artifact\]/);
  assert.ok(
    cloud.indexOf('verify-release.sh, artifact') <
      cloud.indexOf('gcr.io/cloud-builders/docker'),
  );
  assert.equal(cloud.match(/api:\$BUILD_ID/g)?.length, 2);
  assert.doesNotMatch(cloud, /:latest/);
});
