# Dispatch summary read change

The user requested fast local implementation without tests or repeat
measurements. This change is not performance-validated or release-validated.

Board planning now asks the board selector for identities only, avoiding
full card hydration before capturing truck planning inputs. The HTTP summary
handler uses BoardPlanningReader, which permits at most two concurrent truck
reads across requests in this process. Each reader owns a separate DI scope,
DbContext and explicit company context. Cancellation releases the shared
semaphore. Input order, current-work selection and fuel validation are retained.

The direct PlanningReadService.ForBoardAsync path remains sequential for
callers using a shared scope. No shared DbContext is used concurrently.
No UI contract, database migration or production deployment is involved.

The HTTP board reader also fetches fuel summaries for the selected trucks
in one database query. Each isolated reader receives its truck's summary
for the lifetime of that read scope, including a missing result. Geometry
is excluded from this batch. Existing integrity checks are shared with the
single-truck reader; writes invalidate the captured summary. The two test
stores implement the new batch contract, but their tests were not run.

The telemetry writer already batches its lookup and SaveChanges call and
ignores older positions. Fuel publication already uses a conditional upsert
that rejects old or repeated versions. Those write semantics were retained.
No indexes were added without query-plan evidence.

The updated API and local probe compiled successfully in
`artifacts/managed/diagnostic-8l4IGo`. This is compilation evidence only.

Tests, browser verification and before/after measurements were intentionally
not run at the user's request. Compilation is the only completion check.
The previous 4,858-test pass predates this change and does not validate it.

## Board detail hydration follow-up

The board now resolves execution ownership before reading source details.
It only projects source stops for loads without accepted execution. Previously,
it hydrated source details for every load and discarded those owned by execution
after hydrating execution separately. An all-native page now skips that source
detail query. Mixed pages retain source-only details. A correlated ownership
filter excludes source fallback even for cancelled or otherwise excluded
execution legs, without a separate ownership round trip for source-only pages.
Net performance has not been measured.

No persistence semantics, indexes or migrations were changed in this follow-up.
Compilation is recorded in the local build output.
Tests and browser verification remain not run.

## Fleet catalog follow-up

FleetNames loads truck, driver and trailer labels using one concatenated
projection instead of three sequential queries. A kind discriminator preserves
separate dictionaries even if IDs overlap across resource tables. Each query
branch retains its normal company filter. The existing request snapshot,
shared cache key and invalidation rules are unchanged. This reduces database
round trips only when the shared catalog cache needs to be populated; warm
cache reads were already served without these queries. SQL execution and
latency have not been verified for this change.
