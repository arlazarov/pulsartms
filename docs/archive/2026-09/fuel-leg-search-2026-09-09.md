# Fuel search failure and per-leg verification — September 9, 2026

## Scope and observed failure

Truck 11005, root dispatch `5269044d-c35c-4977-8023-92eedcbcf62d`,
failed at `TruckFuelPlanStore.SaveAsync` with the snapshot invariant
`ArgumentException`. The local API was still using the 15:59 Release assembly,
before the route anchoring and memory repairs described in the
[memory audit](route-fuel-memory-audit-2026-09-09.md). No persistence guard was
relaxed. The current API was rebuilt and restarted without migrations,
synchronization, or Gmail background maintenance.

One user-requested recalculation on that build completed and saved successfully
in **66,590.8896 ms** at approximately 21:41 UTC. The then-current tank reading
was 100% (211.34 US gal), not the earlier low reading. It covered all three
assigned loads and selected LOVES #306 first, at approximately 923 route miles.
This was a live application operation, not a database test fixture.

The slow run made three short post-delivery checks and eleven full-itinerary
routing requests. The eleven stored compact responses totaled **23,626,997
bytes**, generally about 2.1 MB each. These are serialized response sizes, not
retained heap or network-transfer measurements. The request timing includes
database, local optimization, provider, schedule, and commit work; no exact
phase timing breakdown was recorded.

## Changes

- Request-local subset memoization includes ordered visit identities and the
  distinct minimum-stop mode. Equal-mile ties, successful ordering, infeasible
  outcomes, and cancellation retain existing behavior.
- Schedule replay is skipped only when an ideal incumbent cannot be beaten by
  the alternative's lower bound using actual checked fuel, terminal, and extra
  road-time costs. Nonideal incumbents and invalid bounds always replay.
- Checked alternatives replace only mandatory legs containing changed fuel
  visits. Unchanged legs reuse the baseline; shared recipes reuse actual checked
  geometry. Every result still evaluates the complete assigned itinerary.
- The latest recipe per leg is request-local with a combined 200,000-point cap.
  At most twelve complete variants and eleven additional routing calls are
  allowed, separately from the existing three terminal checks. If individual
  recipes would exceed the remaining calls, one complete anchored route request
  preserves the supported long-itinerary envelope within that same budget.
  Exhausted call budget skips unchecked candidates, never publishes a partial
  chain. Price colors, tank settings, reserves, $20 stop cost, and driver-time
  pricing are unchanged.
- Search version 19 preserves valid version 11–18 results on ordinary reads;
  explicit recalculation can replace them atomically.

## Verification status

The first targeted run passed 56 memoization and schedule-ranking tests. The
final `bash test.sh all` run passed **1,061 Server, 485 Client C#, and 218
JavaScript tests (1,764 total)**. API Release build passed with zero warnings and
errors. Regressions include all-assignment integration, bridge purchases,
economics, strict anchors, visit order/ownership, cached price metadata, complete
fallback, cancellation, point/call budgets, and preserving version 18 display.

After the local API restart, one explicit version 19 recalculation completed and
saved in **37,705.0797 ms**, approximately 43% less elapsed time than the first
control. It compared eight complete variants using eleven additional routing
calls. Ten fresh provider responses totaled **8,197,609 serialized bytes**; the
remainder came from the provider cache, including the earlier terminal checks.
Nine fresh responses were individual fuel-bearing legs and one was the bounded
whole-itinerary fallback. The largest response was 2,129,380 bytes. The compared
payload totals are stored JSON sizes, not wire bytes or retained process memory.

The resulting three-load, 3,134-mile plan bought 138 US gal at LOVES #306 first,
20 at #435, 186 at #397, and 45 at #706. Tank reading remained 100%; the truck had
moved since the first run, and provider cache/traffic conditions differed. This
is a live control comparison, not an isolated latency benchmark or proof of a
globally cheapest plan. The map returned to an enabled calculation button with
the selected truck intact and no calculation exception.

Sampled local API RSS was about 399 MiB shortly after restart, 508 MiB after
the control, and 145 MiB roughly three minutes later. These are individual working-set samples, not managed-retention or
peak measurements; they do not demonstrate a leak or establish safe production
memory use. No production memory ceiling or sustained-load check was performed.

No production deployment, migration, or disposable PostgreSQL fixture was used.
Local results do not establish production latency, a process memory ceiling,
globally optimal station selection, or live traffic accuracy at future stops.
