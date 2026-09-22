# Native ARM64 versus emulated x64 memory

Follow-up: the [full native 50-truck run](fleet-load-native-50-2026-09-21.md)
completed against a fresh fixture without forced GC.

## Controlled comparison

The same optimized source was published for Linux x64 and Linux ARM64 and run
sequentially against the same isolated fixture, with one CPU, 1 GiB and no swap.
The Mac host is ARM64. `uname -m` in the final container returned `aarch64`.
Both variants used workstation GC. No production settings were changed.

The earlier 50-truck rerun was interrupted during cold preparation at the
user's request. This is a small diagnostic scenario, not a completed native
50-truck capacity test. The schema contained 50 truck records, with only some
plans prepared. Identical requests read truck indexes 0, 25 and 49 and
calculated fuel/ETA for index 0 twice. The latter two planning reads did not
represent fully prepared routes. Each process then idled 30 seconds before
the fixture-only Admin endpoint forced full GC with LOH compaction.

| MiB | Emulated x64 | Native ARM64 |
| --- | ---: | ---: |
| Container after idle | 559.3 | 319.9 |
| Container immediately after compaction | 497.3 | 263.9 |
| Managed estimate after idle | 156.0 | 186.5 |
| Managed estimate after compaction | 65.1 | 69.2 |
| Last-GC fragmentation after compaction | 38.0 | 7.5 |
| GC committed memory after compaction | 167.2 | 149.0 |
| Process RSS after compaction | 574.8 | 342.4 |

The roughly 233–239 MiB container difference shows that the earlier emulated
measurements cannot directly establish production memory requirements.
This includes architecture/runtime differences, not a separately measured
Rosetta-only allocation. Cgroup usage and RSS are different accounting views;
memory-map aliases must not be blindly summed or subtracted from live GC bytes.
Forced compaction is a diagnostic observation, not an application fix.

## Live object evidence

After the native comparison, `dotnet-gcdump` captured 73,083,315 shallow heap
bytes across 380,636 graph objects (approximately 69.7 MiB). This collection
itself triggers GC and happened after the paired measurement. A graph walk
found these concrete root paths:

- A 16 MiB byte array is retained by static `LazyData`, through
  `Lazy<MemoryStream>` and `MemoryStream`. The deployed `GeoTimeZone.dll` is
  the assembly containing `LazyData`; Infrastructure uses this library for
  geographic time-zone and HOS-region lookup.
- 8, 4, 2 and 1 MiB byte arrays have paths through
  `SharedArrayPoolThreadLocalArray[]`: reusable pooled buffers.
- Several approximately 1.1 MB byte arrays have paths through MemoryCache,
  Cached and Snapshot objects.
- Approximately 5.14 MiB of coordinate arrays and 0.69 MiB of large block
  arrays belong to route geometry indexes. Their root paths pass through
  MemoryCache, RouteGeometry and RouteGeometryIndex.

The report gives one shortest path per large object, not retained-size or
exclusive-owner analysis. These observations identify live data and pools;
they do not prove a leak or establish long-term retention stability. Clearing
all caches, adding recurring forced GC or moving data into the database is
not justified by this snapshot.

## Validation and evidence

Both diagnostic publishes succeeded. All 4,854 automated tests passed after
the fixture compaction endpoint was added; production source was unchanged
in this investigation. Builds/tests were started only after the paired memory
measurements; the subsequent heap inspection is not a latency benchmark.

Evidence under `artifacts/managed`:

- `diagnostic-Mpn8sF`: paired requests, idle/GC snapshots and memory maps.
- `diagnostic-T7JQMb`: raw native heap dump.
- `diagnostic-ihZDPl`: stock heap report.
- `diagnostic-RJJaRS`: type totals and root-path report.
- `diagnostic-UoSJzl`: native executable, left running locally.
- `diagnostic-fl4q8t`: matching x64 executable.
- `diagnostic-0d23nO`: complete test output.

The native local fixture is retained. No 100-truck test, deployment, production
migration, cache-limit reduction or recurring forced-GC policy was performed.
