# Core rebuild phase 1M: stored settings at publication

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, migration or application-database experiment was performed.

## Problem and delivered behavior

Fuel already reread effective settings before saving, but the publication
scope did not prevent another process from changing those settings before
commit. ETA had no final uncached dimension check. A background live-route
build could also restore an older profile over a more recent profile edit.

The shared Infrastructure publication scope now protects
TruckPlanningProfiles and FleetPlanningSettings alongside work and history.
The PostgreSQL inventory grows from ten to twelve tables, retaining its fixed
order, NOWAIT acquisition before snapshot reads and owned transaction.
Protecting table membership matters: a missing row still supplies defaults,
and inserting its replacement must not escape validation. Provider calls and
calculation remain outside the publication transaction.

Fuel's existing uncached profile checks now run while truck/fleet settings
remain protected through both saved fuel copies and profile writes. Explicit
fleet exchange-rate preferences are part of those settings. Automatic stored
rate observations use a shared checkpoint table and remain outside this lock;
this stage does not block unrelated checkpoint jobs or claim rate ownership.

Live route builds capture the observed effective profile before provider work
and compare it inside publication before saving the requested profile.
Automatic builds also reject an already-stale supplied profile before calling
a provider. Manual builds can intentionally change dimensions when stored
settings have not changed during calculation. Existing explicit profile edits
keep their last-accepted-write behavior without a new client revision field.

Automatic progress/reroutes and ETA use the profile owner's uncached routing
check inside the result transaction. It reuses the established route-input
signature for the same captured load and each profile. Fuel-only preferences,
rates and confirmation flags do not reject road ETA or progress. Full-profile
writers retain the stricter comparison. Rejected ETA publication preserves
saved forecasts and removes its uncommitted memory result. ETA input
preparation now depends directly on the profile owner rather than the larger
route orchestration service.

Display caches retain their existing bounds. Uncached validation neither fills
them nor calls a provider. Progress checks that make no write keep their cached
profile path; fresh settings validation belongs to publication. No public HTTP
field, Client file, financial formula or database schema changed. Existing
unrelated working-copy changes were preserved.

## Verification

Added 18 server cases covering:

- Profile and fleet-setting changes at manual/automatic live-route publication.
- Stale automatic profiles rejected before provider calls and unchanged saved
  profiles/results after rejection.
- Intentional manual dimension changes accepted and committed.
- Late dimension changes rejected by progress and reroute publication.
- Height, weight and hazardous-cargo changes rejected at ETA publication,
  preserving forecasts and clearing uncommitted memory despite warm caches.
- Fuel-only profile/fleet changes accepted by road ETA.
- Independent SQLite connections attempting profile/settings updates or inserts
  during publication, then succeeding after release.
- ETA profile validation ordered between transaction entry and forecast writes.

The existing architecture closure check now covers both stored-settings
readers and the exact twelve-table lock inventory. No architecture exception
or weakened test was introduced. Existing fuel regressions still cover both
saved copies, warm caches, profile atomicity and commit failure.

The first full run caught three query-budget regressions: unchanged progress
checks performed two unnecessary settings queries. The implementation now
retains the cached preliminary read and validates fresh settings only before
an actual write. The existing query-budget assertions were left unchanged.

Affected `bash test.sh routing fuel dispatch` passed:

- Server.Tests: 2,101 passed.
- Client.Tests: 663 passed.
- JavaScript: 64 passed.

Final `bash test.sh all` passed after the no-write query correction:

- Server.Tests: 2,389 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,962; no failures or skipped tests.

The stage changes 21 source, test and documentation files. Pinned CSharpier
validation covers all 13 changed C# files. Whitespace and local documentation
links are checked separately from behavioral tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-6eTrXd/baseline.json`: pre-edit hashes for
  1,979 files.
- `artifacts/managed/diagnostic-BUloJw/changed.json`: initial C# inventory and
  pinned formatter run.
- `artifacts/managed/diagnostic-3PR7Ai/affected-1.log`: affected checks passed.
- `artifacts/managed/diagnostic-AE4NFa/full-1.log`: query-budget feedback.
- `artifacts/managed/diagnostic-cCLY3b/full-2.log`: final full-suite run.
- `artifacts/managed/diagnostic-yQunjM/audit.json`: preliminary file inventory
  and formatting/whitespace audit.
- `artifacts/managed/diagnostic-kN1NDr/audit.json`: final file inventory,
  formatter/whitespace checks and local documentation links.

## Limits and next slice

The existing global publication lock remains a transitional compromise.
Publications across trucks serialize; settings writers can now defer
publication and wait for active publication. No production throughput or
latency improvement is claimed. Complete source-writer revision ownership is
still required before safely narrowing this scope.

Automatic exchange-rate observations, saved-road versions, standalone base-road
preparation and route-choice settings validity retain separate policies.
Uncached rate reads do not protect the observation until commit. Fuel still
lacks a persisted historical dependency token for automatic refresh after a
valid commit. These are explicit remaining gaps, not completed work.

Next: define automatic rate and saved-road ownership without locking unrelated
synchronization work; carry the required evidence into fuel refresh. Durable
accounting records, driver settlements and normalized visits remain later
slices. This phase does not claim the entire core is complete or rated 10/10.

Real PostgreSQL execution and lock contention were not tested: no suitable
isolated PostgreSQL fixture was available. No local database server was
started or installed. SQLite and source-level protocol checks do not establish
PostgreSQL behavior. Live providers and authenticated browser checks were not
run. No separate Client build was required because Client/Razor/contracts were
unchanged; the full solution test runner still builds its dependencies.
No migration was introduced or applied, and no deployment was made.
