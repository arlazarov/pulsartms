# Process memory map diagnostics — September 21, 2026

## Purpose and boundary

The existing runtime sample separates approximate managed objects and GC
committed memory from cgroup usage, but cannot attribute the remaining memory.
The new Admin-only `GET /api/diagnostics/memory/map` reads Linux proc mappings
through an Application contract implemented by Infrastructure. The API remains
a MediatR boundary. There are no SQL writes, migrations, provider requests,
forced collections, memory dumps or application-behavior changes.

The response contains fixed aggregate categories and optional resident/PSS/
anonymous/private-dirty counters. Raw addresses, paths and mapping labels are
never returned or logged. Results are cached/coalesced for 30 seconds; map text
is bounded at eight MiB and status at 64 KiB. Invalid or oversized scans are
not returned as partial totals. Restricted hosts fall back from `smaps` to
virtual-only `maps`, then explicit unavailable status. macOS reports unsupported.

Anonymous memory is not automatically native memory or GC memory. Shared/aliased
mappings can double-count physical pages in RSS, and mapping reservations are
not resident usage. The endpoint does not inspect GC roots or allocation stacks.
See the maintained [interpretation guide](../../operations/diagnostics.md).

The workstation Docker CLI exists, but its daemon was not running. A live Linux
container smoke was therefore not run; no database server or substitute fixture
was started. Parser fixtures include the high-address Linux vsyscall mapping,
missing counters, known-zero counters, units/overflow, cancellation, privacy and
category grouping. The approved production release subsequently verified
`smaps` availability; the observations below describe its limitations.

## Verification

`bash test.sh all` passed the full local suite, including architecture checks.
Evidence: `artifacts/managed/diagnostic-T3lSXt/tests.log`. CSharpier and
`git diff --check` passed. The standalone diagnostic mode compiled in Release
and ran without database credentials or provider setup; on this macOS host it
correctly reported `unsupported-platform` and null Linux counters.
Evidence: `artifacts/managed/diagnostic-THZniQ/map.json`.
Python collector syntax was checked and `--include-map` ran against the
protected production endpoint. Browser checks were not run; there are no UI
changes. The local full suite passed 3,176 Server, 1,052 Client and 625 JavaScript
tests without skips or failures.

## Approved publication

Explicit user approval authorized build
`b58a7538-f423-430f-9374-fe04c2a63771`, revision
`amftms-api-b-b58a7538-f423-430f-9374-fe04c2a63771`.
Image digest:
`sha256:3c2d450382f7683d10ee08fd08d8e4b2a64dc40d0a85b08243b5b5b09ef32fe9`.
The deployment wrapper verified readiness and 100% traffic. The memory limit
remains 512 MiB; no migration or Client publication was needed. Deployment
evidence: `artifacts/managed/diagnostic-cP8IiW/deploy.log`.

The cloud gate passed 3,152 Server and 1,052 Client tests. It skipped 20
PostgreSQL tests without an isolated fixture. JavaScript reported 619 passed
and six skipped, with no failures. Asset verification covered 255 files and
11 entry-point dependency graphs. Evidence:
`artifacts/managed/diagnostic-hZI83a/build.log`.

## Production mapping interpretation

The endpoint returned `smaps` / `complete`. Each reported PSS counter equalled
its RSS counter, including double-mapped runtime code. Consequently these
observations do not establish deduplicated physical costs of those mappings.
The runtime mapping category grew during warmup; it is not a measured count of
unique JIT code bytes. Raw paths and addresses were not collected.

The host did not expose RssAnon/RssFile/RssShmem or cgroup anonymous/file/kernel
counters through the existing readers. Process RSS and container accounting
differ materially. Do not sum this table with GC counters or interpret the
difference between anonymous RSS and GC committed bytes as exact native usage.
Anonymous mappings include GC, native allocations and unlabelled stacks.
The labelled process heap covers only part of native allocation.

This deployment adds attribution, not a memory reduction. A short observation
window cannot establish long-term stability, rule out a leak or prove that the
earlier OOM is fixed. The next attribution step is an allocation-stack profile
for anonymous/native memory and runtime code generation under a representative
workload, before changing GC policy, dependencies or container limits.


## Observed counters

The three-minute collector recorded 31 successful runtime samples and
6 mapping responses (5 distinct cached scans), with no request errors
and one process start. The first
sample was `2026-09-21T23:39:54.4758919+00:00` and the last
`2026-09-21T23:42:53.1410355+00:00`. Container usage ranged from
370.3 to 412.7 MiB and ended at 412.7 MiB. Final approximate managed
objects were 100.6 MiB, GC committed memory 154.2 MiB, and measured byte-cache
admission sizes 12.6 MiB. Cache sizes are included in managed memory;
not every cache reports bytes. Liveness returned HTTP 200 after collection.

Last mapping scan RSS by category (not additive with container/GC counters):

| Category | MiB |
| --- | ---: |
| anonymous | 231.0 |
| anonymous-executable | 0.0 |
| assembly-file | 79.0 |
| kernel-special | 0.0 |
| labelled-process-heap | 5.8 |
| labelled-stack | 0.6 |
| native-library-file | 25.5 |
| other-file | 0.3 |
| runtime-double-mapped | 110.0 |

Evidence: `artifacts/managed/diagnostic-oXqNid/`, containing `runtime.jsonl`,
`maps.jsonl`, `stages.jsonl` and `summary.json`. No credentials are stored there.
