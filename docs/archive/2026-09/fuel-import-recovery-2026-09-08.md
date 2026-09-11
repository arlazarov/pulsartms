# September 8 fuel import recovery

Deployment follow-up: the [September 9 release](fuel-eta-release-2026-09-09.md)
records the deployed safeguard and post-deployment recovery checks.

## Confirmed incident

The September 9 USD price file arrived on September 8 at 19:10 and 19:23 UTC,
and the CAD file at 18:24 UTC. The USD CSV's effective date was September 9.
Mail labels, external CSV attachments and the existing OAuth credentials were
valid. Production had no September 9 USD prices and no Gmail lifecycle
registration checkpoint. No September 8 push requests appeared in Cloud Run
request logs. The precise expiration of the preceding watch was not recovered;
missing application renewal/recovery ownership was confirmed.

The local API used the production database without local Gmail credentials.
It was stopped before activation so it could not acquire the same maintenance
lease. This is not a permitted isolated development database configuration.

## Recovery performed

After the owner confirmed application-managed renewal, the administrator signed
in through a temporary local recovery form. It sent credentials directly to the
production HTTPS login endpoint and used the resulting in-memory bearer token
with the existing Admin-protected endpoints. No authentication policy was
changed, no token was fabricated or exported, and no credentials were persisted
by the form. The temporary HTML/module were removed after completion.

Production revision `amftms-api-00079-xkx` returned HTTP 200 for
`POST /api/fuel/gmail-watch/start` at September 9 03:33:03 UTC and
`POST /api/fuel/import` at 03:33:04 UTC. A genuine Gmail Pub/Sub notification
completed with HTTP 200 at 03:32:57 UTC. Initial concurrent lookup/import attempts
returned 503 before the import completed; checkpoint-creation races also appeared
in EF logs. This record does not claim those logging/concurrency issues were fixed.

Read-only production verification found:

- 605 USD and 91 CAD price rows effective September 9.
- All three newly received email IDs recorded by the idempotent import.
- 602 USD and 87 CAD rows with coordinates; importing prices does not guarantee
  every station can be shown on the map.
- Watch expiration September 16 03:33:03 UTC and next renewal September 10
  03:33:03 UTC.
- The first background recovery attempt failed while concurrent import/lookup
  requests were running and reserved its existing 15-minute retry for September 9
  03:47:11 UTC. Its successful
  retry and the following two-hour recovery interval were not yet observed.

No image was deployed and no migration was applied. The existing server performed
the import, transaction/deduplication and cache invalidation. The local API remains
stopped pending a safe restart; other local processes and production remain running.

## Prevention and verification

Infrastructure now supports explicit `Gmail:BackgroundMaintenanceEnabled`, default
true. Development configuration sets it false. Only the Gmail hosted worker is
affected; manual Admin operations, notification validation and all other service
registrations remain unchanged. The safeguard takes effect at host startup and
does not authorize sharing a production database for development or tests.

`bash test.sh fuel` passed 405 server, 108 Client C# and 18 JavaScript checks before
the safeguard. After the DI change, `bash test.sh all` passed 751 server, 401 Client
C# and 178 JavaScript tests, including five new registration regressions. No real
PostgreSQL test fixture was used; operational read-only production verification
was not a database test. Production performance was not benchmarked.

An overly broad initial Cloud Run audit-log diagnostic exposed deployment
configuration secrets to tool output. The owner was notified. Later diagnostics
restricted logs to application/request streams and projected non-secret fields.
Credential rotation was not performed and remains a separate coordinated action.

## Follow-up during release verification

At September 9 03:47:11 UTC the scheduled background recovery completed
successfully. Both error fields cleared and the next recovery was scheduled for
05:47:11 UTC, confirming the existing two-hour catch-up interval without another
manual import. September 9 USD/CAD row counts remained 605/91.
