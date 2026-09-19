# Local artifact retention

Generated outputs belong in `artifacts/managed`, not new task-named directories.
The shared owner is `scripts/artifacts.mjs`; it runs locally, never against a
database or cloud storage. Normal tests retain one reusable build cache at
`artifacts/tests` instead of duplicating it per task.

## Policy

- Target one GiB across registered managed runs.
- Delete eligible runs older than seven days, or oldest eligible runs first when
  above the size target.
- Keep two newest runs per fixed kind and the most recent successful run.
- Keep active owner processes, outputs with open files and runs containing `.keep`.
- Keep outputs completed within the last hour so follow-up work can use them.
- Never delete unregistered directories, malformed markers, linked trees, existing
  legacy artifacts outside the managed pool or explicit output overrides.
- If `lsof` is unavailable or fails, skip deletion. The module resolves canonical
  paths before comparing open files, including macOS `/var` versus `/private/var`.

The one-GiB value is a retention target, not a hard disk quota. Protected/latest/
recent runs may exceed it; cleanup never overrides those protections. It excludes
the reusable test cache, installed dependencies, active legacy localhost builds,
explicit overrides and Trash. Cleanup runs when workflows run, not on a timer.

Automatic cleanup **permanently deletes only eligible registered generated runs**;
it does not repeatedly fill Trash. Do not put unique source, credentials, business
data or sole copies of evidence there. Pin an output before relying on it for a
long-lived local server, deployment handoff or investigation. Preserve small dated
conclusions under `docs/archive`; do not keep every full publish tree as evidence.

## Commands

```bash
# Preview only.
node scripts/artifacts.mjs prune

# Apply the bounded policy.
node scripts/artifacts.mjs prune --apply

# Isolated temporary build; the runner stays alive until the command finishes.
node scripts/artifacts.mjs run scratch -- dotnet build Client/Client.csproj --artifacts-path '{artifacts}/build'
```

The command runner substitutes `{artifacts}` in arguments and also exposes the
same absolute directory as `PULSARTMS_ARTIFACT_DIR` and its legacy alias
`AMFTMS_ARTIFACT_DIR`. Both identify the current managed run, even when the parent
environment contains an older value. The allowed manual kinds are
`scratch` and `diagnostic`; do not invent a kind per task, which would defeat
latest-result retention. The wrapper returns the child's failure status and
forwards termination signals. It marks completion and prunes after the command.
Before terminating the wrapper while leaving a background process running, pin
the run; process/open-file protection is a safety net, not a deployment handoff.

Create an empty `.keep` file inside a particular run to exempt it. Remove only
that marker when the run is no longer needed; the next cleanup evaluates it.

## Automatic integration

`bash test.sh` prunes before using its shared build cache. Default
`bash verify-release.sh` and `bash deploy-client.sh` use the managed release runner;
the deployment wrapper keeps the lease through the Firebase operation. An explicit
`PULSARTMS_RELEASE_DIR` remains caller-owned, unchanged and outside automatic deletion.
Release scripts accept `AMFTMS_RELEASE_DIR` only when the canonical variable is
unset. An explicitly configured canonical value always wins.
The existing staged integrity and test gates are not skipped or reordered.

New runs use `.pulsartms-artifact.json`. Retention still recognizes existing
`.amftms-artifact.json` markers under the same policy. If both exist, the canonical
marker wins; a malformed or linked canonical marker never falls back to a legacy
marker to authorize deletion.

Browser probes allocate a fresh managed output by default, log its absolute path,
and finish the lease on process exit. Reports, screenshots and optional heap dumps
are grouped per run. The existing `*_OUTPUT_DIR` variables remain explicit unmanaged
overrides, including `MAP_LIFECYCLE_OUTPUT_DIR` for the lifecycle probe. Such
overrides intentionally bypass retention and must be cleaned by their owner.

Heap snapshots remain opt-in via `MAP_TEST_HEAP=1`; ordinary checks do not enable
them. They can contain application/provider state: keep them local and never
commit or upload them. Runtime JavaScript/CSS and their current hashed assets
are not cleanup targets.

Regression checks live in `Client/tests/architecture/artifactRetention.test.js`
and run through both `npm test` and `bash test.sh`. They cover bounded selection,
pins, active/open files, canonical paths, malformed markers, linked paths, dry run,
last-success retention and browser producer wiring. File-deletion checks use
temporary filesystem fixtures, not application databases.
