# Core rebuild phase 1P: fuel saved roads through publication

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, migration or application-database experiment was performed.

## Problem and delivered behavior

Fuel already protected canonical work, historical predecessor selection and
effective settings through result commit. The saved roads consumed during
calculation could still change before publication. A cached root could also be
older than the database state used by a later validation query.

SavedRoadVersion now captures the actual current plan, future base roads,
full-plan fallbacks and connecting roads used by the calculation. The current
plan's immutable token comes from the row that supplied its geometry, including
a cached row. A fallback retains the absent or unusable base that selected it;
if a base appears before commit, the old selection cannot publish. An onward
connection remains a dependency even when no eligible exit price exists and
arrival policy falls back to reserve-only behavior. Repeated reads of one source
retain every observed version; only lookup keys are deduplicated.

SavedRoadValidation checks these captures inside the existing protected
publication transaction, before profile or fuel writes. Automatic calculation,
manual editing and reset to automatic share this guard. A conflict preserves
the previous profile, route fuel copy and truck fuel snapshot. Native execution
leg identity, stored and serialized assignment revisions, and ownership remain
explicit. Existing optimistic result checks are unchanged. Fuel-only plan
annotations and unused current baselines do not produce road conflicts.

The former ETA-specific root reader is now ISavedRoutePlanReader in Routing,
implemented once by Infrastructure and shared by ETA and fuel. Metadata queries
return ownership, version and tracking values without transferring geometry.
Provider-specific SQL stays in Infrastructure. The root token remains internal
to Application; no HTTP payload field is added. The existing fourteen-table
publication scope already protects the road sources, including missing-row
inserts. No new locks or optional fallback services were introduced.

ETA and fuel also normalize visited-stop dictionary key order before hashing
tracking. PostgreSQL jsonb does not preserve object key order, so equivalent
tracking objects must not acquire different identities merely through metadata
projection. See the official [PostgreSQL JSON documentation][postgres-json].
ETA policy 16 invalidates the previous signature representation. Tracking
values and passed-stop sequence remain significant.

No schema, migration, financial formula, Client file or worker changed.
Existing unrelated working-copy changes, including the generic
BankOfCanadaExchangeRateProvider name, were preserved.

## Verification

Added 30 server cases: 28 fuel integration cases, one ETA integration case and
one architecture case. Coverage includes:

- Late base, connection and root changes during automatic calculation,
  manual save and reset, with prior profile/results and cache state retained.
- Deleted bases, cleared connection geometry, changed plan version/input hash
  and native stored/serialized ownership or assignment revisions.
- Stale cached current roads, followed by a successful fresh retry.
- Future full-plan replacement, changed full-plan mode and a previously absent
  base becoming available after fallback selection.
- Onward connection changes without eligible exit prices.
- Conflicting repeated observations of the same road.
- Unchanged success, fuel-only writes, unused baselines and reordered tracking
  dictionaries in both fuel and ETA.
- Required transaction ownership and validation before profile/result writes.

SQLite probes inspect returned column names during final road validation;
RouteJson and PlanJson must not be returned. Existing metadata translation and
fourteen-table source-closure checks continue to pass. Existing independent
SQLite writer tests cover saved-road inserts and updates during publication.
These do not establish real PostgreSQL execution or contention properties.

Early runs found an ambiguous target-typed constructor and stale tracked rows
in the new multi-request test fixture. The constructor was made explicit, and
the fixture now clears tracking between request/setup boundaries. Production
optimistic concurrency checks were not weakened. The final runs supersede
those iteration failures.

`bash test.sh fuel` passed: 1,439 Server, 228 Client and 52 JavaScript checks.
Final `bash test.sh all` passed:

- Server.Tests: 2,459 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 4,032; no failures or skipped tests.

The stage changes 31 resulting source, test and documentation files, including
22 C# files. Three old reader/contract paths were moved into shared Routing
ownership. Formatter, whitespace, source inventory and local documentation
links are checked separately from behavioral tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-r3cWEc/baseline.json`: pre-edit hashes for
  1,993 files.
- `artifacts/managed/diagnostic-5Mq5XX/affected-1.log`: constructor feedback.
- `artifacts/managed/diagnostic-43Jewr/affected-2.log`: fixture feedback.
- `artifacts/managed/diagnostic-y3O6OJ/affected-3.log`: affected checks passed
  before the final tracking-order cases.
- `artifacts/managed/diagnostic-74Q2fD/affected-4.log`: tracking setup feedback.
- `artifacts/managed/diagnostic-Y9x99B/affected-5.log`: final affected run.
- `artifacts/managed/diagnostic-GGLT2I/full-1.log`: full suite passed.
- `artifacts/managed/diagnostic-eeiyHB/audit.json`: preliminary source and
  formatting audit; its only missing link was this then-unwritten report.

- `artifacts/managed/diagnostic-3fxSXH/audit.json`: final audit.

## Limits and next slice

These are transient publication dependencies. They do not persist new fuel
history/road tokens for read-time validity or automatic-refresh eligibility
after a previously valid commit. Standalone base preparation and route-choice
settings validity also remain separate work. Unsupported native continuations
remain excluded. Operational captures are not durable accounting evidence.

Road identity relies on existing writers updating input/date or plan-version
fields when replacing geometry. Presence flags detect clearing; arbitrary
payload changes that preserve every version field are outside this contract.
Cache expiry/invalidation remains bounded and per instance. This stage rejects
stale publication without adding distributed invalidation.

The fourteen-table publication scope is broad and transitional. Publications
across trucks remain serialized and source writers can be delayed. No measured
production latency, throughput or memory improvement is claimed. Real
PostgreSQL execution and lock contention were not checked because no suitable
isolated fixture was available. No SQL server was started or installed.

Next: protect standalone base/choice settings and persist the dependencies
needed for fuel refresh after later historical or road corrections. Complete
writer revision ownership is required before narrowing the broad lock.
Normalized visits, removal of compatibility paths, driver settlements and
accounting evidence remain later stages.

Live provider and authenticated browser checks were not run. Client/Razor/HTTP
contracts were unchanged, so no separate Client build was required; solution
tests still build their dependencies. No migration was introduced or applied,
and no deployment was made.

[postgres-json]: https://www.postgresql.org/docs/16/datatype-json.html
