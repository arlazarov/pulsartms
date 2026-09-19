# Source review, planned boundaries and detached reads

This record describes local implementation on September 17, 2026. It is not a
production deployment or completion of the full core rebuild.

## Source proposals and accepted history

DispatchSourceLink retains the normalized imported header and per-visit resource
proposal before local workspace overrides. The Assignments tab compares those
facts with accepted leg resources. Unmatched imported names remain visible and
are not treated as resolved catalog identities. The comparison is read-only.

Review-only source observations now request durable planning revalidation without
advancing the accepted leg revision or recording another accepted history row.
Restoring the original source resources clears the warning under the same rule.
Actual accepted changes still use ExecutionAcceptance and immutable history.

The proposal column is folded into the unapplied
20260917055902_RebuildExecutionStorage migration, alongside the previously
verified typed stops, accepted revisions and durable work queues.

## Planned source transfers

Transfer preview and acceptance share SwitchSourceAssignment eligibility. Exact
operator-selected boundaries can split assigned or unassigned source work into
planned outgoing and incoming legs. Planning does not invent an actual start,
release or receipt. Source pickup can activate the outgoing leg; the incoming
leg still requires explicit receipt. Resources are rechecked at activation.

A future plan may reuse a currently busy truck without activating it twice.
Driver-only prefixes are rejected as truck work until their truck start is
explicitly corrected. Successful retries retain the same two initial revisions.
The PostgreSQL transfer probe verifies planned source activation as well as
planning, cancellation, independent confirmations and fresh-context replay.

## Read ownership

Domain Dispatch.ForExecution has been removed. Native base-road sections and
planned mileage signatures now consume captured immutable RouteWorkSnapshot
values directly. Caller mutation cannot change their captured assignment,
visits or signature. Source planning overrides cannot become accepted assignment
metadata.

The existing Application ExecutionRouteProjection temporarily serves the
remaining entity-shaped consumers. It now constructs detached copies, including
independent stops, instead of cloning a source entity graph. Resource labels from
the old assignment, planning anchors and tracked navigation objects do not leak
into accepted work. Handoff readiness and unknown actual times survive copying.
This bridge still exists; its removal remains an explicit core exit condition.

## Browser corrections

The first browser run exposed a source-review notice covering the stop controls
at 390px with 200% root text. It now belongs to the bounded scrolling header and
moves into the comparison body while Assignments is open. The final comparison
also verifies the actual warning text and its own horizontal bounds. Nested
headings use the existing section-heading token.

The browser uses the staged Blazor application with intercepted fixture API
responses and synthetic identity. It makes no working-database or provider writes.
Root text scaling is not browser zoom or proof of every visual state.

## Verification and limits

The full local suite passed 4,283 checks: 2,707 Server, 1,015 Client C# and 561
JavaScript. Evidence: artifacts/managed/diagnostic-0cZlXo/tests.log.
The release gate also passed all 4,283 checks, strict Release builds with zero
warnings/errors and validation of 273 assets and seven JavaScript entry graphs.
Evidence: artifacts/managed/diagnostic-sf05Kc/release.log; staged Client:
artifacts/managed/release-xyrvBI/publish/wwwroot.

The PostgreSQL probe passed the clean reset, protected identity/configuration,
import/replay, accepted history, route publication, durable queues and transfer
checks. Its isolated fixture was removed. Evidence:
artifacts/managed/diagnostic-TKcna5/postgresql.log. The probe build passed with zero
warnings/errors in artifacts/managed/diagnostic-ZtZEZO/build.log.

The staged workspace browser matrix passed 11 scenarios with no browser errors,
unexpected requests or failed assertions. It covers 1440px, 390px and 320px,
both themes and 390px at 200% root text, including the assignment comparison.
Evidence: artifacts/managed/browser-ui-gqMeeD/report.json and screenshots.

These results precede any subsequent planning writer-revision work. Performance
and production contention were not measured.

The working database is unchanged. Compatible cutover, protected backup,
operational cleanup and working migration remain pending. Accounting, driver and
owner/operator compensation, actual fuel/toll imports and toll estimates remain
future product extensions described by the core specification.
