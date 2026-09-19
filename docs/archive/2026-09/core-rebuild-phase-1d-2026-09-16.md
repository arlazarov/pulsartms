# Core rebuild phase 1D: complete work reads and snapshot consistency

Date: 2026-09-16. Status: locally implemented and verified.
No deployment, migration or application-database experiment was performed.

## Delivered boundary

TruckItineraryReader and the internal GetTruckItineraryQuery expose an immutable
per-truck operational snapshot. The query takes a truck and an explicit as-of
instant, without Board paging, search, overdue exclusion or ETA depth limits.
It is registered through the existing Application/Infrastructure composition.
No HTTP endpoint or Client contract was added.

The reader reuses ExecutionWorkReader's selection batch and ExecutionLoads'
native hydration. It does not duplicate native assignment selection or rehydrate
the same native snapshots. The lightweight callers retain their existing scope.
Legacy full visit facts are loaded after candidate selection in the same database
snapshot.

The result includes:

- Remaining current/planned assignments, including overdue work and native
  successors beyond the existing ETA cutoff.
- Truck resource configuration, segment assignments and revisions.
- Stable visit identities, resolved operations, locations and verification
  timestamps, appointments, actuals and completion revisions.
- Visits excluded from a continuous truck path, marked explicitly instead of
  removed from the result.
- Typed missing-visit, assignment, path, source-review and missing-transfer
  problems, plus full-scope precedence assessment.
- Native accepted/observed source signatures and review reasons.
- Load-link sequence, status, truck, assignment revision and boundary visit IDs,
  including completed predecessor references.
- Transfer boundary identities, planned/actual times and independent release
  and receipt confirmation. Confirmation with unknown actual time remains valid.

Completed/cancelled work does not become remaining work. Empty inactive trucks
return an empty snapshot; unknown trucks return no snapshot. Orphan database
rows and work without a truck association are outside this per-truck contract.

Invalid native JSON, empty/duplicate visit lists and null entries retain the
assignment with a missing-visits problem. The shared snapshot parser now reports
a null visit as malformed JSON so the existing guarded reader can handle it.
No malformed snapshot is rewritten or silently repaired.

## Consistency and revalidation

IExecutionReadScope owns the consistency boundary. Its Infrastructure
implementation selects PostgreSQL repeatable-read or SQLite serializable
isolation. All selection, hydration and dependency queries use the same scoped
context and transaction. Unknown providers and weaker caller transactions are
rejected rather than read with a weaker guarantee. Ordinary nested reads can use
a compatible outer transaction without committing or disposing it.

InputSignature hashes the projected facts, memberships, configuration, revisions
and dependencies. Legacy changes that do not increment a revision still change
the signature when the projected fact changes. The result contains immutable
values and never exposes tracked entities, raw provider payloads or mutable
Dispatch screen models.

MatchesAsync requires a new database snapshot. It rejects an already-open
transaction so an older transaction cannot certify that its own stale view is
current. Failures and cancellation release owned transactions. Reads neither
save nor discard pending tracked edits.

AsOf supplies the UTC date for overdue classification, not historical database
reconstruction. The content signature excludes the clock instant itself. An
otherwise unchanged result within the same day therefore retains its signature.

A successful comparison is a point-in-time check. It does not lock out a later
change or replace atomic revision checks at result publication.

## Regression evidence

Added 21 server cases covering complete selection, excluded visits, conflicting
assignments, native continuation and completed boundaries, malformed snapshots,
unknown confirmed times, missing transfer records, signature stability and
invalidation, membership/configuration changes, unsaved tracked edits, inactive
and unknown trucks, nested transaction ownership, weaker isolation, failure
cleanup, cancellation and rejection of stale-snapshot revalidation.

Extended the transfer integration fixture to check complete-snapshot invalidation
after confirmation. The architecture guard now includes TruckItinerarySnapshot.
Existing DI composition validation resolves the new reader, handler and scope.

Affected-category verification passed before the final fresh-revalidation case:
1,184 server, 625 Client and 52 JavaScript tests.

Final `bash test.sh all` completed with exit code 0:

- Server.Tests: 2,236 passed, zero failed or skipped.
- Client.Tests: 1,012 passed, zero failed or skipped.
- JavaScript: 561 passed, zero failed or skipped.
- Total: 3,809 passing checks, including architecture checks.

Pinned CSharpier, scoped whitespace, documentation links and file preservation
against a pre-edit hash baseline were checked. Managed local evidence is pinned
under `artifacts/managed/diagnostic-ExwW3L`.

No PostgreSQL fixture was available. PostgreSQL execution/isolation behavior,
concurrency under load, production performance and restore behavior were not
tested. SQLite checks verify the local transaction contract and data behavior;
they do not certify PostgreSQL. Browser and release checks were not run. No
migrations were authored or applied.

## Remaining work and next slice

Existing ETA, preview and live planning are not yet consumers of the complete
snapshot. The shared phase-1C evidence now carries richer boundary/transfer facts,
but this does not make its existing multi-query ETA description transactional.
This stage delivers the new read boundary and internal query, not a production
consumer cutover or a complete core rewrite.

Next, move ETA preparation onto the snapshot without resolving assignments again
through mutable/cached reads. Keep its calculable prefix and unsupported native
continuation explicit, and preserve saved-route ownership and atomic publication
checks. Then align preview/live planning scope with the same contract.

Normalized visit storage, correction history, financial evidence and cross-process
publication remain separate work. Snapshot comparison is not an accounting or
settlement approval record.

See the [core specification][spec] and [acceptance scenarios][scenarios].

[spec]: ../../architecture/core-rebuild.md
[scenarios]: ../../architecture/core-rebuild-scenarios.md
