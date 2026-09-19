# Core rebuild phase 1J: profile and fuel write ownership

Date: 2026-09-16. Status: implemented locally; full automated suite passed.
No deployment, schema migration or application-database experiment was
performed.

## Findings and delivered behavior

Fuel calculation previously saved its requested truck profile before reading
prices or running the optimizer. A later failure could leave a changed profile
with the previous saved fuel plan. The final compatibility-copy check also read
the effective profile through display caches, which could hide a stored change.

FuelPlanningService now owns one publication transaction for the requested
profile, current-route fuel copy and truck-owned fuel snapshot. It performs no
profile write before calculation succeeds. A changed input, failed write,
result-revision conflict or failed commit preserves the previous records.
Automatic calculations, manual edits and resets retain the shared commit path.
No routing/geocoding request, formula or station-selection rule was added.

Before changing the profile, fuel compares its initially observed effective
profile with uncached stored inputs. TruckPlanningProfileService,
PlanningSettingsService and FuelExchangeRateService share their existing
projection and validity rules between cached and uncached reads. Fleet defaults,
explicit exchange-rate precedence and saved-rate age checks remain unchanged.
Uncached reads neither request a provider nor fill the display cache. The route
copy checks the newly effective profile again and validates both row and saved
plan truck ownership, including legacy work.

Explicit profile saves now capture remaining truck work and use the existing
PlanningWorkPublication boundary. Changed work, obsolete assignment revisions
and completed dispatch references cannot choose the profile to write. Profile
cache invalidation happens only after commit for standalone saves, route builds
and fuel publication. Failed commits cannot expose an uncommitted profile by
invalidating its cached value as if the save had succeeded.

Fuel publication now invalidates the exact dispatch/execution-leg route cache
after commit. The previous forwarding helper omitted the leg identity, so an
old native-route read cached immediately before commit could survive the save.
Regression cases repopulate that cache at the transaction boundary and verify
that the next legacy or native read returns the committed fuel result.

## Simplification

- Removed unused fuel-clear and recommendation writers from
  RoutePlanningService and RoutePlanStore.
- Removed the unused fuel-driven route-replacement writer.
- Removed the route-service fuel-save forwarding method; the fuel publication
  owner calls RoutePlanStore directly inside its existing transaction.
- Made low-level profile and route-copy fuel writes internal operations that
  require a transaction. Public HTTP commands keep their existing fields.
- Moved three tests from direct profile persistence to the supported profile
  save path, preserving their original behavior assertions.

Legacy persisted fuel recommendation fields remain readable. No Client files,
database tables or migration files changed. Existing unrelated work was
retained.

## Verification

Added 23 server cases covering:

- Changed work immediately before an explicit profile save.
- Successful profile publication and failed-commit cache retention.
- Completed work and obsolete assignment revisions.
- Price-read, work-validation, snapshot-write and commit failures preserving
  the profile and both fuel copies.
- Successful requested-profile publication with matching fuel signatures.
- Automatic and manual fuel saves detecting profile or fleet-setting changes
  behind warm display caches.
- A legacy route row belonging to another truck rejecting fuel publication.
- Pre-commit cache repopulation for legacy and native routes, followed by a
  read of the committed fuel result.
- Uncached profile reads observing changed stored inputs without changing
  display-cache contents, including explicit-rate precedence.
- Changed, removed and expired saved exchange rates without provider calls.
- Architecture checks preventing the removed public mutation entry points from
  returning.

The initial compilation identified three tests calling the former public
profile store method. They now exercise the protected application operation.
No production fallback, architecture exception or skipped assertion was added.

Affected `bash test.sh routing fuel dispatch` passed before the final ownership
regression was added:

- Server.Tests: 2,039 passed.
- Client.Tests: 663 passed.
- JavaScript: 64 passed.

The final required `bash test.sh all` passed:

- Server.Tests: 2,330 passed.
- Client.Tests: 1,012 passed.
- JavaScript: 561 passed.
- Total: 3,903; no failures or skipped tests.

Pinned local evidence:

- `artifacts/managed/diagnostic-vF48Vr/baseline.json`:
  1,962-file pre-edit baseline.
- `artifacts/managed/diagnostic-ngChiP/affected-1.log`:
  initial compile feedback.
- `artifacts/managed/diagnostic-5Lyxtx/affected-2.log`: affected checks.
- `artifacts/managed/diagnostic-uEjGif/full-1.log`: first full suite.
- `artifacts/managed/diagnostic-evUKY4/full-2.log`: final full suite.

The stage changes 24 source, test and documentation files. Formatting,
whitespace and local documentation links are checked separately from behavior;
the final audit is pinned alongside the pre-edit baseline.

## Limits and next slice

The canonical-work table lock is unchanged. It does not lock fleet settings or
exchange-rate storage against concurrent writers. Fresh reads detect changes
already visible in the transaction; they do not establish a cross-process
settings revision contract or guarantee that settings cannot change before
commit. Explicit profile editing still has no client revision token. Cached
display reads retain their existing bounded freshness.

Historical predecessor inputs were inspected but not replaced. Their completed
native-leg references and unknown-start guards cannot be reconstructed from
remaining work alone. The next historical slice needs an explicit immutable
input contract that retains this evidence and its freshness check.

Narrow per-truck publication ownership still requires every source writer to
participate in revision changes, including legacy number assignments and queue
membership. No narrower lock or production speed improvement is claimed here.

PostgreSQL execution, lock contention, live providers and authenticated browser
checks were not run. No suitable isolated PostgreSQL fixture was used, and no
database server was started or installed. There are no new or applied migrations
from this stage. Accounting and driver-settlement rules remain outside its
scope.
