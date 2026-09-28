# Server candidate — September 28, 2026

The integration candidate for the next server release: the application
audit (`claude/audit-d1-d2`) and the current-work follow-ups
(`claude/current-work-design`) on top of the released `0e6add5d`.
Nothing here is released. It records what the candidate contains, what
was checked, what must still happen before publication, and the exact
cutover. Evidence is in the audit worktree's `artifacts/managed` unless
a run is named from this worktree.

## Candidate

- Branch `claude/server-candidate`: merge `b9c6cc74` (the audit branch at
  `f39f9688` with `claude/current-work-design` at `29df3561`), the audit's
  documentation-only `46a7cace` merged after it, and this record. It
  contains the released `0e6add5d` and `main` (`66f8801e`, an ancestor;
  `main` does not yet contain the release).
- 72 commits over `0e6add5d`, this record included, merges not counted.
  The designer's branch is not part of it.
- A later change to either branch makes another candidate: affected
  checks again, then the gate.

## What it changes

- Messaging (audit F27 and the release overlap): delivery statuses that
  arrive before their provider id are kept and applied
  (`EarlyDeliveryStatuses`), reconciled when a previous binary saved the
  id (`KeptStatusReconciliation`), reported (`messaging.kept-status-unapplied`,
  `messaging.accepted-without-status`); a webhook status advances its
  conversation's revision (F29); in production each revision sends
  nothing to the provider until a deployment operator releases it
  (`SendHold`, `api/diagnostics/sends`).
- Dispatch imports ordered by database read tickets (F21); carrier
  natural keys (F28); road preparation says why it waits (D1); the
  carrier roster cached (D2); HOS provider holds nothing (D3); fuel
  hand-overs neither block plans nor go unrecorded (D6).
- Stop geocodes in their own byte-bounded memory with an attempt limit
  (F26); fuel-plan cost terms with one owner, no figure changed (F25);
  a skipped fuel-import message recorded and reviewed (F20).
- Users' theme can be unchosen (empty), so the Client can default to
  dark; saved choices unchanged.
- Architecture: the API names Application alone; Infrastructure reaches
  Application only through contracts (IL check); every background
  operation reports progress (report only, never a restart).
- Current work: ETA memory bounded by idle time and count, an ETA scope's
  writes and eviction in one lifecycle, the forecast upsert refuses
  another carrier's row, current-work auditor rules.
- Client: one file (`MessagingSignals`, a tab keeps its mailbox across
  a rejoin); compatible with the released API, so the frontend can go
  before or after the API, or not at all with this release.

## Pending migrations (production is at 74)

| # | Migration | Change | Previous binary (`0e6add5d`) on it |
| --- | --- | --- | --- |
| 75 | `RecordDispatchReadTickets` | table, column default 0 | run (diagnostic-vdc2U5) |
| 76 | `ScopeCarrierNaturalKeys` | two primary keys start with `CompanyId` | run with one carrier; no second carrier while it can run |
| 77 | `CountDispatchImportWrites` | column on the new table | run (vdc2U5) |
| 78 | `KeepEarlyDeliveryStatuses` | table | not mapped |
| 79 | `LetThemeBeUnchosen` | `Users.Theme` default `''` | run (diagnostic-CHECIp) |
| 80 | `RecordSendReleases` | table | not mapped |
| 81 | `RecordFuelImportSkips` | table | not mapped |

All are applied before the API, as one EF idempotent script (74 to 81)
in one transaction with a 5 s lock timeout; the reset guard names 81.
None needs a Down for an API rollback. 76 takes an exclusive lock on
two small tables.

## Configuration

- `appsettings.Production.json`: `Messaging:SendHold:RequireRelease`
  true (in the image).
- Deployment environment, new and required before any release can be
  given: `Operations__Operators__0` (and further indexes) - the identity
  ids of the deployment's operators. Which identities is the owner's
  decision. None set means sends stay held until it is set and a new
  revision deployed.
- `K_REVISION` is set by Cloud Run; without a valid one sends stay held.
- API memory and instance limits unchanged.

## Checks

| Check | Result |
| --- | --- |
| Per-change focused tests and mutations | recorded with each commit and in the audit document |
| `bash test.sh all` on `b9c6cc74` (every group, this worktree) | exit 0: Server 4,039, Client 1,301, JavaScript 669, none skipped (PostgreSQL tests ran) - diagnostic-pBm5Ba in this worktree; the docs-only merge after it changes no code |
| Release gate `verify-release.sh` (strict builds, artifacts) | not run - after the correctness review, on the frozen final tree |
| Browser checks `uiSmoke`, `messagingTabsSmoke` | not run - part of the gate |
| PostgreSQL fixture tests | run in the groups where the fixture is recorded (none skipped) |

## Decisions prepared (read only, 2026-09-28)

**Operator identities.** Production has three accounts: two Admins of
`amfcarrier` and the Meta reviewer, a Dispatch user of
`meta-review-demo`. The Operator policy needs Admin as well, so the
reviewer can never be one. Proposed: the owner's own identity only -
the person who deploys and proves the drain - set as
`Operations__Operators__0` on the new revision; the second Admin only if
the owner wants a second person able to release. The identity ids are
given to the owner directly, not recorded here.

**Where the business number's webhooks go** (diagnostic-uGFNee in this
worktree: the Cloud Run request log for `/api/webhooks/whatsapp/{key}`
over four days, query strings dropped, and the production messaging
timeline read in a read-only transaction):

- 2026-09-25 00:35 the demo company's URL was verified; from 00:36 the
  provider posted to `meta-review-demo` (44 requests that day), until
  2026-09-26 00:36.
- 2026-09-26 01:22 the `amfcarrier` URL was verified again; since then
  every post went to `amfcarrier` (27 on the 26th, 3 on the 27th, the
  last 2026-09-27 10:58), none to the demo company.
- The database agrees: every `amfcarrier` reply from 2026-09-26 on is
  delivered; the 12 replies still "accepted" were all sent 2026-09-25,
  while statuses went to the demo company. Both companies use one
  business number.

So, as of the last webhook, statuses reach production under
`amfcarrier`. The log shows where the provider sent, not its current
setting: a change after 10:58 on the 27th shows only with the next
webhook. Step 0 of the cutover is therefore a read of the next
`amfcarrier` webhook in the log, or the owner reading the setting in
Meta's dashboard; nothing in this plan changes it.

Expected after the release: `messaging.accepted-without-status` will
list those 12 replies of 2026-09-25 - delivery unknown, from before the
release, not caused by it.

## Before publication

1. Root's correctness review of the commits still under review
   (`53e83d86`, `6972e1af`, `dca3d714`, `f39f9688`, the merge) and of
   this record.
2. Owner: the operator identities for `Operations:Operators` (proposed
   above); the webhook destination read again just before the switch.
3. The release gate on the frozen tree, once.

## Cutover

1. Backup to `local-backups/`, protected counts read only.
2. Apply 75-81 (script above); verify 81 migrations and the new tables.
3. Build from a clean worktree of the final commit; deploy the revision
   without traffic, with the same flags and `Operations__Operators__*`;
   verify its identity and image digest.
4. Move traffic. The new revision holds its sends: replies queue, a fuel
   send answers 503. `GET api/diagnostics/sends`: its revision name and
   held.
5. Prove the drain, read only: the previous revision `Active` false,
   `TrafficShutDown` true, its last request log line older than the
   move.
6. A deployment operator calls `POST api/diagnostics/sends/release`;
   `GET api/diagnostics/sends` shows it released; the outbox sends the
   queued replies (`api/diagnostics/background`).
7. Two minutes later: `messaging.kept-status-unapplied`,
   `messaging.outbound-overdue` and the two fuel hand-over rules have no
   finding; `/api/health/live` 200; readiness not degraded.
8. Thirty minutes later: `messaging.accepted-without-status` lists no
   message sent around the switch.
9. After release: the incident reads named in the audit (loads 1341 and
   1355 with D1's reasons; the 30 road requests of F23).

Rollback: redeploy the previous image; no Down. The previous binary has
neither the early statuses nor the send hold, the ticketless import
returns (corrected by the next binary's first pass), and no second
carrier may exist while it runs.
