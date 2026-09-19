# Execution, Switch and mileage release

Date: 2026-09-14. The user authorized verification, deployment and migrations.
This follows the [September 13 verification][previous]. Pay remains deferred.

[previous]: execution-verification-2026-09-13.md

## Released artifacts

- Cloud Build: `d8b4661e-7959-4076-b74c-b527b734be4c`, SUCCESS.
- API revision: `amftms-api-00113-rsc`, receiving 100% of traffic.
- Image repository: `us-east4-docker.pkg.dev/amftms/amftms/api`.
- Digest:
  `sha256:b0d26a2aabe57fca4912e3c72cef9b7a29298bea8b9c5f84844047f097d491f4`.
- Firebase live version: `cc52062972af0b99`.
- Firebase live release: `1789386135122000`.
- Verified Client: `artifacts/managed/release-9VARyK/publish/wwwroot`.
- Public application: <https://tms.amfcarrier.com>.

The API image passed the Cloud Build release gate. Final Client-only fixes were
verified separately by the complete local release gate before uploading that
exact staged Client to Firebase. No mutable image tag was deployed.

## Release verification

Final local gate: `PULSARTMS_RELEASE_UI=1 UI_TEST_BROWSER_CHANNEL=chrome`
with `bash verify-release.sh` and a managed release directory.

- Server: 1819 passed, zero failed or skipped.
- Client: 887 passed, zero failed or skipped.
- Node: 530 passed, zero failed or skipped.
- Strict Release solution build: zero warnings or errors.
- Typed JavaScript, Prettier, SCSS and generated JS checks passed.
- Published artifact verification: 267 assets and seven JS dependency graphs.
- Offline Chrome: 52 page checks across 12 viewport/theme/text-scale cases;
  zero geometry failures, unexpected requests or browser errors.
- Browser report: `artifacts/managed/browser-ui-XuNd0L/report.json`.

The initial browser run found a missing synthetic mileage-policy response and
old selectors/space expectations. These were corrected without permitting
unmocked requests or dropping data assertions. It also exposed a real enlarged
text overflow: fallback ETA status became an extra grid cell in Dispatch.
Fallback arrival/status now share the existing arrival group. Dispatch enables
wrapping through the shared road presentation property, clocks retain AM/PM,
and cycle warnings retain their own row. A style regression was added and the
complete release gate, including browser geometry checks, passed afterward.

## Database migration and recovery material

Production was resolved from Cloud Run configuration without printing secrets:
`neondb`, endpoint `ep-blue-cherry-aefp3o1q`, Neon PostgreSQL 18.6. The direct
endpoint was used for administrative connections; the pooler rejects the
read-only startup options. No database test server or container was created.

Both pending migrations were applied in one transaction:

1. `20260914022232_AddExecutionAndMileage`.
2. `20260914031300_FinishSwitchAndAutomaticMileage`.

The operation locked migration history, checked the expected 33-migration
starting state and used a ten-second lock timeout and five-minute statement
timeout. Existing row counts were compared inside the transaction. The final
history has 35 entries; all indexes are valid and the execution-aware partial
index predicates were verified.

Unchanged counts at migration commit: 384 dispatches, 936 stops, nine trucks,
nine drivers, seven trailers, 16 route plans, 92 base routes, one route choice,
14 deadheads, 13 ETA forecasts and two identity accounts.

Private recovery directory, outside the repository:
`/Users/antonarlazarov/Developer/pulsartms-db-backup.t56KgG`.

- Before-maintenance dump: `before-execution-20260914.dump`, 224110571 bytes.
  SHA-256:
  `76a14544c8d43d148ff9a51aca4026726dd6a5b3a5cee91daddce8163c27d51c`.
- Quiesced dump: `quiesced-before-execution-20260914.dump`, 224110558 bytes.
  SHA-256:
  `2fd6d689682e3b332066396099c156fdecd300d34ef0ef91282f90520f62a422`.
- Reviewed SQL: `reviewed-execution-migrations.sql`.
  SHA-256:
  `24cafa30ca8d4b19f9620da87eb5895832a20472e5973f01165ca6d2e4396c34`.

Directory/archive permissions are 0700/0600. Both custom-format dumps passed
`pg_restore --list` with 205 entries. A complete restore was not tested. The
generated SQL's two transaction wrappers were replaced in memory by one guarded
transaction; its migration statements were unchanged. An initial BOM guard
rejected the input before any database write, then the explicit BOM handling
allowed the reviewed script to proceed.

## Non-rolling-compatible transition

Six traffic tags pointing at old revisions were removed before service disable;
the old revision images were not deleted. Tagged revision URLs can bypass a
disabled service, as described in [Cloud Run manual scaling][scaling].

[scaling]: https://docs.cloud.google.com/run/docs/configuring/services/manual-scaling

The API was manually scaled to zero. Cloud Run recorded shutdown of revision
`amftms-api-00112-m6p` at 11:36:56 UTC, and the service returned HTTP 503.
The local API on port 5086 was also stopped before migration. After the quiesced
backup and migration, the new revision was deployed without traffic. Traffic was
then assigned solely to `amftms-api-00113-rsc` before automatic scaling resumed.
Service min/max were restored to 1/20 and revision maximum remains one.
The local API was restarted from the matching Release build with automatic
migrations still disabled, as in its previous launch command.

Do not route traffic back to an old binary against this schema. Native history
requires a reviewed forward repair; backup restoration is a separate operation.

## Post-deployment evidence and limits

- Direct API and public-domain `/api/health/live`: HTTP 200, `Healthy`.
- Anonymous `/api/health/ready`: HTTP 401; the Admin boundary remains enforced.
  An authenticated readiness request was not performed.
- Published index, CSS, Client WASM and both selected bootstrap scripts match
  staged SHA-256 hashes. Entry HTML/CSS revalidate; hashed assets are immutable.
- Fresh Chrome loaded the actual production Login page with HTTP 200, the
  expected sign-in controls and zero page errors. No account was impersonated.
- No ERROR/exception/retry entries were found in the sampled new-revision logs.
- The independent odometer checkpoint advanced and nine positions were stored.
  This verifies ingestion activity, not complete-trip mileage or allocation.
- No native legs existed at the final read. This rollout did not plan or confirm
  the 11005/54777 exchange, invent Drop/Hook times or rewrite assignments.
- Authenticated live Fleet/Switch workflows, Google map rendering and physical
  phones were not exercised. Offline fixtures do not establish those outcomes.
- There was no isolated PostgreSQL fixture, full backup restore, production load
  test or measured memory/latency improvement. Live migration metadata checks
  are operational verification, not disposable PostgreSQL integration tests.
