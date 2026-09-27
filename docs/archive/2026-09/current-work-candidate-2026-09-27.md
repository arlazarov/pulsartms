# Current-work candidate — September 27, 2026

The publication checklist for branch `claude/current-work-design`
(stages 1 to 4e of [current-work.md](../../architecture/current-work.md)).
Nothing here is released. It records what the candidate contains, what
was checked, what must still run before publication, and what stays
open.

## Candidate

- Code at `bc29aac9`, this record in the commit after it; it contains
  `main` at `66f8801e` (main is an ancestor, no merge pending). A later change to `main` or this branch
  makes it another candidate: affected checks again, then the gate.
- 21 commits over `bb5e46f6`: stages 1, 2a-2c, 3a-3b, 4a-4e, the rule
  edits, and the stage 4e follow-ups `884f6da2` (ETA display read),
  `187b0dc2` (saved fuel inputs shared per refresh, duty staleness) and
  `bc29aac9` (populated chain across processes, duty message, price
  refresh measured).

## Before publication

1. Root's review of `884f6da2`, `187b0dc2` and `bc29aac9` and their
   evidence runs (below).
2. Full gate on the frozen tree: `bash verify-release.sh` (all automated
   tests, strict builds, staged artifacts). Client changed, so the
   strict Client build is part of it. No edits or builds while it runs.
3. Browser gates that are maintained: `uiSmoke` and `messagingTabsSmoke`,
   on localhost.
4. PostgreSQL fixture tests run in the gate (recorded fixture
   `pulsr_core_fixture`, own schema per run); report them as run or
   skipped, never assumed.
5. Migration `RecordEtaForecastWork` (two nullable varchar(64) columns on
   `DispatchEtaForecasts`) applied **before** the API that writes them:
   the forecast upsert names the columns and fails without them. It is
   additive; the code before it ignores them, so rolling back the API
   needs no down migration. The reset guard names it (74 migrations).
6. API memory and maximum instances unchanged. Deployment only on an
   explicit request.
7. After release, a bounded read-only check through the normal owners:
   one truck's summary shows the saved forecast on a process that did
   not refresh it; a duty change shows the duty message; Completed tab
   counts match the owner.

## Checks performed (evidence in `artifacts/managed`, pinned)

| Change | Checks | Runs |
| --- | --- | --- |
| 4e ETA read | two processes: cold, warm, relay, raced load, refused older save, tie, keyless rows, board part; PostgreSQL keys upsert; mutations | `diagnostic-2iHeau`, `-ELz6pr`, `-xq7VyS`, `-pmmNHx`, groups `-XP0n6K` |
| Fuel check shared | caller costs; share unchanged, announced, unannounced, during check, other history, sequential, overlapping, nesting; operation holds a share; mutations | `-LV6ESc`, `-t4xIZF`, `-Qjnrzy`, `-9Gm8f7`, `-3DwExV`, `-iVuvZA`, groups `-mcbRP8`, `-txS0ve` |
| Duty staleness | reader, cache interleaving, domain rule, message, Client contract; mutations | `-l4Enma`, `-226JZg`, client-duty run |
| Populated chain | real refresh with hours and position, other process, board | measure-and-chain run, groups `-JG2z1M` |

Invalid or superseded runs are marked in place (`lOgXM9` stale build,
`Xm4V7y` test draft, `ImFvgB` script error, a first Client test draft,
`pdfqU4` a full run stopped unfinished). The full suite has not been run
on this candidate.

## Bounded memory and subscriptions

What the branch adds or changes that holds state, and what bounds it.

| State | Owner | Bound | Freshness |
| --- | --- | --- | --- |
| Prepared summaries | `PlanningSummaryCache` | 256 entries, 512 KiB each, 8 MiB | 30 s, commit notice, ticket and lease |
| Saved forecasts for display | `ReadCache`, family `eta-forecast` | shared 16 MiB read budget, 120 s | invalidated after commit, relayed |
| Captured planning inputs | `ReadCache`, `planning-inputs` | same budget | invalidated after commit, relayed |
| Item generations (incl. `fuel-saved-inputs`) | `CacheGenerations` | 4,096, least recently used; an evicted key starts at a new epoch, so a comparison fails safe | per commit |
| Batch gates | `ReadCache.BatchGate` | 64 semaphores per family constant | process lifetime |
| Shared fuel check | `FuelSavedInputsValidation.Share` | one per DI scope, entries per saved plan checked in one refresh | disposed with the operation |
| ETA memory | `EtaMemory` (unchanged here) | not bounded by count; `eta-current` reported unmeasured | pre-existing gap |

Client: no new event subscriptions, timers or retained collections;
map conflicts are replaced with each response. The sustained navigation
run (Cards, Papers, Table, Map, Messenger in a loop, heap and server
memory at start and end) required by current-work.md has not been done;
it belongs to the browser part of the gate or stays an explicit gap.

## Open, with owners

- Loads 1403 and 1385: source closed, execution leg active; they leave
  the Completed tab after 4b. Recovery: Root.
- Keyless saved forecasts (every row before the migration): hidden from
  the display read until each truck's next committed refresh; how soon
  is not measured. Owner: this branch, checked after release.
- Auditor rules CW1-CW4 are not implemented (current-work.md).
- Map switch reset is unobservable (a surviving mutation); no call-count
  test on `WorkPlacements`.
- Production costs are not measured; statement counts are SQLite
  fixture counts.
