# Core rebuild phase 1O: ETA saved roads through publication

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, migration or application-database experiment was performed.

## Problem and delivered behavior

ETA reread its description before publication, but saved roads could still
change between that check and forecast commit. A cached current plan could
also be older than both database descriptions. Separately, cold future timing
could read geometry different from the metadata used for its cache key.

EtaChainDescription now retains immutable root signatures and future road
versions. Root dependencies include completed routes skipped while selecting
current work. Legacy dispatch and native execution-leg identities stay
separate. Root signatures include stored input/ownership metadata and plan
identity, truck, leg, assignment revision, version and stop tracking. Fuel-only
root changes do not affect road identity. Policy 15 includes these dependencies
and the effective root routing signature in ETA InputHash, including chains
without future loads.

Cold timing compilation derives versions from the actual loaded geometry and
checks them before filling EtaMemory. A mismatch cannot contaminate the cache;
a retry can compile the expected version. Next Loads uses the same shared
loaded-version projection. Both metadata paths include geometry-presence flags.
This matters because deadhead preparation can clear geometry while retaining
financial mileage and its calculation date. Clearing the road now changes its
version identity without discarding those financial values.

Immediately before forecast persistence, ETA validates captured root/future
metadata inside its existing publication transaction. It also compares the
actual current plan used by calculation with the selected root token. A stale
cached root cannot publish against newer metadata. Checks include missing
roads that appear during calculation and completed roots that become active
again. They transfer metadata only, with no geometry or provider request inside
publication. A conflict preserves saved forecasts and removes the unpublished
memory result. Unchanged roads and fuel-only root writes still publish.

Infrastructure adds DispatchBaseRoutes and DispatchRoutePlans to the existing
PostgreSQL source-lock inventory, bringing it to fourteen tables.
DispatchDeadheads was already included. Updates, deletion and insertion are
protected through commit, including source writes that do not enter the
publication service. The exact architecture inventory now derives dependencies
from Infrastructure metadata SQL as well as EF sources and navigation joins.
No new lock service or optional fallback was introduced.

No schema, migration, public HTTP payload field, financial formula, Client file
or worker changed. Existing unrelated working-copy changes were preserved.

## Verification

Added 26 server cases covering:

- Late replacement, deletion and clearing of saved roads and connections.
- Root plan version, ownership, leg and stop-tracking changes.
- Native root assignment metadata addressed by execution-leg identity.
- Previously absent roads appearing before commit.
- A completed root becoming active while a later root is being calculated.
- A stale cached root against newer metadata, with a successful fresh retry.
- Cold geometry/version mismatch without timing-cache contamination.
- Fuel-only root updates and root-only routing-profile invalidation.
- Independent SQLite writers attempting updates and inserts in all three
  saved-road tables, then succeeding after publication releases its scope.

Existing PostgreSQL translation checks inspect the outer returned projection,
including database-computed geometry flags. SQLite read probes inspect actual
returned column names, so a metadata read cannot transfer RouteJson or PlanJson
under the unchanged/label-only path. References to RouteJson inside SQL boolean
expressions and joined subqueries are not full geometry transfer. No
architecture exception, disabled check or relaxed production invariant was
added.

Initial runs found duplicate load numbers in the new concurrency fixture and
a missing test namespace import; both were corrected. Subsequent feedback
refined SQL/result-column checks for the new presence flags. The source-lock
architecture check continues to require exact coverage.

Iteration used `bash test.sh routing`, including ETA and architecture checks.
The final full run supersedes the earlier fixture and SQL-assertion failures;
no successful category-only run is claimed for this stage.

Final `bash test.sh all` passed:

- Server.Tests: 2,429 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 4,002; no failures or skipped tests.

The stage changes 22 source, test and documentation files, including 14 C#
files. Formatter, whitespace and local documentation links are checked
separately from behavioral tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-oJCmu6/baseline.json`: pre-edit hashes for
  1,987 files.
- `artifacts/managed/diagnostic-W4nf48/affected-1.log`: fixture feedback.
- `artifacts/managed/diagnostic-b700dq/affected-2.log`: namespace feedback.
- `artifacts/managed/diagnostic-HduS5z/affected-3.log`: metadata-check feedback.
- `artifacts/managed/diagnostic-kuFgAv/affected-4.log`: SQL projection feedback.
- `artifacts/managed/diagnostic-MBNM5N/affected-5.log`: SQL alias feedback.
- `artifacts/managed/diagnostic-W4mzZv/translation.log`: translation inspection.
- `artifacts/managed/diagnostic-z2D6sI/full-1.log`: full suite passed.
- `artifacts/managed/diagnostic-bEQeef/audit.json`: preliminary formatting,
  whitespace, inventory and documentation-link checks.

- `artifacts/managed/diagnostic-2HeuCt/audit.json`: final audit.

## Limits and next slice

This is an ETA boundary. Fuel still needs captured road-version dependencies
through calculation and commit, plus historical tokens for read/automatic
refresh after corrections. Standalone base preparation and route-choice
settings validity remain separate work. Unsupported native continuations
remain excluded rather than receiving an implicit connection.

The version contract relies on existing writers changing input/date or plan
version identity when replacing geometry; it is not a content hash of every
coordinate. Geometry presence covers clearing, not arbitrary payload edits that
preserve every version field. Current-route cache expiry/invalidation remains
bounded and per instance; conflict rejection adds no distributed invalidation.

The fourteen-table source lock is broad and transitional. It can delay more
road writers, and publications across trucks remain serialized. No production
latency or throughput gain is claimed. Real PostgreSQL execution and lock
contention were not tested because no suitable isolated fixture was available.
Translation and SQLite checks cannot establish those production properties.
No local SQL server was started or installed.

Next: carry fuel saved-road versions through its result publication, then
address standalone base/choice settings and fuel refresh after historical
corrections. Complete source revision ownership is required before narrowing
the broad lock. Normalized visits, removal of compatibility paths, driver
settlements and accounting evidence remain later stages.

Live provider and authenticated browser checks were not run. Client/Razor/HTTP
contracts were unchanged, so no separate Client build was required; solution
tests still build their dependencies. No migration was introduced or applied,
and no deployment was made.
