# Core rebuild phase 1K: historical connection ownership

Date: 2026-09-16. Status: implemented locally; full automated suite passed. No
deployment, schema migration or application-database experiment was performed.

## Findings and delivered behavior

Historical predecessor selection needs completed native work and unknown-start
evidence outside the remaining itinerary. Replacing it with only remaining
work would silently remove those guards. Its previous contract also returned
mutable Dispatch objects, and standalone candidate lookup and native hydration
did not share one explicit database read boundary.

DeadheadHistoryService now captures immutable load/stop facts and a content
signature. Candidate lookup, predecessor hydration and completed native-leg
hydration use one IExecutionReadScope. The reader retains existing bounded
legacy selection, completed-reference rules, ties, unknown starts and transfer
guards. Supplied current facts are copied before asynchronous reads;
compatibility projections create independent mutable objects for the existing
algorithms.

The PostgreSQL candidate query previously selected the current dispatch again,
whereas SQLite used the supplied current truck and start. PostgreSQL now seeds
its correlated top-two batch from parameterized captured ID/truck/date/time
arrays. This uses composable unmapped SQL queries and multi-array unnest: [EF
Core SQL queries][ef-sql], [PostgreSQL array functions][pg-arrays].
Translation is checked without connecting; real PostgreSQL execution is
untested.

The historical token includes financial inputs, schedules,
assignment/completion revisions, native identity, address readiness and
unknown-start evidence. Geometry keeps its narrower existing hash, so a
price-only refresh does not need a routing request. The capture is transient
input, not new accounting storage. MatchesAsync requires a fresh read and
rejects an older outer transaction. ReadLoadedAsync may intentionally
represent supplied facts differing from storage; its result does not certify
that those facts are current.

DeadheadHistoryPublication validates the captured signature inside the owned
protected write transaction. The source-table inventory now includes
DispatchDeadheads because stored previous-leg references affect
completed-history membership. The architecture check retains exact source/lock
closure and now covers both canonical and historical readers and publication
owners.

DeadheadService uses one transaction for retry reservation and initial
financial refresh, then another for final route mileage/geometry and
DispatchRates. Geocoding and routing calls run between them. DispatchRates
reuses the existing transaction while retaining standalone transaction
ownership for other callers. Changed history rejects publication.
Financial-write or commit failure rolls back the final route and financial
values together, leaving the committed retry reservation and preceding
financial mileage. Existing optimistic conflicts preserve the concurrent
result winner.

A failure-injection test exposed an additional rollback problem: EF retained
unsaved financial values after a failed final write. A later save on the same
context could persist those values despite the rollback. Publication failure
now detaches its owned route and rate entries. The regression verifies
rollback, another save and a repeated EnsureAsync without leaking the failed
result.

No formulas, public HTTP fields, Client files, tables or migrations changed.
Existing unrelated working-copy changes were retained.

## Verification

Added 22 server cases. The affected routing/fuel/dispatch/synchronization run
passed after the rollback correction: 2,128 Server.Tests, 663 Client.Tests and
64 JavaScript cases. The final architecture-owner case was then included in
the required full run, `bash test.sh all`:

- Server.Tests: 2,352 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,925, with no failures or skipped tests.

The stage changes 30 source, test and documentation files. Formatting,
whitespace and local documentation links are checked separately from behavior.

New cases cover:

- Deep isolation from mutable caller inputs and algorithm projections.
- Stable candidate ordering and historical changes outside the geometry hash.
- Native completion, handoff and manual completion compatibility facts.
- Completed native hydration sharing the candidate-read transaction.
- Changed native revision invalidating a previously captured token.
- Supplied current values differing from storage and changing during lookup.
- Endpoint, price, cancellation, new predecessor and unknown-start changes at
  the reservation or final publication boundary.
- Financial-write and commit failures preserving matching road/rate values,
  including later use of the same database context.
- Provider execution outside the publication transaction, successful matching
  rates and rejection of an older outer read transaction.
- Final historical validation staying inside the owned write transaction.

The initial affected run failed only on a date-display assertion in the
PostgreSQL translation test. The assertion now uses the date's invariant debug
format. The later rollback regression failed before the tracked-state cleanup
was added. Neither failure was addressed by weakening architectural boundaries
or skipping tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-4rPwZU/baseline.json`: pre-edit hashes for
  1,965 files.
- `artifacts/managed/diagnostic-uaq56I/affected-1.log`: date assertion feedback.
- `artifacts/managed/diagnostic-AqOEiV/affected-2.log`: first affected pass.
- `artifacts/managed/diagnostic-1UfkP7/rollback-before-fix.log`: reproduced
  tracked-state leak after financial-write rollback.
- `artifacts/managed/diagnostic-LlNeZP/affected-3.log`: affected checks after
  tracked-state cleanup.
- `artifacts/managed/diagnostic-sxWAbQ/full-1.log`: required full suite.
- `artifacts/managed/diagnostic-gxByGp`: 30-file change inventory, successful
  pinned CSharpier check for 24 C# files, whitespace and 133 local-link checks.

## Limits and next slice

ETA, next-load and fuel helpers consume immutable history, but ETA/fuel final
publication does not yet carry its historical token alongside the
remaining-work signature. This is the next dependency-propagation slice; the
whole core is not declared migrated. Standalone base-road preparation and
profile/settings, exchange-rate, telemetry and provider-price consistency
remain separate policies.

The protected PostgreSQL inventory now contains ten tables. The broad lock
serializes publications across trucks and can delay source writes. Replacing
it requires all source writers to own truck revisions, including inserted
legacy work and saved native-history references. No production throughput,
latency or narrower concurrency claim is made.

PostgreSQL execution/lock contention, live providers and authenticated browser
checks were not run. No suitable isolated PostgreSQL fixture was available; no
database server was started or installed. No migration was introduced or
applied. Accounting and driver settlements still need their own durable
evidence and versioned calculation rules.

[ef-sql]: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/whatsnew
[pg-arrays]: https://www.postgresql.org/docs/17/functions-array.html
