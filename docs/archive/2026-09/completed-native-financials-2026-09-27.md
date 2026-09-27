# Completed native load mileage read

## Defect and owner

Completed history called DeadheadService with commercial load IDs only. Native
connections are stored by both load ID and execution-leg ID; the same records
were readable on the live board but not in Completed history.

GetDispatchQuery now calls DeadheadService.ReadCompletedAsync. It batches the
page's native sections through existing ExecutionRouteSections, reusing the
already-read commercial scalar values. Legacy rows retain their existing path.
Unambiguous native rows reuse the same saved-mileage validation and DispatchRates
formulas as the board. No schema, DTO, Client calculation or provider call is
added. No route geometry is loaded by the new section lookup.

Multiple noncancelled sections do not select an arbitrary leg or duplicate a
commercial load's loaded mileage. Their aggregate stays unknown pending an
authoritative multi-section financial policy. This is an explicit remaining
coverage gap, not a completed aggregation feature.

## Checks

The regression failed for all three native-successor variants before the fix
and passed afterward: completed history reads 100 empty miles, 200 total miles
and 0.5 total RPM from a saved native connection. An obsolete connection hash
subsequently produces unknown totals. Route-provider call counts do not change,
and reads leave no pending writes.

A two-load/two-section regression verifies unknown ambiguous totals and the
same query count for one versus two rows, with no road/history JSON projection.
This measures batching of the new lookup, not all production database costs.
There is no new cross-request cache. Background calculation remains unchanged;
no claim is made that its total cost has decreased.

- Focused reproduction: three native cases failed before the fix.
- Focused Completed/read and native-successor checks passed after the fix.
- `bash test.sh dispatch routing`: 2,050 server and 714 Client tests passed.
- JavaScript type check, Dispatch 16 and Architecture 67 checks passed.
- Tool build with warnings treated as errors passed.
- Full release gate and browser verification have not been run for this fix.

## Existing data and bounded verification

The updated application reader was used read-only against AMF for the first
100 Completed rows (324 total at observation time), without sending messages,
forcing routes or editing production records. Among 51 native rows:

- Eight returned valid saved totals, including AMF1408 and AMF1407.
- Three had saved miles rejected by current dependency validation:
  AMF1400, AMF1397 and AMF1384.
- Thirty-nine were pending with no saved connection row.
- AMF1388 was unavailable without a resolved connection.

AMF1408 returned 143.382 total miles and 4.533344 RPM; AMF1407 returned
482.665 total miles and 3.107746 RPM. These are results of the corrected local
reader against existing data, not evidence that the deployed API is fixed.
Source history import/completion can reveal a different predecessor; invalid
saved mileage must not be relabelled current. Missing historical preparation
is a data-recovery task, not another Client display fix.

The existing BaseRouteOperation owner can prepare native historical sections,
but its ordinary scan selects active/planned work. A one-time bounded historical
backfill requires authorized demand through that owner, fresh inputs and normal
version/commit checks. Do not force live route/fuel replans or reuse a stale hash.
No historical backfill was performed in this change, and no publication was
performed. The 12 recent Torque invoice-status repairs also await adapter release.

Runtime audit coverage: existing saved-connection hash validation rejects stale
financial reads. No runtime source-data auditor or historical backfill was added.
The authoritative aggregate policy, unavailable connection review and remaining
historical preparation are deferred to Routing/Dispatch owners, with completion
requiring valid results or explicit missing-input reasons after authorized repair.

Evidence: managed diagnostic run `diagnostic-mbAnaG` in the
`codex/dispatch-history-import` worktree. Actual screenshots and performance
under overlapping production consumers remain unmeasured.
