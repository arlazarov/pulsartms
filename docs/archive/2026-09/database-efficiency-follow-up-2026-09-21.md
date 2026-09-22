# Database efficiency follow-up

## Scope and changes

Local implementation only. The fixture has 50 synthetic trucks; a board page
contains 12. No production database, migration or deployment was used.

- ETA descriptions fetch truck profiles in one query and reuse the existing
  profile resolution rules. Returned profiles remain independent mutable values.
  Individual profile reads and publication validation retain their existing paths.
  Batch reads deliberately read current rows rather than inserting prefetched
  rows into individual cache entries after their generations might change.
  This costs one query on a warm batch where individual cached reads could cost
  none; the cold batch replaces up to 12 queries with one.
- Checkpoint acquisition attempts the conditional ownership update first. An
  existing available checkpoint needs one statement instead of an existence
  read followed by an update. Initial creation and contention retain their
  fallback checks; lease-protected saves and renewal are unchanged.
- Fleet-name query-count assertions now expect the combined query. A profile
  regression covers duplicate input IDs, missing stored profiles, parity with
  individual reads and independently mutable results.

## Checks and observations

The prior fleet catalog union executed successfully on PostgreSQL and SQLite.
The dispatch board and POST planning summaries returned HTTP 200 before and
following the new build. An initial diagnostic incorrectly used GET for planning
and received 405; subsequent diagnostics use the controller's POST contract.

Evidence:

- `artifacts/managed/diagnostic-mP5qGl`: initial board calls; cold profile reads
  totalled 12 commands. The GET planning attempts are invalid measurements.
- `artifacts/managed/diagnostic-I1r3eB`: corrected POST planning calls before
  the follow-up, approximately 12.53 and 5.25 seconds.
- `artifacts/managed/diagnostic-dWpUQS`: updated board profile reads totalled
  one command each. Board calls took 3.84 and 2.32 seconds; a cold planning call
  took 11.14 seconds. These small, cache-sensitive samples do not establish an
  overall latency improvement. Planning still reads profiles per truck.
- `artifacts/managed/diagnostic-wI1m3Y`: fixture index inventory and read-only
  EXPLAIN ANALYZE. The available-work selection used the existing index and took
  0.079 ms, with 50 completed rows filtered out. This is not a claim-transaction
  concurrency benchmark or evidence about a large production backlog.
- `artifacts/managed/diagnostic-fWeut8`: successful API/probe compilation and the
  currently running local fixture build.

## Remaining costs and decisions

The key route, truck-profile and fuel-plan lookup indexes already exist. No new
indexes were added without evidence of a missing useful access path.

Stored route PlanJson averaged 23,465 bytes across 50 rows (maximum 23,933).
Geometry is already stored separately. Saved-road metadata is projected within
PostgreSQL instead of transferring that geometry.

Cold planning remains expensive: fuel application reads checked geometry to
project progress and validate reachability. The display cache similarly loads
route chunks to construct a spatial index even for a metadata response. Those
reads cannot simply be removed without changing distance and freshness behavior.
A future optimization should load the required fuel leg and share revision-keyed
progress facts, with parity checks for detours, passed stops and low fuel.

Telemetry already reads/writes a batch; unchanged EF fields are not updated.
Fuel publication rejects repeated/older versions. Checkpoint state writes and
route optimistic concurrency were retained: skipping them blindly could hide a
lost lease, freshness transition or concurrent publication. No claim is made that
all database work is now minimal.

## Final verification

`bash test.sh all` passed: 3,182 server tests, 1,052 Client C# tests and 625
JavaScript tests (4,859 total), plus TypeScript checks. The first run exposed
three obsolete query-count assertions; a later run caught duplicate unit numbers
in the new test fixture. Those test issues were corrected before the final pass.
No architectural exception or weakened correctness assertion was introduced.

The existing browser tab recovered through Retry after the local API restart.
Dispatch displayed 50 trucks, its first page of cards, route distances, HOS and
fuel-stop counts. This is a smoke check, not exhaustive visual verification.
Expired ETA values remain blank on this synthetic fixture, whose periodic ETA
refresh is disabled. Production integration and deployment checks were not run.
