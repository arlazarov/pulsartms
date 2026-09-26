# Release of September 26, morning

The owner authorized it ("work and publish") and Root cleared candidate
`3630bbdb` (see [the candidate record](candidate-2026-09-26.md)). The
undecided fuel reserve policy and Driver Pay are not in it.

**Status: complete.** API released and verified at 09:22 UTC. The
frontend's upload first stopped on expired Firebase CLI credentials; after
the owner signed in again, the verified artifact was published at
10:47 UTC.

Not done: no message sent, no replan, no reset, no configuration or
credential change.

## Backup

`local-backups/pulsartms-release-backup.kZ8E0G/before-2026-09-26-release-3630bbdb.dump`,
taken 09:12 UTC: `pg_dump` 18.6 custom format with `--no-owner
--no-privileges`, 28 MB, SHA-256
`753d415d62d84a3e3564416edd9208c8d9e5e14d7175ab2219de5d4a31845962`,
720 entries, 104 table data; in `local-backups/inventory.json`. No
restore rehearsed. No migration in this release (still 73).

## API (09:13-09:22 UTC)

- **Before:** `amftms-api-b-d7a80320-…` (`257aeaf1`) at 100%.
- **Build:** Cloud Build `f169545d-dcb5-49bd-9419-855efa4c5273` from a
  clean worktree of `3630bbdb`; image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:26d3cd54c90e09b9e6f1bf181f85778029c5cffa85af55319b7f9a63076fb854`.
- **After:** generation 258, spec and status traffic 100% on
  `amftms-api-b-f169545d-dcb5-49bd-9419-855efa4c5273`; Ready,
  ConfigurationsReady and RoutesReady true; 1 GiB, CPU always
  allocated, minimum 1 and maximum 1 instance, 17 environment variables.
- **Health:** `/api/health/live` 200 on the service and through Hosting;
  `/api/health/ready` 401 without sign-in. No error or 5xx logged by the
  new revision.
- **Old revision:** kept as the rollback target (no schema change); its
  last log line was at 09:26:38.
- **Protected data** (read-only, before and after): migrations, users,
  carriers, conversations, messages, broadcasts, fuel sends and the
  WhatsApp credential revisions unchanged. Stations with a status checked
  in the last two days went from 0 to 7: the repaired sweep
  (`327252b9`) is checking stations in live plans.
- Root confirmed the 100% traffic independently.

## Frontend

- `PULSARTMS_RELEASE_UI=1 bash deploy-client.sh` from the same worktree
  (09:23-09:28): its gate passed (JavaScript 656, Client 1,225, Server
  3,743, UI smoke); artifact kept as
  `artifacts/managed/deploy-3630bbdb-release-hZVJT4` (manifest SHA-256
  `ce8fd7505ad421202fd809152d005611fdcf81a9bfeab0ed2e75d528d508cb42`).
- Firebase first refused the upload ("credentials are no longer valid");
  nothing was published then.
- **Published (10:47 UTC):** after the owner's sign-in, the artifact was
  checked again against its manifest (all files match, manifest SHA-256
  unchanged) and published with the script's own post-gate steps from the
  `3630bbdb` worktree, without repeating the unchanged gate:
  `node Client/build/verifyHostedConfig.mjs` and `firebase deploy --only
  hosting --public`. Hosting version `sites/amftms/versions/aef44d6ce29bbfa8`,
  released 2026-09-26T10:47:24Z. Log: `hosting.log` beside the artifact.
- **Served:** all 95 uncompressed files fetched from
  `tms.amfcarrier.com` match the manifest's SHA-256 (0 mismatches; the
  `.br`/`.gz` variants are served by encoding and were not compared).
  `index.html`, `css/main.css` and `appsettings.json` are `no-cache`;
  fingerprinted framework files are `public, max-age=31536000,
  immutable`. `appsettings.json` is valid JSON with the Maps key.
- **After:** the API is still 100% on `amftms-api-b-f169545d-…`, health
  200 through Hosting and directly, no error or 5xx since. The new client
  reads `/api/messaging/changes` (200); tabs still on the old client hit
  `/api/messaging/events` (401/404) until reloaded.
- The build is not byte-reproducible: this artifact and the candidate's
  (`candidate-3630bbdb-release-h5qEVM`) differ in 18 manifest lines (the
  Client assembly's fingerprinted name). The one to publish is this
  verified artifact.
- Meanwhile the old client finds no `/api/messaging/events` and keeps its
  30-second poll, as it effectively did before.

## Still to do

- Measure WhatsApp latency with one owner-sent test after the frontend.
- Read the ETA "not saved" reasons for 11006 on the new revision.
- 11007's Erie detour stays unresolved; see
  [its record](route-11007-erie-detour-2026-09-26.md). No replan was
  forced.
