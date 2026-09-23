# Driver messaging, file storage and audit release — September 23, 2026

The user authorized this release with its migrations. API and Client from
`main` at `9b32cba9`. Nothing here sends WhatsApp messages: no integration
credentials are stored, the environment carries no storage or WhatsApp
settings, and automatic fuel sending is off everywhere.

## Code deployed

- Cloud Build `971b6d90-e249-4ffc-b576-943da592f72f`, image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:0f71ba2808d843ba408843ead59347be39b2ebb16356a63d8886389f1165215e`.
- Revision `amftms-api-b-971b6d90-e249-4ffc-b576-943da592f72f` at 100%
  traffic; scaling mode automatic, minScale 1, 1 GiB, one instance. The
  environment keys are unchanged. `/api/health/live` returned 200 directly
  and through Hosting; the only warning was the expected 401 from an
  unauthenticated `/api/health/ready`.
- Firebase Hosting serves `main.css?v=d40302c3d1ccb0ad` with `no-cache`
  entry HTML. `/privacy/` and `/privacy/privacy.css` still answer 200 with
  the published policy, now from the Client source (`36d0cd78`). The sign-in
  page loads with no console errors; signed-in pages were not checked.

## Migrations applied

A private custom-format backup was taken first:
`local-backups/pulsartms-release-backup.yZbTvO/before-messaging-release.dump`
(20 MB, SHA-256 `796eb042…`, recorded in `local-backups/inventory.json`).
`pg_restore --list` read 629 entries including the migration history,
Users, Dispatches, ExecutionLegs, DispatchDocuments and integration
settings; no restore was rehearsed. The new revision applied, at start-up
and before traffic, the eight reviewed migrations
`AddConsistencyJournal` through `AddFiledDriverFiles`;
`__EFMigrationsHistory` holds 67 rows. All create new tables or add to
them; `DispatchDocuments` gained two nullable columns and two indexes.
The new tables hold no conversations, files, storage connections or
outbound messages.

## Production incident verified: AMF1399

Before: cancelled load 1399 had a planned leg marked for review, the only
cancelled load with a runnable leg. After: synchronization's owner path
moved it to `held` (revision 2) with the review reason. The auditor's first
pass (23:20:45 UTC) opened `execution.cancelled-source-runnable` before the
repair; its second pass (23:30:47) resolved it and opened the review
finding `execution.cancelled-source-held`. No cancelled load has a planned
or active leg.

## Preserved

- Users: 2 before and after.
- AMF1407: still `sent` with 2 stops; the stops' content hash is identical
  before and after. Its leg shows completed at 18:20:25 UTC, five hours
  before this release.

## Found by the auditor

`routing.planning-refresh-overdue` is open for one leg's refresh demand
(8 versions requested, 0 completed) and was requeued once through the
allowlisted owner action; it predates this release. The start and end of
one repair attempt are both recorded as `repair-requested` because the
outcome's name is `requested`: naming to clarify later, one attempt.

## Checks

On `9b32cba9`, run sequentially: `bash test.sh all` (Server 3518, Client
1096, JS 638) then `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` with the
offline UI smoke and the two-tab messaging probe. On the isolated
PostgreSQL fixture: the migration chain with its backfills, the read-marker
upsert, arrival ordering, the filing claim and the journal advisory lock
(Database category, 40). A post-migration query timeout seen once in that
fixture was not reproduced in five runs; cause unconfirmed, evidence in the
pinned `artifacts/managed/diagnostic-GQa50N`.

## Remaining

Not checked: signed-in pages in production, and whether Firebase Hosting
passes the messaging stream through live (the Client falls back to polling
every 30 seconds either way). External setup: Meta app review, business
number, tokens, webhook subscription and templates; a Cloud Storage bucket
and service account; the Google Drive OAuth client and Picker key. Existing
`DriverMessages` and load-document bytes were not moved.
