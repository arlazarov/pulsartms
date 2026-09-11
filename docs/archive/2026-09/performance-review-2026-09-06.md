# Performance review — 2026-09-06

## Evidence and scope

Production Cloud Run is configured with 512 MiB RAM. Recent logs show OutOfMemoryException in RoutePreviewService (reading saved PlanJson through Npgsql) and ReadCache (JSON deserialization). This establishes failing allocation paths, not a complete retained-heap diagnosis or proof of a leak.

Reviewed route reads/previews, read-cache isolation, planning refresh queues, TomTom request caching and limits, dispatch-board/HOS reads, client planning cache/polling, map geometry simplification and listener disposal, and routing-related indexes. This is not a claim that every project file or production query has been profiled.

## Implemented locally

| Area | Change | Trade-off |
| --- | --- | --- |
| Read cache | Dedicated 32 MiB size-limited cache; individual entries above 8 MiB are not retained | Oversized routes may be read from DB again; the cache cannot retain them indefinitely |
| Saved routes | Copy small entity metadata, share immutable PlanJson string | Each caller still gets an isolated mutable entity; full plan parsing still occurs |
| Other cached values | UTF-8 byte snapshots instead of UTF-16 JSON strings | Keeps mutation isolation; serialization/deserialization still costs CPU |
| Previews | Fetch identifiers first; read route JSON individually as needed, not all current/upcoming payloads together | More small queries, lower peak payload residency; keep preload for fast selection |
| HOS | Reuse the board result in truck planning reads | Removes duplicate board deserialization within one request |
| Browser payload | Omit FuelPlan.RouteChecks from display-only plans | Diagnostic checks remain in persisted plans; client DTO does not consume them |
| Shared previews | Cache simplified previews for 30 seconds inside the bounded read cache | Preview may lag briefly; selected-route refresh remains authoritative |
| Concurrent client reads | Share an in-flight request for the same planning URL | Cancellation stops the caller waiting, not another caller's shared request; session clear prevents old responses repopulating cache |

## Local validation, second pass

- Synthetic snapshot: 2,760,012 JSON characters, 20 warm reads. Previous JSON-wrapper cloning allocated 110,411,720 bytes in 177.63 ms; new snapshot cloning allocated 9,440 bytes in 0.10 ms. This measures snapshot cloning only, not HTTP/DB latency or full route parsing. Timings vary by machine.
- Expanded end-to-end service test to 30,001 geometry points (stored in aggregate geometry and a leg). Repeated warm route/progress/display reads still make zero additional DB reads and no routing-provider calls. Includes stop-progress handling and enabled/disabled synchronization cases.
- The test command reported 277,413,888 bytes maximum RSS including test/build tooling; this is not a Cloud Run RSS measurement or proof of production memory safety.
- Client tests cover shared requests, per-caller cancellation, session invalidation and reuse of matching versioned geometry.

## UX and cost safeguards retained

- Keep last successful route while refresh fails; preserve geometry when id/version is unchanged.
- Keep initial previews and existing client-side planning cache, rather than force every selection into a cold load.
- Keep full computational geometry, distances and ETA logic unchanged.
- Keep TomTom DB cache, daily/minute limits, negative caching and duplicate-request locking unchanged.
- Keep the bounded planning queue and refresh backoff; no extra paid API call is needed for these cache changes.
- No production schema changes, data deletion, memory-plan upgrades or deployment performed.

## Still requires measurement

Before declaring the OOM fixed, compare peak RSS/GC heap, allocation rate, p50/p95 planning latency and response bytes under concurrent map/dispatch usage, including cold caches. Measure individual PlanJson sizes and route point counts. Test on a 512 MiB staging instance with paid providers disabled or replayed.

Largest remaining architectural cost: each display read parses the full computational route before trimming it. A versioned, persisted lightweight display projection would avoid that, but requires coordinated invalidation for reroutes, stop progression, fuel-plan updates and geometry versions. Do not replace live calculations with stale projections solely to save RAM.

Update: the local implementation now uses the bounded in-memory projection described below. Persisting that projection to DB is not implemented or needed for warm reads.

Production-safe diagnostic SQL (read-only, aggregate payload sizes only):

```sql
SELECT count(*) AS plans,
       max(octet_length("PlanJson")) AS largest_bytes,
       sum(octet_length("PlanJson")) AS total_bytes
FROM "DispatchRoutePlans";
```

The mapping was subsequently verified through the read-only probe below.

## Actual saved-route measurement

Ran `tools/RouteMemoryProbe` against the configured database in an explicitly read-only transaction, with a statement timeout and a 16 MiB per-route parsing limit. No application host or paid providers were started. Each route was read separately; the table's loaded totals are sums, not a measured simultaneous process RSS. Retained managed memory was measured after GC on the second parse; input string storage is estimated as two bytes per UTF-16 character.

| Truck | Saved plans | DB JSON bytes | Input string + retained parsed objects, summed |
| --- | ---: | ---: | ---: |
| 11005 | 2 | 8,805,671 | 29,328,878 bytes (~28.0 MiB) |
| 11006 | 2 | 11,611,002 | 38,102,420 bytes (~36.3 MiB) |
| 54777 | 3 | 6,557,711 | 21,500,342 bytes (~20.5 MiB) |

Truck 11006 / load 1372: 6,058,832 stored bytes; 12,117,664 estimated input-string bytes; 7,622,344 retained parsed bytes; 15,943,976 allocated parsing bytes; 101,642 point entries including duplicated aggregate/leg/reference geometry. Second parse took 25 ms on the local machine.

Summed loaded representations of all seven plans are approximately 84.8 MiB, excluding transient allocations, other application caches, EF/Npgsql, network buffers and the runtime. Do not multiply this by a guessed concurrency factor and call it measured RSS. Large routes exceed the current 8 MiB cache-entry cap (which counts UTF-16 input size), so not all of this is retained in the read cache. A lightweight display projection remains the important next step to avoid repeated full parsing while preserving UX.

## Implemented lightweight display projections

RouteDisplayCache retains UTF-8 simplified display JSON and a precise position-matching index; it does not retain the original PlanJson or full RoutePlan object graph. The index stores double-precision coordinates inline in pre-sized segment storage, without retaining RoutePoint objects. Matching allocates only the final output point, not a point per segment.

Projection fills are serialized to bound concurrent cold-load allocation. Warm lookups are lock-free. Entries expire after two minutes, respect the existing route invalidation generation, and share a dedicated 32 MiB size budget. UI reads and previews use this path; ETA/fuel construction still uses full data. The DB and route calculation geometry are not rewritten. Per-request stop enrichment, input validation, current GPS progress and cached ETA selection still run.

Measured actual-route projection sizes (UTF-8 JSON + conservatively accounted exact index and metadata):

| Truck | Prior summed full JSON + parsed routes | New summed display snapshots |
| --- | ---: | ---: |
| 11005 | 28.0 MiB | 2.16 MiB |
| 11006 | 36.3 MiB | 3.11 MiB |
| 54777 | 20.5 MiB | 1.86 MiB |

Total ~7.13 MiB instead of ~84.8 MiB for these representations (~92% reduction). This is not total process RSS: temporary display DTOs, full planning jobs and other runtime/caches are additional. Snapshot accounting is used because GC retained-heap deltas for small samples were noisy, including zero/negative deltas from unrelated collection.

Load 1372 display snapshot: 1,433,083 accounted bytes, including 447,851 bytes of display JSON. Warm DTO parsing allocated 1,209,352 bytes in 1.49 ms versus full parsing's 15,943,976 bytes in 25.99 ms in the same run. Cold projection creation still parses the full route once. Existing geometry id/version responses remain supported. Tests verify exact matching against full geometry, clone isolation, cache invalidation, warm DB-free reads on 30,001 points and low matching allocations.

## Truck movement rendering

Client route matching uses binary-search boundaries for the existing +/-5 mile window instead of visiting every segment. A synthetic 100,000-segment route spanning 1,000 miles visits 1,002 candidate segments with at most 36 boundary index reads. This is an algorithmic test, not a measured browser FPS improvement.

Playback line updates are limited to four per second independently of marker animation. Within an unchanged displayed segment, only the first polyline vertex is updated; the remaining path is rebuilt when its displayed segment changes or zoom detail changes. Stop-label text is written only when rounded values change. Zoom changes preserve interpolated progress. Forced final position updates bypass the drawing throttle. No polling or paid-provider calls were added. All 20 JavaScript tests pass; the client builds without warnings or errors.
