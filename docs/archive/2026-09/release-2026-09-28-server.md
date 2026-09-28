# Server release — September 28, 2026 (UTC)

The owner approved the release of the server candidate and chose the
owner alone as deployment operator; Root reviewed the final merge, F20
and the final gate. Candidate and cutover plan:
[server-candidate-2026-09-28.md](server-candidate-2026-09-28.md).

**Status:** migrations 75-81 at 03:04, API on traffic at 03:14, sends
released by the operator at 03:21. No message sent by the release; no
reset, replan or payment; Driver Pay untouched; memory (1 GiB) and
instances (minimum 1, maximum 1) unchanged. The frontend was not
released (optional; see Open).

## Package

`9af9a134` on `claude/server-candidate`: the audit branch and the
current-work follow-ups over the released `0e6add5d`. Final gate
`PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on the clean tree, exit
0: Server 4,039, Client 1,301, JavaScript 669, none skipped; UI smoke
`browser-ui-dYrbeT`, messaging tabs `browser-messaging-tabs-gq81E2`;
artifact `release-v69FuZ` (in the candidate worktree, pinned).

## Backup

`local-backups/pulsartms-release-backup.XNT4il/before-2026-09-28-release-9af9a134.dump`,
03:03:32-03:03:53: `pg_dump` 18.6 custom, `--no-owner --no-privileges`,
51,451,001 bytes, SHA-256
`5714cd77c8479e371ced77d06856d953e813bdff17758133d0a4b09bf31ee8b4`,
720 entries, 104 table data; in `local-backups/inventory.json`. No
restore rehearsed.

Protected data before, read only: migrations 74, users 3,
conversations 5, conversation messages 57, driver messages 0, fuel visit
sends 0, saved ETA forecasts 29, dispatches 418, number counters 1, HOS
readings 13, fuel imports 25.

## Migrations before the API

The EF idempotent script for exactly 75-81 (`--no-transactions`),
applied in one transaction with a 5 s lock timeout at 03:04:47-49 while
the previous revision served: 81 migrations, the four new tables
present, `Users.Theme` default `''`, users, messages, dispatches,
counters and HOS readings unchanged. The previous revision logged no
application error afterwards; its only 5xx were the known 503 refusals
of `/api/messaging/changes`.

## API

- **Before:** generation 282, 100% on
  `amftms-api-b-13babfb4-d0f4-4895-a641-d46360b95909` (`0e6add5d`).
- **Build:** Cloud Build `ee182a39-6170-45a5-849f-a4d2a8967a2d` from a
  clean worktree of `9af9a134` (03:05-03:13), SUCCESS, image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:10f2fe032bc7b58b9a6ddbb532323c0a146acd016f8654104248358395771547`.
- **Deploy:** the steps of `deploy-server.sh` with the same flags, the
  revision `amftms-api-b-ee182a39-6170-45a5-849f-a4d2a8967a2d` without
  traffic, plus `--update-env-vars Operations__Operators__0` (the
  owner's identity); its 17 existing variables kept. Identity and digest
  verified by `scripts/cloud-run-release.mjs revision`.
- **Webhook destination, just before the move:** the latest webhook post
  was 2026-09-27 10:58 to `amfcarrier`; none since, none to the demo
  company.
- **Traffic:** moved at 03:14:18-31, verified by
  `cloud-run-release.mjs traffic`; generation 284, 100% on the new
  revision.
- **Drain:** the previous revision served its last request at 03:14:29
  (planning reads, no webhook), `TrafficShutDown` true at 03:14:30,
  "Shutting down user disabled instance" at 03:14:38, `Active` false
  (Retired) at 03:14:47.
- **Release of sends:** the operator's `POST
  /api/diagnostics/sends/release` answered 200 at 03:21:27; one
  `SendReleases` row for the revision, released by the operator. No
  reply was queued meanwhile; no kept status.
- **Health:** `/api/health/live` 200; synchronization took its lease at
  03:15:30; the load import and planning run. The new revision's only
  5xx were the same 503 refusals of `/api/messaging/changes`.

## After, read only

- 03:22 open findings: `messaging.accepted-without-status` 12 (the
  replies of 2026-09-25, as expected); `routing.source-road-overdue` 30
  (F23); the current-work rules `execution.source-closed-work-open` 2,
  `execution.source-review-open` 1, `routing.route-passed-work-open` 2;
  `execution.cancelled-source-held` 1 (since 2026-09-23). None for
  `messaging.kept-status-unapplied`, `messaging.outbound-overdue` or the
  fuel hand-over rules.
- D1 now names the wait of loads 1341 and 1355: both completed, both
  waiting on `road-provider` (attempts 117 and 69), retried every five
  minutes - the F23 class.
- No message was sent across the switch, so the thirty-minute check of
  `messaging.accepted-without-status` has nothing new to find.

## Open, with owners

- `driver-hos` fails every minute on a carrier without a Samsara token
  (since 2026-09-27 23:00, before this release) and logs each time.
  Owner: Fleet.
- Completed loads waiting on the road provider (1341, 1355 and the 30 of
  F23). Owner: Routing, by a decision on completed legs.
- The frontend with `MessagingSignals` (a tab keeps its mailbox) is not
  released; the designer's separate frontend is based on `0e6add5d` and
  lacks it. Owner: the owner, for the order of frontend releases.
- `main` still does not contain `0e6add5d` or this release.
- 11006 recovery remains open.
