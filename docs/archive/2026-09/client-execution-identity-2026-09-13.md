# Execution identity in Client planning displays

## Scope

Commercial load identity remains `DispatchId` / `NextLoadRoute.Id`. The Client
now carries the server's optional `ExecutionLegId` and assignment revision where
applicable. Legacy responses retain null leg identity and zero revision.

- Shared planning caches do not publish scoped execution results under an
  unscoped dispatch URL. Seeing a scoped result clears an older unscoped alias.
  Recalculation can update only matching truck, load, leg and revision aliases.
  The truck endpoint remains the owner of its authoritative current-leg result.
- Omitted geometry is reused only for matching truck, load, execution leg,
  assignment revision, plan ID and version. A mismatch requests full geometry.
- Retained ETA/progress, map publication, snapping and stop-ETA context include
  execution identity. A changed leg or revision cannot retain another execution's
  readings or silently leave the fuel editor attached to its earlier plan.
- Next Loads snapshots are bounded by the existing budgets and keyed by truck,
  current commercial load, execution leg and assignment revision. HTTP requests
  send `currentExecutionLegId` when present. Late responses and inspector callbacks
  must match that current scope; metadata must identify a retained future leg.
- Later legs of the same commercial load remain eligible future entries. Their
  lines, markers and selection use distinct execution identities. Existing legacy
  callbacks stay unchanged; scoped callbacks include both execution identities.
- A scoped future-stop card does not adopt an unscoped commercial dispatch-detail
  ETA. Commercial details remain available for exact source-stop facts.

These changes do not create server execution plans, apply switches, recalculate
mileage or infer assignments. They require the matching server contracts and
current-leg selection policy. Server ETA and fuel forecasts must retain their own
execution ownership before they can safely describe repeated source visits.

## Verification

Regression sources cover scoped alias isolation, per-truck recalculation,
geometry mismatch recovery, display identity, bounded future-route snapshots,
leg-specific markers and callback ownership. Their categories are Routing and
Fleet, including map JavaScript and dependent Architecture checks.

Tests and browser checks were not run because the operator reserved test
execution. Isolated Client and Client.Tests builds completed with zero warnings
and errors during implementation. No deployment, migration, provider request,
production-data modification or performance measurement was performed by this
Client subtask. Passing compilation is not an end-to-end exchange verification.
