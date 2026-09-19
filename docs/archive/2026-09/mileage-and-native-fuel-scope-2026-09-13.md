# Mileage attribution and native fuel ownership

Date: 2026-09-13.

## Mileage module

The implementation and API contract are documented in
[operational mileage attribution](../../features/mileage-attribution.md).
New movement records keep resource identity and load context independent of
provider imports. Planned and manually reported actual evidence are separate,
versioned records. Automatic policy attribution is captured when recording a
movement; an explicit, audited correction can reevaluate the current policy.

Actual distance requires a factual start and end, observed no earlier than the
end. Recording or completing an interval checks other movements of that truck
inside a serializable transaction. Exact adjacency is allowed. Planned entries
without factual times do not claim that the truck already traveled that route.
Known write conflicts return a reload-required response rather than silently
replaying a mutation. A repeated creation key returns the original movement.

Read summaries are bounded, read-only allocated-row subtotals. Empty includes
bobtail as a subset, never an extra addition. Legacy imported loaded miles and
validated saved deadhead are only a planned compatibility view when no native
execution or movement ledger exists. Opening the view creates no movements,
provider requests, actual mileage or payroll data. The subsequent approved
automatic recorder adds validated native planned segments and an independent
OBD odometer feed. Its factual boundary rules and partial-coverage limits are
documented in the maintained mileage guide; complete trip capture is not assumed.

## Native fuel scope

Fuel request, saved snapshot, itinerary, geometry cache and projection identify
the current execution leg and assignment revision. The current native horizon
ends at that leg's last stop. It does not append future transfer legs using
legacy commercial-load deadhead. Pending receipt is not a current GPS origin.
Native schedule previews use the execution leg's driver, not the mutable truck
catalog driver. Legacy-only itineraries retain their existing calculation path.

Different execution legs of the same commercial load cannot share assignment
signatures or reuse a saved station plan. An old manually edited plan does not
silently supply purchases for another leg. Native snapshots require consistent
scope in their summary and itinerary; these additions are JSON fields, not new
fuel database columns. Legacy and native route rows are read by explicit scope.

Multi-leg fuel planning across a transfer remains deliberately unsupported until
confirmed movement, driver and fuel-transfer continuity can be represented.
This is an explicit boundary, not an assumption that the next leg is empty or
has the current truck's GPS and fuel.

## Verification status

Added unrun source regressions for mileage policies, bobtail subtotal handling,
planned/actual separation, interval adjacency and overlap, idempotent recording,
manual evidence provenance, audited overrides and native fuel scope.
Automatic source regressions additionally cover cumulative measured values,
reset/gap rejection, factual cargo and assignment boundaries, bootstrap limits,
planned/actual summation, cursor replay and delayed factual confirmation.
No tests, provider experiments, database writes or deployment were run by this
implementation agent. Formatting used the pinned CSharpier version. Coordinated
builds belong to the parent task; a build is not runtime or PostgreSQL validation.
No performance improvement has been measured or claimed for the mileage module.
