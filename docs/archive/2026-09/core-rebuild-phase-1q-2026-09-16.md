# Core rebuild phase 1Q: base and route-choice settings publication

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, migration or application-database experiment was performed.

## Problem and delivered behavior

Route-choice preview/save read routing settings through the display cache.
Canonical work validation did not detect a profile changed without cache
invalidation, or between the profile read and publication. Standalone base
preparation had no protected settings comparison; legacy base writes did not
own a publication scope and native writes used only their assignment lock.

Both paths now reuse TruckPlanningProfileService.RequireRoutingCurrentAsync.
It rereads effective settings uncached and compares the existing route-input
signature for the same load. This preserves one definition of routing
restrictions. Fuel-only settings, rates and confirmation flags do not change
road identity. A stale supplied profile is rejected before provider work, and
a second comparison runs inside publication before any result write.

Route-choice validation precedes replacing a preview, advancing a choice
revision and writing a selected base/current road. A conflict retains the
previous draft, choice, base and live plan. Existing work, current-route, GPS,
native assignment and optimistic result checks remain. No cache invalidation
occurs for a settings rejection.

Standalone base preparation enters the existing IPlanningPublicationScope.
Its settings read and route write share protection through commit, including
when a profile row was initially absent. Native assignment locking remains
inside that scope. A supplied outer transaction is rejected before provider
work. Geocoding and route calculation finish before publication begins.
Operation-owned base entities are detached as before; pre-existing tracked
rows retain their established ownership. Commit failure rolls back the base
and any native planning request.

Manual live-route builds use an internal base-build entry point that receives
requested dimensions and the previously observed stored profile separately.
An intentional dimension edit is therefore allowed while an intervening
stored restriction edit rejects publication. The later live-plan transaction
retains its full-profile validation and profile write. Work without a truck
uses the existing default-profile identity for routing validation.

Application owns the new orchestration. Infrastructure's existing scope owns
provider-specific SQL. No optional business-service fallback, new lock table,
public HTTP contract, schema, financial formula, Client file or worker was
introduced. Test composition now exposes its existing shared profile/base
services instead of repeating constructor wiring. The future trip-cost,
owner/operator and actual fuel/toll import requirements remain in the plan.
Unrelated working-copy changes were preserved.

## Verification

Added 31 server cases:

- Fifteen route-choice cases cover full/current routes, stale warm profiles,
  changes during provider work and at preview/save publication, unchanged and
  fuel-only success, and native revision retention on a settings conflict.
- Thirteen base cases cover assigned/completed-native sections, early/provider/
  publication conflicts, absent-profile insertion, unchanged routing with
  fuel-only edits, default-profile work, outer transaction rejection, deliberate
  manual dimension changes and rollback of base/native requests on commit
  failure.
- Three architecture cases require the uncached settings comparison after
  publication begins and before result writes, using the existing scope.

The initial affected run found that a new fixture attempted to access an
internal production helper. The fixture now uses the existing public domain
projection; no visibility or architecture rule was relaxed. The subsequent
affected run and full suite passed. An existing base-route characterization
now persists its changed profile before preparing with those dimensions,
matching the new standalone input contract.

`bash test.sh routing` passed: 1,032 Server, 190 Client and 52 JavaScript
checks.
Final `bash test.sh all` passed:

- Server.Tests: 2,490 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 4,063; no failures or skipped tests.

The stage changes 22 source, test and documentation files, including 15 C#
files. Formatting, whitespace, inventory and local documentation links are
checked separately from behavioral tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-uSASRf/baseline.json`: pre-edit hashes for
  1,997 files.
- `artifacts/managed/diagnostic-TdmdEY/affected-1.log`: fixture compile
  feedback.
- `artifacts/managed/diagnostic-T9aStL/affected-2.log`: affected suite passed.
- `artifacts/managed/diagnostic-YeYeIN/full-1.log`: full suite passed.

- `artifacts/managed/diagnostic-bjrhfz/audit.json`: final audit.

## Limits and next slice

This protects settings; it does not supply a new canonical work snapshot for
standalone unassigned or historical base sources. Existing native assignment
guards remain, but other standalone source revisions and ownership still need
consolidation. No persisted fuel-history/road dependency or automatic-refresh
rule after a valid commit is added here.

Base-cache publication and the subsequent live-plan/profile publication remain
separate transactions. A successful cache write can survive a later live-plan
failure; its matching input hash does not approve a new live route or profile.
This stage does not claim a combined transaction or immutable financial
evidence.

The fourteen-table source lock is unchanged, but standalone base writes now
enter it too. This can increase contention and defer preparation while another
writer is active. No measured production latency, throughput or memory gain is
claimed. Real PostgreSQL execution and contention were not tested because no
suitable isolated fixture was available. No SQL server was started or installed.

Next: persist the dependencies needed for fuel read/automatic-refresh validity
after historical or road corrections. Remaining standalone work-input capture,
complete writer revision ownership and PostgreSQL measurements are still needed
before narrowing the global source lock. Normalized visits, compatibility
removal and financial implementation remain later stages.

Live provider and authenticated browser checks were not run. Client/Razor/HTTP
contracts were unchanged, so no separate Client build was required; solution
tests still build their dependencies. No migration was introduced or applied,
and no deployment was made.
