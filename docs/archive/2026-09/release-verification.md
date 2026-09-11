# Release verification and performance history

Historical review retained from the release guide. These results apply to their
recorded working copy; use the [current release workflow](../../operations/release.md)
for new deployments. Later evidence belongs to the
[stabilization record](stabilization-work.md).

The working copy includes substantial pre-existing changes and untracked source files. They must be included together when committing the feature set. No staging, commits, history rewrites or deployment were performed in this review. Splitting existing work requires reviewing coherent feature groups: identity/release, dispatch/schema, synchronization, routing/fuel/ETA, and client/map.

## Implemented performance changes

Local verification on 2026-09-08 passed the complete gate: 99 Node tests, 393 server
tests, 70 Client C# tests, strict solution build/publish, 222 published asset hashes,
six generated JavaScript dependency graphs and all 40 offline UI page cases. The
UI report contained no browser errors, unexpected requests or checked geometry
failures. Live map/provider acceptance and browser zoom were not run. The SDK
reported the optional `wasm-tools` workload absent; native WASM optimization was
not performed. Cloud Build, Firebase and Cloud Run were not executed by this local
verification; deployment control flow is covered by stubbed-command regressions.

These are source-level findings, not measured production latency, frame rate or billing.

1. Map rendering: the single GPU scene owns truck, station and stop rendering. Stop circle/number pairs retain stable IDs and data when hover only changes ordering. A number or job change replaces only that stop's pair. These object-reuse regressions are not GPU timing measurements.
2. The server lazily simplifies each cached history snapshot once and skips the truck DB lookup for a fresh snapshot. The unused Client history renderer and its polling code have been removed; they are not part of the current map.
3. Route payloads: clients send knownPlanId/knownVersion through the existing controller/MediatR/read-service chain. An exact match omits coordinate arrays while retaining current route metrics, stops, tracking, fuel and ETA. PlanningDisplayCache restores only the coordinate arrays from the captured matching plan. The Blazor-to-JavaScript boundary also omits geometry after an acknowledgement of the same plan ID/version/truck ID; a cache mismatch requests full geometry without mutating the scene. Stop metadata updates independently of road geometry. A new plan/version returns full display geometry; older HTTP clients still receive full geometry.
4. Dispatch board: a truck-specific request filters dispatches in SQL before projecting stops and uses a truck-specific entry in the existing board cache group. Assignment matching uses ID and normalized-number dictionaries and retains ID precedence, stop assignments and deduplication. Full-board pagination still uses the existing complete-board projection.
5. Repeated route reads: planning read paths reuse an already loaded dispatch. Automatic planning refreshes its state after actual route/tracking writes, fuel generation or recommendation changes instead of unconditional repeated reads. Tracking fetches fleet telemetry once for both current and recent positions. ReadCache clone semantics remain unchanged to avoid sharing mutable plans.
6. TomTom: successful and failed cached requests take a read-only fast path without the global semaphore, advisory lock or a new transaction. Misses still recheck inside the existing lock, preserving quota and duplicate-call protection. Safety-related truck routing and detour checks are not removed and quotas are unchanged.
7. Assignment-only queries in automatic planning, fuel-region planning, route previews and synchronization set IncludeHos:false. Display queries continue to load HOS.
8. Fuel imports invalidate the existing fuel cache after each committed message; IFTA updates invalidate it after saved changes. Dispatch endpoint columns use the same proportional grid in every row, with the existing mobile stacking breakpoint preserved.

## Remaining measurement / scaling work

TomTom now commits a quota reservation before HTTP; the database transaction no
longer spans the provider request. A pending reservation conservatively retains
attempt accounting and a bounded retry window after interruption. Fake-provider
regressions cover malformed responses, cancellation and retry suppression; see
`security-rollout.md`. Full-board DTO splitting, immutable read-model caching and
cross-instance history/ETA deduplication remain possible later changes, not completed
work. PostgreSQL execution plans, multi-instance contention and live browser frame
times have not been measured against production. No real paid API calls were made
during local verification.

Instrument database command counts and durations, response sizes, external cache hit rates, paid calls per planning operation, browser long tasks and frame times before committing to numerical speedup targets.
