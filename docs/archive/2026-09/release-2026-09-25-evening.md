# Release of September 25, evening

The owner authorized this release after an independent review of the
border hardening, per [release.md](../../operations/release.md).

**Status:** complete.
- API: released and verified at 19:57 UTC.
- Frontend: first attempt blocked at 20:10 by expired Firebase CLI
  credentials; the owner signed in again, and it was released and
  verified at 21:30 UTC.

One defect was found afterwards and fixed locally, not released: see
[AMF1414's tracked plan](#amf1414s-tracked-plan).

Not done: no message sent, no forced replan, no reset, no configuration
or credential change, no Driver Pay merge.

## Package

- **Code:** `401387c9`, 28 commits and 152 files since `ce465d2d`, the
  morning's release. It is `aa94a802` plus one formatting fix: the first
  Cloud Build (`9f0b289a`) stopped at the Client format check on
  `tests/browser/hoursForecastSmoke.mjs`, and deployed nothing.
- **Contents:**
  - border hardening for AMF1414 (truck 11007): a road for one-country
    work is asked to avoid crossings, and the returned road is checked in
    full; one that leaves is refused, and a saved one is bought again for
    that load only;
  - the base-road border verdict, its background check and two audit
    rules;
  - return navigation for the map and Dispatch, duty duration in
    Messages, Active/Inactive fleet lists, archived chats of inactive
    drivers, the next-stop label, and test synchronization fixes.
- **Gates:** Cloud Build ran its gate phases before the image. The
  frontend wrapper's gate on the same commit passed: Server 3,706,
  Client C# 1,217, the JavaScript suites and the offline UI smoke;
  artifact `release-xAAShB` verified.

## Migration: one, additive

`20260925190516_RecordBaseRoadBorderCheck` adds the nullable
`DispatchBaseRoutes.BorderCheck varchar(8)`. The older binary ignores it.
`__EFMigrationsHistory` went from 72 to 73 rows.

## Backup

`local-backups/pulsartms-release-backup.kRvdJH/before-2026-09-25-evening-release.dump`:
`pg_dump` 18.6 custom format, 26,135,141 bytes, SHA-256
`c056b635…c7094def`, in `local-backups/inventory.json`. `pg_restore
--list` reads 737 entries, 104 of them table data. No restore rehearsed.

## API

- **Before:** `amftms-api-b-73e68210-…` at 100%, generation 246.
- **Build:** `28367de3-af14-43ac-a0a9-09d9a6a52a54`, image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:1469c75cb3bf9d3bc8c4aae3f248ec56d99beb434da7b98ab56a713905b011ca`.
- **After:** generation 248, spec and status traffic 100% on
  `amftms-api-b-28367de3-af14-43ac-a0a9-09d9a6a52a54`; Ready,
  ConfigurationsReady and RoutesReady true; 1 GiB, CPU always allocated,
  17 environment variables, as before. Startup probe passed at 19:57:03.
- **Old revision:** kept as the rollback target (the migration only adds a
  column). It drained; its last log line was at 20:01:43.
- **HTTP:** `/api/health/live` 200 on the service and through Hosting;
  `/api/health/ready` 401 without sign-in. The new revision logged no
  error and no 5xx; the `libgssapi_krb5` start line is known.

**Protected data** (`READ ONLY` reads before and twice after): users,
AMF1407, active and planned legs, truck 11005, fuel sends, conversations
and messages, outbound queue, templates, groups, broadcasts, stored
credentials and automatic fuel sending are unchanged. Only the migration
count and the base roads changed.

**Border results:**
- AMF1414's base road was bought again at 19:57:14, once (one route call,
  nothing refused): 1,234.6 mi, verdict `stays`, instead of 986.5 mi
  through Ontario. So the provider honoured the avoidance for this road.
- The backfill judged all 40 saved base roads within minutes: 34 `stays`,
  6 `n/a`, none leaving and none unknown.
- Audit: only the known `execution.cancelled-source-held` finding; no
  border finding.
- AMF1414's saved plan still holds its 18:09 route; it takes the new
  base road on its next planning build, which was not forced.

## Frontend (21:24–21:30 UTC)

- **First attempt (20:04):** the wrapper's gate passed, then Firebase
  refused the upload ("credentials are no longer valid"). Nothing was
  published.
- **Release:** after the owner signed in again, `PULSARTMS_RELEASE_UI=1
  bash deploy-client.sh` ran from the same clean worktree of `401387c9`
  (local Client settings copied, compared). Its gate passed again:
  Server 3,706, Client C# 1,217, the JavaScript suites and the UI smoke.
  Artifact `release-Ropy01`; Firebase uploaded 285 files, "release
  complete".
- **What Hosting serves:** `index.html`, `css/main.css`
  (`?v=db1da54f36149762`), `appsettings.json` and
  `_framework/dotnet.sivox1e2p5.js` are byte-identical to the artifact.
  The entry HTML is `no-cache`, hashed framework files are
  `public, max-age=31536000, immutable`, and `/appsettings.json` is
  valid JSON.
- **API after:** still generation 248, 100% on the same revision and
  digest, health 200 directly and through Hosting, no error or 5xx.
- **Protected data** between 20:10 and 21:30: unchanged except ordinary
  operation (one leg completed at 21:04; one base road recalculated at
  20:58, verdict `stays`).

## AMF1414's tracked plan

The base road was fixed, but the plan still was not. Tracking rerouted
the plan at 20:48 on the new revision:
- a new 139.9 mi connector from the truck;
- then the saved 986.5 mi leg through Ontario.

`BaseRouteService.RecalculateAsync` keeps the previous plan's remaining
legs and buys only the connector, and the border check did not guard
that path.

Fixed locally in `326fcd55`, not released:
- a remainder that leaves is bought again whole;
- such a road is no longer kept as the display reference;
- the display read skips a base road recorded as leaving.

Until that is released, AMF1414's plan (map, ETA and fuel) keeps the
Ontario leg. No replan was forced.

## Open

- **Truck 11007 ETA:** unchanged. The admin counters are read only live
  through `GET /api/diagnostics/stages`, with no exporter or persistence;
  see [the incident record](eta-11007-2026-09-25.md).
- **Border check limit:** a foreign stretch under 1 km inside one
  straight segment of geometry can go unseen.
