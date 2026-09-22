# Fleet database latency investigation

## Measurement

The existing native ARM64 50-truck fixture was restarted with an opt-in,
request-scoped EF diagnostic listener. It retains only aggregate durations,
counts, SQL fingerprints and table names. SQL text and parameter values are
not retained. The Admin policy gates both collection and reading results;
anonymous access returned 401/403. No production code or data changed.

The listener follows the existing diagnostic approach in RouteMemoryProbe.
An AsyncLocal scope separates marked requests from ordinary browser polling
and background operations. Each request completes before the next begins.
No builds or tests ran during measurements. Results below are repeated reads
after initial requests, not guarantees of a completely warm cache: mutations
and elapsed time can invalidate or expire entries between operations.

| Operation | Wall ms | SQL commands | Command duration sum ms |
| --- | ---: | ---: | ---: |
| Truck planning read | 2,239 | 18 | 1,273 |
| Default board, including ETA | 5,567 | 90 | 5,125 |
| Automatic planning, one truck | 2,701 | 33 | 1,749 |
| Fuel plus ETA, one truck | 16,035 | 111 | 7,186 |

Connection-open totals were less than one millisecond per measured request.
This sample does not show connection-pool starvation. Command durations
include remote database/network response time, not just PostgreSQL CPU time.
Reader consumption, materialization, transaction start, application work and
waits can contribute outside these command events. Sums can overlap for
concurrent operations and must not be presented as exact exclusive fractions.

The one-truck refresh endpoint calls AutomaticPlanningService.ForTruckAsync.
It does not measure queue claim, completion or the entire background wrapper.

## Actual Client request split

The earlier load driver used the default board endpoint with ETA enabled.
Client DispatchList explicitly disables HOS, financials and ETA for its base
rows, then requests financial and ETA enrichment separately. Additional
measurements used those actual flags:

| Repeated request | Wall ms | SQL commands | Command duration sum ms |
| --- | ---: | ---: | ---: |
| Base board | 264 | 4 | 181 |
| Financial enrichment | 266 | 4 | 185 |
| ETA enrichment | 3,947 | 88 | 3,777 |

The first pass gave 359, 232 and 4,288 ms respectively. The slow default-board
measurement must not be described as the initial Client table render time.

## Concrete source path

EtaChainInputsService.DescribeTrucksAsync captures itineraries and roots in
batches, but then awaits DescribeCoreAsync for each truck. That method calls
FutureVersionsAsync and DeadheadHistoryService.ReadLoadedAsync independently
for each truck's future work. In the repeated default board request, six SQL
fingerprints accounted for 75 commands. Five repeated 12 or 13 times, and
one repeated 14 times. They read source dispatches, native execution stops,
future road versions and predecessor/history facts.

The existing stage counters attributed approximately 11.06 seconds of the
two default board requests to ETA, consistent with this request-level result.
Repeated automatic planning also rereads assignment, native execution and
resource facts: several SQL fingerprints occur three times. Some protect
fresh publication and cannot simply be removed or globally cached.

The next targeted improvement is batch preparation of ETA future-road and
history inputs across the page, preserving per-truck original history batch
membership and the existing read snapshot. Then assemble each description
without additional per-truck persistence calls. Verify identical hashes,
exclusions, native/legacy road selection, history dependencies and freshness.
Do not parallelize queries on one DbContext or weaken publication validation.

Fuel/ETA still has substantial time outside command events. This investigation
does not establish that every remaining delay is SQL or that eliminating these
queries solves its full latency. A separate scoped stage trace is needed before
removing its validation or changing concurrency.

## Evidence and verification

Managed evidence directories:

- `diagnostic-1LecfR`: eight scoped measurements and stage snapshot.
- `diagnostic-J4uFpf`: six measurements of the Client's three board variants.
- `diagnostic-tHpj0q`: instrumented native executable, retained locally.
- `diagnostic-UHy7gE`: 4,854 passing tests before the listener policy correction.
- `diagnostic-mugHFe`: final automated verification.

The discarded `diagnostic-9OB0Zw` run collected no SQL measurements because
the initial listener checked a role claim instead of the actual Admin policy.
The corrected run explicitly rejects missing measurement output. The native
publish, CSharpier and diff checks passed. No migration or deployment occurred.
Final verification passed all 4,854 tests: 3,177 server, 1,052 Client C# and
625 JavaScript, plus JavaScript type checks. Anonymous diagnostics returned 401.
