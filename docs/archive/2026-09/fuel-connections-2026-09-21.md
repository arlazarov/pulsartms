# Fuel connection recovery — 2026-09-21

Trucks 11007 and 54777 reported a missing matching saved connection while
calculating fuel after the company-isolation release.

## Production findings and recovery

For 11007, the connection from AMF1400 to AMF1386 had been prepared by the time
of the read-only diagnostic. Its predecessor execution, input signature and
geometry all matched; its endpoints were continuous with the current road.
The later preparation timestamp does not prove the exact earlier failure.

For 54777, AMF1397 remained a planned accepted execution after its source
replaced the delivery stop ID. The source pickup and delivery were completed,
but the accepted original delivery was still incomplete. Source reconciliation
retained it for topology review. Fuel consequently selected AMF1397 after the
active AMF1396, even though that did not match the chronological connection.
The user explicitly confirmed that AMF1397 was already completed.

An authenticated production request used the existing stop-completion endpoint
to close the accepted delivery with the source's actual delivery timestamp,
`2026-09-20T22:42:59.004Z`. The endpoint recorded the completion and execution
history under the caller's identity; no direct SQL repair or invented delivery
time was used. The source topology was not silently rewritten.

Both initial fuel recalculations returned HTTP 200 and success:

- 11007: `Feasible`.
- 54777: `FeasibleBelowReserve`, a valid plan whose first purchase is reached
  below the preferred reserve, not an unreachable station or a technical failure.

The compact results are retained in
`artifacts/managed/diagnostic-ZFc6Gr/result.json`. Production data was read in
read-only database sessions during diagnosis; writes used the application API.

## Local prevention

The background road repair scan had no selected company, so runtime query
filters returned no loads. In-memory hints also attempted to observe work
without selecting its owner. Explicit durable demands could still prepare
roads, which is why this could recover after a map request while the normal
scan did nothing.

The worker now scans each active company with separate pagination and resolves
hints under their owning company before queueing them. Provider routing remains
outside fuel calculation and publication transactions. No company filter is
disabled. Missing or incompatible geometry is still rejected by fuel planning.

New regression cases start the worker with no selected company and cover both
ordinary scans and explicit hints outside the speculative date horizon.
`bash test.sh all` passed: 3,093 server, 1,052 Client C# and 623 JavaScript tests,
4,768 total, without failures or skips. PostgreSQL tests ran in the isolated
fixture. Evidence: `artifacts/managed/diagnostic-4lkVty/check.log`.

This prevention change is local and has not been deployed. The production
completion and successful recalculations above used the already deployed API.
No migration is required. Browser rendering and sustained production behavior
were not verified by these checks.

## Tracking initialization after a route rebuild

The subsequent 11007 read marked fuel stale because the truck was off its saved
road. Rebuilding from its current position exposed a separate invariant failure:
the new plan was persisted with a null `Tracking.NextStopId`. Fuel publication
then rejected the otherwise matching remaining itinerary as changed assignments.

Route construction now invokes the existing stop tracker before publication,
using accepted completion facts without requiring a later background GPS pass.
The publication check remains unchanged. A regression builds from the current
position after pickup completion, reads the persisted plan, and verifies both
the next delivery ID and the fuel remaining-stop guard.

After this change, `bash test.sh fuel` passed 1,731 server, 234 Client C# and
64 JavaScript tests, including the dependent routing and architecture checks.
Evidence: `artifacts/managed/diagnostic-LSJ7bP/check.log`. This was a targeted
run; the full-suite result above predates the tracking initialization change.
Neither local prevention fix has been deployed.

The existing production route-preview and choice endpoints recovered 11007
without deploying code or directly editing its stored plan. Preview, save and
fuel recalculation succeeded; the final calculation was `FeasibleBelowReserve`.
The read-back contained the correct next delivery and two fuel stops. It still
reported `Truck is off the calculated route.`, so this recovery does not establish
that its road stays current as telemetry changes. The 54777 read-back contained
one fuel stop with `NeedsRefresh = false`. Evidence:
`artifacts/managed/diagnostic-VPhhxu` and
`artifacts/managed/diagnostic-tlk5yy`.
