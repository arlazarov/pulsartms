# Fuel planning and quiet ETA release — September 9, 2026

## Deployed scope

This release supersedes the deployment boundaries in the
[rolling fuel plan](rolling-fuel-plan-2026-09-08.md),
[in-flight ETA retention](quiet-eta-inflight-2026-09-08.md), and
[fuel import recovery](fuel-import-recovery-2026-09-08.md) records.
Their earlier test counts and unapplied-migration statements remain historical.

- Cloud Build `2e77c37d-fa8e-45ca-acee-e7edaa793066` succeeded.
- API image: `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:ea4ff743dc32588efd80e9a64dad740dfa320fc9c9581e4744f33c45fb443f5c`.
- Cloud Run revision `amftms-api-00080-8bl` became ready with 100% traffic.
  Previous revision `amftms-api-00079-xkx` was retained for rollback.
- Firebase Hosting published the exact verified Client artifact
  `artifacts/release.C1u4UE/publish/wwwroot` after the API was ready.
  All 74 uncompressed public files fetched from `https://amftms.web.app`
  matched the staged SHA-256 values; all 222 staged files, including compressed
  variants, passed the release artifact gate.
- Public API liveness returned HTTP 200. Scoped application logs from the new
  revision contained no warnings/errors; request logs contained no HTTP 5xx.
  One fuel-recalculation request returned HTTP 400 at 04:15:07 UTC; it was not
  initiated by the release-verification browser. This is not a clean claim for
  every live fuel search.

The rejected request took 2.859 seconds, trace
`7b3a22abd4fc53f93e4730cb3df9aa46`. Narrow revision/time-scoped application logs
contained no correlated reason. The planning boundary maps handled planning
exceptions to HTTP 400 without logging their message. Required-input, reserve or
route-feasibility rejection cannot be distinguished from a planner defect using
this response code alone. Missing saved connections in future ETA cards are not
an established cause: fuel planning can calculate an absent connection. No retry
was forced; the exact rejection remains unverified.

The release includes durable current/future-load fuel plans, bounded explicit fuel
search, quiet ETA/Cycle/recap retention during refresh, and the development Gmail
worker safeguard. It does not rotate credentials or start the local API.

## Migration and recovery

Production startup applied `20260909020907_StoreTruckFuelPlans` successfully.
Read-only verification confirmed its migration-history entry, six expected table
columns and three indexes, including unique truck ownership. The change adds a
table; it does not backfill or rewrite existing operational tables. Keep the
additive table when rolling back the API image rather than executing destructive
Down SQL automatically.

Before deployment, a private database backup was created outside the upload root:
`/Users/antonarlazarov/Developer/amftms-db-backup.TpwS9d/neondb-before-StoreTruckFuelPlans.dump`.
It is 120,704,203 bytes; SHA-256:
`aca6700761b57c9a8503f946c50b434c15aeee8045b1efa5d95e27c90b1a41ee`.
The directory/file permissions are 0700/0600. `pg_restore --list` inspected 177
archive entries; a full restore rehearsal was not performed.

September 9 price imports remained present: 605 USD and 91 CAD rows for the dated
files, of which 602/87 had station coordinates. Older CAD validity windows also
cover September 9; these are not additional newly imported stations. Gmail watch
and recovery errors were empty. The automatic recovery completed at 03:47:11 UTC,
with the next recovery at 05:47:11 UTC and renewal at September 10 03:33:03 UTC.

## Release gate and fixes discovered by it

Final Cloud gate: **755 Server + 404 Client C# + 178 JavaScript = 1,337 tests**,
zero failed or skipped; strict solution build completed with zero warnings/errors.
Published assets and six JavaScript entry-point dependency graphs passed checks.

- Staged UI smoke: 44 page checks, desktop/mobile, light/dark, normal/enlarged text.
  Report: `Client/test-results/ui-smoke/report.json`.
- Staged ETA/fuel retention: four scenarios, 32 screenshots, including pending
  requests across the previous forecast deadline and complete replacement.
  Report: `test-results/fuel-final-hours/report.json`.
- Offline map/GPU/popup checks: 4 GPU + 8 popup + 20 hours + 2 constrained cases,
  42 screenshots. Report: `test-results/fuel-release-stop-cards/report.json`.
  These use the matching compiled JavaScript/CSS, not a live provider.
- These browser reports contained zero failures, browser errors or unexpected
  requests. Representative mobile/desktop output was visually inspected.
- The actual Cloud source subset independently passed 50 Server, two Client and
  18 Node architecture checks. Required rules and maintained tooling source were
  present; known credential, environment, raw-capture and generated-tool paths
  were excluded from upload.
- After adding this release record, `bash test.sh architecture` passed another
  50 Server, two Client and 18 Node checks. This documentation-only follow-up is
  not counted as additional unique tests in the full release total.

Build `3fc50f20-5da1-4226-a852-39c3f34da49e` was cancelled before deployment when
packaging excluded a rule file required by the architecture audit. Build
`9900c186-8869-4c8b-b9de-7e20631ab1f9` failed before deployment: the cloud source
also omitted maintained tooling, and a responsive-stream test depended on real
timer scheduling. Packaging now includes audit inputs while excluding private
artifacts. Stream tests use an injected test clock; the production one-millisecond
yield is unchanged. Twenty repeated focused runs passed all 120 checks. No
architecture assertion or production delay was weakened to pass the gate.

## Live verification and limitations

The owner authenticated normally on the production site. After Client publication,
the refreshed Fleet Map loaded the real provider without browser errors. Truck
54777 displayed its current route, next-load roads, September 9 fuel stations,
ETA and Cycle. A future pickup popup resolved its order, address, appointment,
ETA, Cycle and next recap; the bottom card was visually inspected at the narrow
live viewport. No manual fuel search or forced paid routing request was performed.
Dispatch Cards also loaded the current/future forecasts, copyable order numbers,
per-stop Cycle and the September 11 00:00 recap of 3h 05m for truck 54777.
Other trucks had future assignments without a saved preceding-load connection;
their explicit unavailable forecasts were observed, not replaced with invented
arrival estimates or force-recalculated during verification.

No safe isolated PostgreSQL test fixture was available. PostgreSQL execution tests
were not run; the authorized production migration and read-only operational checks
are not substitutes for an isolated test fixture. No local SQL server/container
was started. Production load, memory, billing reduction, pump availability and
actual station visits were not measured. Offline tests do not establish globally
optimal routing or legal HOS compliance.

The local API remains stopped because its previous configuration shared the
production database. The development Gmail flag is a safeguard, not permission
to use production as a development/test fixture. Credential rotation from the
diagnostic exposure recorded in the recovery incident remains a separate,
coordinated security action.
