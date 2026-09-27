# Completed native connection preparation

## Incident and owner

The authorized historical-mileage recovery queued accepted completed native
loads through SourceRoadStore and BaseRouteOperation. A queue item could finish
without preparing mileage: DeadheadService.EnsureAsync returned early for a
completed commercial status. The corrected Completed reader consequently still
reported pending financials. Publication of the previous read fix alone did not
resolve the missing historical data.

DeadheadService remains the authoritative owner. Its initial and captured-history
eligibility guards now share CanPrepareConnection. A completed commercial load
qualifies only with an accepted native completed execution leg. Source-only
completed work and cancelled commercial work remain excluded. Existing active
paths are unchanged. No new cache, formula, provider integration or UI read is
introduced. Ordinary active planning scans still exclude completed legs; this
supports explicitly demanded historical preparation.

## Consistency and existing rows

The existing captured history, company boundaries, dependency revisions, durable
reservation, retry deadline, input signature and guarded publication remain in
force. The service reuses saved valid connections without a provider call.
A predecessor change during the provider request rejects publication. No accepted
assignment, actual time, fuel plan or execution history is modified by this fix.

The operational recovery owner must wait for the corrected API and old worker
drain. Existing pending requests retain their versions and retry deadlines.
Requests incorrectly marked finished by the old guard require one bounded new
recovery identity, only when their normal Completed read remains pending.
Queue completion must be followed by an authoritative financial read.

Runtime coverage gap: there is no bounded auditor for a completed request whose
historical financial result remains pending. Routing owns that deferred check;
completion requires owner-based detection with dependency versions and explicit
coverage, not a second calculation. The operational tool performs that comparison
for this bounded incident. AMF1388 and AMF1136 separately lack a trustworthy
historical connection and must not receive fabricated totals.

## Verification

Two completed native regressions failed before the correction and passed after:
normal preparation and a controlled predecessor-revision interleaving.
All five focused lifecycle/worker cases pass. The explicit durable-demand test
starts with a completed native load without saved empty miles, runs the real
BaseRouteOperation against an isolated SQLite fixture and observes 200 total
miles and 0.5 RPM through GetDispatchQueryHandler. A second unchanged worker pass
adds no provider request. Native publication does not overwrite a load-wide rate.

Affected Dispatch/Routing checks passed; exact counts are retained in the
operational evidence. Production recovery and full exact-candidate release gate
remain pending at this commit. No migration, DTO, dependency registration or
Client source changed. No production performance or complete UI claim is made.
