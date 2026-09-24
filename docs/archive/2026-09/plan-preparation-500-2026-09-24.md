# Two HTTP 500s on concurrent cold preparation (September 24)

Local only, on the isolated load fixture (`run.py start`, ten trucks, one
CPU, 1 GiB). Nothing was deployed.

## Reproduction

`exercise.py --prepare` on three fresh fixtures with the build at
`56d27706`. Each server log was saved before the fixture was stopped. The
evidence is pinned under `artifacts/managed`.

| Run | Evidence | Failed preparations |
| --- | --- | ---: |
| 1 | `diagnostic-1TXCDY` | 0 |
| 2 | `diagnostic-7fUZbV` | 0 |
| 3 | `diagnostic-9ZnGtM` | 1 of 10 (10.2 s) |

## Cause, from the saved log of run 3

1. The background planning worker was publishing truck 1 and held its row
   in `PlanningInputRevisions`.
2. The probe's preparation of the same truck asked for that row with
   `FOR UPDATE NOWAIT`. PostgreSQL answered `55P03`. EF Core logged the
   command and the query as failures, at error level.
3. `PlanningPublicationScope` translated the error, as designed, into
   "Planning inputs are being updated. Retry planning shortly.", with a
   retry time five seconds away.
4. `AutomaticPlanningService` returned that as the plan's message. It also
   remembered it for two minutes, as it does a failed provider, ignoring
   the retry time.
5. The probe's `PrepareAsync` treated any message as a failure and threw.
   `ApiExceptionHandler` answered 500.

The contention is expected: preparation and the background pass run
together at startup. The defects were the two-minute memory of a
five-second answer, the error-level logging of ordinary contention, and the
probe treating "retry shortly" as a failure. In the product the same memory
made a truck show "being updated" for two minutes after a moment of
contention.

## Change

- The error memory keeps an answer that names a retry time only until that
  time (`ARefusalWithARetryTimeIsNotHeldPastIt`).
- The truck lock uses `SKIP LOCKED`. When no row comes back, a plain read
  tells "held" (retry shortly) from "missing" (ownership changed). No
  database error, no failed command (`PlanningPublicationScopePostgresTests`
  on the isolated PostgreSQL fixture, which counts failed commands).
- Recording a hand-over waits up to ten tries, 500 ms apart, for a busy truck
  and is then refused with the retry answer
  (`AHandOverWaitsBrieflyForABusyTruck`). The message may already have been
  sent, so a moment of contention should not lose its record.
- The probe retries a "being updated" answer up to ten times, 5 s apart.

Each regression was checked by reverting its guard and seeing it fail.

## Also found, in the same area

A fuel recalculation checked in-flight WhatsApp attempts by creation time.
An attempt started before the calculation and accepted during it passed the
check, so the commit could drop a hand-over it never saw. The check now uses
the attempt's status time (`AnAttemptAcceptedDuringTheCalculationRefusesItsCommit`).

## After the change

A clean rerun alone would not prove the cause, because the failure
appeared in one run of three. The cause is shown by the log above and
pinned by the regressions.

The same reproduction on the fixed build (`5bf92f65` plus the display
reference change), three fresh fixtures:

| Run | Evidence | Failed preparations |
| --- | --- | ---: |
| 1 | `diagnostic-YRSFi9` | 1 (0.7 s) |
| 2 | `diagnostic-vzxLlq` | 0 |
| 3 | `diagnostic-1PucBb` | 0 |

Run 1's failure was a second probe-only path. Before planning, the probe
calls `BaseRouteService.EnsureAsync` directly. That call met the same
held lock and got the new "being updated, retry shortly" answer, which the
probe did not retry, so it surfaced as a 500. No database error was
logged, and the planning path did not fail. `6167f2a3` makes the probe
retry that answer on its base road and deadhead steps too. After that fix,
one more fixture (`diagnostic-ezKDoN`) prepared its ten trucks with all 37
requests passing (`diagnostic-sFJZV2`). That is one run, not a rate.

**Open:** a handler that lets this retry answer escape reaches
`ApiExceptionHandler`, which logs it as an error and answers 500. The API
layer may not reference the Domain exception, so it cannot map it there.
The planning handlers catch it and answer with a message. Not every
handler that reaches the publication scope was audited.
