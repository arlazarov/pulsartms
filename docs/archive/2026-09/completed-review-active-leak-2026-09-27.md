# Completed source review leaked into Active

AMF1309 was stored as completed, had no native execution leg and still appeared
in the normal AMF Current board. The source link retained an assignment review.
ExecutionWorkReader's conflicting-actuals branch bypassed
ExecutionWorkRelevance entirely, including its terminal-status exclusions.

The shared Domain relevance owner now accepts the conflicting-actual review
flag after cancellation, native execution and source-completion checks. Open
ambiguous work remains visible; a historical review cannot reopen a completed
or cancelled source-only load. Native active/planned authority is unchanged.
Dispatch and itinerary consumers keep the same shared reader. No query, cache,
provider call, assignment update or historical data mutation was added.

A three-case regression failed for completed/cancelled and passed for assigned
before the fix. A prior import test expected completed invalid actuals to appear
in the live itinerary; this contradicted the user's Active/history distinction.
It now requires an empty live itinerary for that case while retaining workspace
review visibility and the guards against invented execution/planning changes.

A bounded read-only check with the corrected local owner excludes AMF1309 from
Current while preserving its completed status and source-review explanation.
This is not evidence of deployment: the published API still needs this change.

Runtime auditor coverage for review-driven terminal-work leakage is deferred
to the execution owner: completion requires a bounded source-only terminal row
versus live-selection check. Existing review reasons must remain accessible in
load details/history. Local reader/import regressions cover prevention; after
an authorized release, verify AMF1309 using the normal board owner, without
rewriting statuses or accepted history. No migration or Client change.

Focused completion checks: 157 server reader/import/relevance/architecture
cases passed, zero skips (six seconds test execution). Client JavaScript
architecture checks also passed. Full release/browser gates were not run;
per the user's rule those belong to the final publication candidate.
