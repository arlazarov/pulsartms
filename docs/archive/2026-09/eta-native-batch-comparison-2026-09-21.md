# Batched native ETA history: paired results

## Implementation

DeadheadHistoryService now reads native eligibility evidence and hydrates
execution loads once for a combined source batch. Each original group retains
its own truck set, predecessor dispatches and saved connection references.
Completed legs are filtered against those original sets after the union read,
before predecessor snapshots are constructed. Shared dispatch identities still
use separate source batches. All reads remain within the existing snapshot.

The single-batch path delegates to the same implementation. This avoids two
independent definitions of native predecessor eligibility. Existing stage
measurements for native leg reads and execution hydration remain available.
No publication rules, input hashes, database schema or provider SQL changed.

Two new regression cases use separate groups for the same truck and prove
that a completed leg eligible through predecessor membership or a saved
connection cannot leak into the other group. Existing tests cover native and
legacy selection, captured-input isolation and publication replay.

## Paired measurement

The previous build and new build ran sequentially on the same isolated
50-truck PostgreSQL fixture. Both used native Linux ARM64, one CPU, 1 GiB,
workstation GC and identical page-one ETA enrichment requests for 12 trucks.
Each process restarted before four requests. Tests and builds finished before
timing. Browser polling remained active, so CPU totals are container-wide.

| Median of three repeated requests | Previous build | New build |
| --- | ---: | ---: |
| SQL commands | 55 | 22 |
| Wall time, seconds | 4.088 | 1.807 |
| Sum of command durations, seconds | 3.622 | 1.353 |
| Container CPU time, seconds | 0.483 | 0.341 |

SQL count fell by 60%, and median wall time by 55.8%. Repeated responses ranged
from 3.639 to 4.218 seconds before and 1.747 to 1.990 seconds after. First
requests took 5.878 and 4.006 seconds, with 76 and 43 commands respectively.
Against the original unbatched query count of 88, the new count is 75% lower.
The timing comparison above is against the immediately preceding build only.

The final parsed HTTP responses were exactly equal. Stored forecast validity
times did not change; the synthetic fixture does not periodically regenerate
ETA. Equality therefore proves unchanged returned data for this fixture, not
fresh forecasts for all trucks or complete production parity. The sample is
small and uses a remote database. No memory reduction or new full-fleet queue
throughput claim is made.

## Validation and evidence

All 4,858 tests passed: 3,181 server, 1,052 Client C# and 625 JavaScript, plus
JavaScript type checks. The affected routing suite passed first. The native
Release publish, formatting and diff checks passed. The local browser remained
usable with TEST-050, its load and remaining mileage displayed; ETA was expired
as expected for the fixture. UI code was unchanged.

Evidence under `artifacts/managed`:

- `diagnostic-ULKlEh`: paired timings, SQL counts and exact HTTP responses.
- `diagnostic-UAfCxS`: previous executable.
- `diagnostic-6fjuAA`: new executable, retained and running locally.
- `diagnostic-1oV6Sr`: affected routing tests.
- `diagnostic-hICl4z`: complete test suite.

No production deployment, production migration or resource-limit increase was
performed. Further optimization should use the remaining command timings;
22 queries does not by itself prove that every remaining query is necessary.
