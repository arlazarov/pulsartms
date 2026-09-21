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

Both subsequent fuel recalculations returned HTTP 200 and success:

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
