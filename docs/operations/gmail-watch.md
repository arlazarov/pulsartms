# Gmail watch operations

## Explicit ownership and activation

An administrator registers application ownership with authenticated
`POST /api/fuel/gmail-watch/start`. This stores registration before requesting the
watch. A failed request returns 503 and keeps a bounded automatic retry schedule;
an active maintenance lease returns 409. No registration means no automatic Gmail
watch or recovery requests. Deployment alone does not register or renew a watch.

Before registering an existing mailbox, coordinate ownership with whoever manages
its current external renewal schedule. Retire that schedule only after successful
application registration and verification. Do not run competing renewal owners.
Local development must use an isolated database and test mailbox; a database with
an existing registration authorizes background work for its configured mailbox.

`FuelDiscounts:Source` selects where fuel discounts come from: `bvd-gmail` (default)
imports BVD price files from the configured mailbox, `none` installs no import and no
Gmail worker for customers without a fuel card. Any other value stops startup.
`Gmail:BackgroundMaintenanceEnabled` controls only the automatic Gmail renewal and
notification-recovery worker of the `bvd-gmail` source. It defaults to `true`; `appsettings.Development.json`
sets it to `false` so a local API does not compete for an existing registration.
Environment overrides use `Gmail__BackgroundMaintenanceEnabled`. The setting is
read at startup, so changes require restarting that host. Manual Admin watch/import
endpoints and authenticated push validation remain available and unchanged.
Missing credentials do not silently disable maintenance when it is enabled.
This switch is defense-in-depth, not permission to share a production database or
mailbox with development; other workers and explicit requests are unaffected.

All environments require `Gmail:ClientId`, `Gmail:ClientSecret`, and
`Gmail:RefreshToken` in secrets/configuration. Obtain OAuth consent and the refresh
token out of band with Gmail readonly scope. The server never opens an OAuth
browser, reads local credential files, or prompts interactively. Missing or revoked
credentials remain failures; they do not mark the watch healthy. After repairing
credentials, an administrator can start again to renew immediately.

External requirements remain operator-owned: Gmail API access and consent, the
configured mailbox/label, topic publish permission for Gmail, authenticated Pub/Sub
push, correct audience/service account/mailbox validation settings, and a hosting
configuration that actually runs background operations while idle. The watch adapter
currently uses topic `projects/amftms/topics/gmail-fuel-notifications` and label
`Label_7441158285664312766`; verify they belong to the intended mailbox/project
before registration. No application test provisions or verifies these resources.

## Schedule, failure budget, and recovery

Google recommends daily watch renewal and requires renewal within seven days.
The returned expiration is stored as UTC, with the history ID, last success times,
retry counts, and error type codes. Renewal is scheduled daily or six hours before
expiration, whichever is earlier, with a minimum 15-minute interval.
See [Google's push notification guide](https://developers.google.com/workspace/gmail/api/guides/push).

The worker checks once per minute. Each operation gets a unique lease owner on a
dedicated row in the existing SynchronizationCheckpoints table (no new schema).
The three-minute lease bounds stale ownership; provider/import work has a linked
90-second timeout. State writes reject lost or expired owners. Retry timing is
persisted before work starts, so a crash or restart cannot immediately replay a
failed call. Failure delays are 15, 30, 60, 120, 240, then at most 360 minutes.
Checkpoint failures are reported at the worker boundary and delay the next check
by five minutes. Expected cancellation is not logged as a failure.

Because Gmail notifications may be delayed or dropped, registered mailboxes also
run the existing idempotent fuel email import every two hours. Recovery has its own
persisted retry budget and keeps email/attachment transaction and deduplication
rules. This is a bounded catch-up, not history replay: the Gmail reader searches
only the last two days. An outage longer than two days needs explicitly selected
historical import/repair; restarting a watch cannot recover that gap by itself.

Monitor maintenance failure logs and stored expiration/last-recovery timestamps.
The generic liveness endpoint does not certify a valid Gmail watch. Investigate a
stale or expired watch, revoked credentials, Pub/Sub delivery failures, and import
failures separately. Successful renewal verifies Gmail acceptance, not end-to-end
notification delivery. Live acceptance and delivery must be verified by an
authorized operator; no provider calls or production changes are part of local tests.
