# Core rebuild phase 1A: shared work selection

Date: 2026-09-16. Status: locally implemented and verified.
No database migration, provider experiment or deployment was performed.

## Result

Per-truck saved route preview now reads immutable work references directly,
without GetDispatchBoardQuery or its detail enrichment. Dispatch Board uses
the same extracted selection logic, then projects references into its existing
index. The HTTP response shape and route/ETA/fuel formulas are unchanged.

The implementation reuses existing selection instead of creating a second
assignment algorithm. ExecutionWorkReader owns the extracted read rules;
TruckWorkSelection contains immutable load/leg/visit identities and assignment
revisions. DispatchWorkProjection adapts them to the existing Board index.

The original LoadRowsAsync implementation and its selection helpers were removed
from GetDispatchBoardHandler. RoutePreviewService.ForTruckAsync no longer calls
the Board handler. Fleet-wide preview and other consumers still do.

## Scope and semantic safeguards

The extracted reader supports the existing explicit overdue inclusion policy.
The per-truck preview preserves its prior exclusion of unstarted overdue work
so it cannot disagree with live planning merely because it migrated first.
Started overdue work retains existing behavior. Tests characterize both
policies.
Changing this policy requires a coordinated consumer change in phase 1B.

The new selection is not the complete canonical itinerary specified in phase 0.
It does not yet provide all accepted visit facts, a full input signature,
cross-load ambiguity reporting or a transactionally coherent input snapshot.
It adapts legacy Dispatch projections internally and retains existing native
StopsJson handling. These bridges are explicit remaining work, not normalized
storage or financial evidence.

Existing board-group invalidation and bounded read caching are reused. The
per-truck selection does not create a new provider polling or write path.
No distributed-cache freshness guarantee is introduced.

## Regression coverage

Four new ExecutionWorkReader integration tests cover:

- Distinct repeated visits and independence from Board search filtering.
- Explicit inclusion of unstarted overdue work and exclusion after delivery.
- Separate legs of one load, assignment revisions and immutable old snapshots.
- Number-only truck assignment resolution without tracked database writes.

One new saved-preview regression preserves the unstarted-overdue scope used
by live planning. The existing provider-free preview test now additionally
asserts that no Board request is sent for the per-truck reads.

In that SQLite fixture, cold preview database reads changed from seven to six;
the repeated warm read changed from one to zero. Exact assertions were updated
to these observed counts. These are query-count observations, not production
latency or scalability claims. Existing geometry mutation isolation still
passes.

## Verification

Final command: `bash test.sh all`, exit code 0.

- Server.Tests: 2,189 passed, zero failed or skipped.
- Client.Tests: 1,012 passed, zero failed or skipped.
- JavaScript: 561 passed, zero failed or skipped.
- Total: 3,762 passing tests, including architecture checks.

An initial routing run detected the changed cold query count. The first full
iteration also detected the changed warm count and a new test comparing
immutable
array backing identities rather than element sequences. Assertions now verify
the intended query counts and values; no architectural check was weakened.

Changed C# files were formatted with pinned CSharpier 0.30.6. Scoped whitespace,
documentation links and preservation of unrelated baseline files were checked.
No Client source or contracts changed. No separate browser/release gate ran.
PostgreSQL concurrency, physical schema, production load and performance were
not checked. No migrations were authored or applied.

## Next bounded slice

Phase 1B: characterize cross-load order and ambiguity, formalize a complete
itinerary contract, and select the next read consumer. Use repeated visits,
native/legacy transitions, overdue work and transfer dependencies as fixtures.
Unify consumer scope before changing current-load selection.

Keep normalization, coherent snapshot reads and removal of Dispatch projection
bridges visible as unfinished gates. Do not claim the entire first canonical
reader or operational core is complete after this selection extraction.

See the [core specification][spec] and [scenario catalog][cases].

[spec]: ../../architecture/core-rebuild.md
[cases]: ../../architecture/core-rebuild-scenarios.md
