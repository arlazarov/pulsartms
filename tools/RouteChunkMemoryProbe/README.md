# Saved truck route memory

Read-only diagnostic for actual saved routes, including chunk-backed plans.
It uses the workstation API User Secrets but never starts API/background
services, calls providers, applies migrations or writes application rows.
The database transaction explicitly uses repeatable-read and read-only mode.
Output contains truck unit numbers and aggregate measurements, not coordinates.

Run through the managed artifact runner:

```sh
node scripts/artifacts.mjs run diagnostic -- dotnet run \
  --project tools/RouteChunkMemoryProbe -c Release \
  --artifacts-path '{artifacts}/build' -- --read-only
```

Each truck includes every saved plan, including historical/completed work.
After one warm-up, five samples retain eight independent copies of decoded
plans, their exact indexes and display snapshots simultaneously. Results are
divided by eight; full compacting collections bracket each sample to reduce
collection noise. Snapshots own another exact index, making
this a conservative coexistence scenario rather than the minimum cache state.
Source entity strings are loaded before the baseline and reported separately.
String byte counts omit object headers; cache estimates are admission estimates.

Forced-GC deltas measure retained managed objects in this local diagnostic
process, not Cloud Run RSS. Allocation includes short-lived decoding and display
serialization. Shared EF/runtime/provider state and unrelated cache entries are
excluded. These measurements cannot establish a marginal whole-server cost per
truck or predict capacity by multiplying one result by the fleet size.

## Running-process counters

After an authorized diagnostic deployment, `collect_runtime.py` samples the
protected production memory endpoint using an existing Admin session kept in
the private workstation development directory. It never prints or copies that
session into artifacts. Prepare the session through normal application login.
The script exits on expired or unauthorized credentials instead of bypassing
authentication. Runtime and stage counters contain no cached payloads or keys.

```sh
node scripts/artifacts.mjs run diagnostic -- python3 \
  tools/RouteChunkMemoryProbe/collect_runtime.py --seconds 1080 --interval 5
```

Samples are pinned in `runtime.jsonl` and `stages.jsonl` under the managed run.
Compare samples from the same process start time. Aggregate cache sizes are
admission estimates, not a separate additive component of GC memory. The
collector neither forces collection nor triggers route/fuel calculations.

## Allocation stages and saved preview cycle

Use `--allocations-read-only` instead of `--read-only` to measure decode,
exact-index construction, cold display construction, warm display/metadata
reads and state serialization separately. All saved plans, including completed
work, are loaded once in a read-only transaction before measurement. Each stage
has three warm-ups and twenty synchronous repetitions. Thread-local allocation
counts exclude database loading and are not retained-memory measurements.
Cold display includes decoding and index construction; do not add those stages.
The output contains sizes and counts, not plan identities or coordinates.

Use `--preview-read-only` to run four sequential fleet-preview reads through
the current Application services. It uses one repeatable-read, read-only
PostgreSQL transaction and starts no host, workers or provider refresh.
The first read includes cold EF, JSON and application caches; later reads use
warm caches. Process-wide allocation deltas include incidental runtime work
because asynchronous continuations can change threads. This is not an exact
per-request allocation counter and does not reproduce live planning writes,
external-provider responses, server concurrency or Cloud Run native memory.

The allocation mode also projects compact currency metadata through the real
saved-route reader while the transaction is open. `currency-metadata` measures
only deserialization of those projected results after rollback, alongside full
route decoding. SQL execution, cache overhead and input hashing are excluded
from this comparison.

After publication of the mapping endpoint, add `--include-map` to the runtime
collector to record `maps.jsonl` every 30 seconds. These are aggregated fixed
categories only, not raw process maps or memory contents. Unavailable OS
counters and virtual-only fallback must not be interpreted as resident zeroes.
`--memory-map-local` runs just the OS reader in the diagnostic process without
loading database credentials, connecting to SQL or starting application services.
