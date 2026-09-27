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

## Save-response defect established

For 54777, accepted revision JSON retains ManualCompletionRecordedAt
10:54:51.2928948Z; the typed PostgreSQL row stores 10:54:51.292894Z.
Workspace fingerprints include accepted stop serialization. EF's write context
retains pre-persistence values, so the original post-save response can disagree
with the next request even without another writer.

A dedicated, isolated PostgresFixture regression reproduced the mismatch
between the write-context response and a new context. It failed on the original
reader. ReadSavedAsync now uses the same workspace owner and query chain with
no-tracking identity resolution for saved response entities. CorrectDispatchStop,
CreateDispatch and UpdateDispatchWorkspace use it after SaveChanges. Ordinary
command reads stay tracked. No timestamp is rounded by Application, no version
check is removed, and receipt history is preserved. Relevant leg revision
changes still change the fingerprint. No new provider calls or query chain were
introduced; allocation/latency performance was not measured.

Existing old receipts may retain their original fingerprint. Explicit reload
reads current persisted facts; do not rewrite historical receipts. Stop-draft
409 responses now expose the existing explicit reload flow rather than asking
for an identical retry forever. The draft remains until explicit discard.
Actual uncertain/network retries keep their request identity.

Evidence: the original PostgreSQL fingerprint test failed; fixed persistence
and stop-correction checks passed 25 tests. Expanded persistence, workspace,
creation and server Architecture selection passed 146 tests, zero skips.
The client conflict regression failed before the UI correction; workspace plus
client Architecture checks passed 24 tests, zero skips. Client compiled.
These are focused checks, not a full release gate. Tests used an isolated
registered fixture, never the application database.

11005 resource conflict still needs owner-level resolution and confirmation
of predecessor completion. An explicit question about AMF1403 is pending.
No production writes or incident deployment performed by root.

## Review follow-up

Independent review found NativeLoadCanBeEditedWithoutImport failing when its
create and edit share a context: equal prices with decimal scales 1200 and
1200.0 serialized differently. Reproduced locally along with a new direct
fingerprint invariant. Commercial's price projection now has canonical decimal
scale, while remaining a JSON number compatible with stored commercial
baselines. No price value is rounded. Both previously failing cases pass.
Focused creation/correction/persistence/import checks: 45 passed, zero skips.
The added numeric JSON round-trip assertion passed separately. JavaScript
architecture: 67 passed. Full gate remains the deploy owner's responsibility.

The user confirms AMF1403 was fully delivered and the correct trailer number
is 055904 (our accepted row is named 55904). Bounded source reads contain
AMF1403 delivery 2026-09-26T12:43:40.010Z and AMF1410 pickup
2026-09-26T20:41:54.488Z. Source assignment resolves a different inactive
trailer row named 055904, while accepted execution references 55904. Truck,
driver and co-driver identities match. The reconciliation resource check stops
before copying actuals. Do not globally strip zeros or bypass inactive-resource
checks: resolve the confirmed duplicate identity with preserved history first.
No production repair has occurred yet.

## Handler sequence verification

The isolated PostgreSQL regression now exercises CorrectDispatchStopHandler
with a fresh context for each of two consecutive saves, passing the preceding
response fingerprint and revision into the next request. Both succeed and
workspace revision advances to 2. This extended test passed, zero skips.
This supplements the reader precision regression; it is not a production test.
