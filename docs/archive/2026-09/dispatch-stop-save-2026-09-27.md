# Dispatch stop save incident — September 27, 2026

## Scope and invariant

The user reports pickup state and rejected manual saves for trucks 11005
and 54777, plus blocked Discard and leave. Root owns this incident separately
from Claude's application audit. No production data has been changed.

DispatchDetails owns navigation protection. Explicit discard must permit
navigation from every editor; Keep editing must retain the draft. No database
or provider work is needed for this UI invariant. Runtime business auditing is
inapplicable to navigation; component regressions cover the interaction.

## Local navigation correction

Leave cleared metadata/activity/document flags but retained correction,
operation, transfer and stop drafts. Clear those navigation guards on explicit
discard. Two component cases (stop status and operation) failed on the original
code at the final navigation assertion, then passed after the correction.
The full DispatchWorkspacePageTests class passed: 21 tests, zero skips.
Client compiled as part of that run. No full release gate or deployment run.

## Bounded production observations

At investigation, 11005 AMF1410 has a planned native leg at revision 2,
without pickup actuals. Its prior AMF1403 native leg is still active at revision
7, with pickup actuals but no delivery, although its source load is completed.
Resource conflict is a hypothesis requiring owner-level confirmation.

Cloud Run request logs show five correction requests for AMF1410 between
10:43:29 and 10:43:39 UTC, all HTTP 409.

54777 dispatch 11ef9bfe-1047-4aa2-bf46-2f8a02f5cd44 has an active native leg,
revision 5, with recorded pickup actuals and CompletionOverride=false.
Its 10:54:51 UTC correction returned 200 and history confirms revision 1:
Corrected stop 1: pending. The 10:55:01 request returned 409. Do not silently
reverse this recorded user correction or infer a completion timestamp.

The save failure cause is NOT yet established. Inspect fresh-read versus
save-response fingerprints, revision ownership, and resource conflicts without
weakening concurrency guards. Retry save currently retains pending requests
for 409; distinguish known rejection from an uncertain network outcome.

## Remaining

Fix and verify save rejection and automatic pickup ownership; preserve history.
Review all navigation draft kinds and pending-response safety. Run focused
dependencies and architecture checks before handing a candidate to the sole
deploy owner. Publication and repaired live state remain unverified.
