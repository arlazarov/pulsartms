# Execution and mileage implementation checkpoint

Date: 2026-09-13 (America/Toronto).

This is source/build evidence, not a deployment or operational repair record.
The maintained scope and remaining release gates are in
[the execution guide](../../architecture/dispatch-execution-and-settlements.md).

## Delivered in source

- Independent trips, resource-assignment legs and optional load links.
- Native transfer participants with separately confirmed release and receipt,
  actual custody intervals, optimistic revisions and idempotency receipts.
- Native-scoped board projection, route/base-route/choice persistence, previews,
  preparation queues, polling and Client enrichment identity.
- Current-leg ETA/cache ownership and driver HOS selection; no catalog-driver
  fallback for missing, stale or unconfirmed native assignments.
- Current-leg fuel ownership; successor legs are not treated as confirmed
  deadhead or current truck GPS/fuel state.
- Fleet configuration and imported/local field ownership.
- Movement policy, load allocation, planned/actual evidence, exception history
  and lazy Client mileage breakdown/recording editors. No Pay implementation.

Mileage and fuel limitations are also recorded in
[their focused checkpoint](mileage-and-native-fuel-scope-2026-09-13.md).

## Build evidence

Managed-artifact builds used the pinned project toolchain, warnings as errors,
single MSBuild worker and shared compilation disabled.

- Server API and its dependencies compiled without warnings or errors.
- Client, JavaScript assets and SCSS compiled without warnings or errors.
- Server.Tests and Client.Tests compiled without warnings or errors.
- No xUnit tests, JavaScript tests, architecture test runner, browser checks,
  provider probes or PostgreSQL validation were executed. The user controls
  when tests may run. Compiling test sources is not executing them.
- No memory, request-count or production-latency improvement was measured.

The first Client build exposed a Sass mixed-declarations warning in the new
mileage editor; moving its width declaration before the shared mixin fixed it.
Compiler failures during integration were corrected before the successful
builds. No claim of a full test-suite pass is made.

## Database and deployment

`20260914022232_AddExecutionAndMileage` was scaffolded with the design-time
context and an inert connection string. Scaffolding did not connect to a
database or start application workers. The migration was not applied anywhere.

The schema replaces legacy unfiltered route uniqueness with execution-aware
partial indexes. Old API/background writers must be drained before application
of the migration and must not run against the new schema. Rollback refuses to
discard native operations, movement/policy history or local resource settings.

No SQL server/container was started. No operational data, dispatch assignment,
transfer actual, production configuration or deployment was changed.
In particular, this checkpoint does not claim that trucks 11005 and 54777 were
repaired on the server. Native Switch API/UI activation is still gated by the
remaining workflow and reconciliation work in the maintained execution guide.

When authorized, the shared persistence/contracts changes require the full
`bash test.sh all` run. PostgreSQL cases require an approved isolated fixture;
do not substitute the application/production database or a database container.
