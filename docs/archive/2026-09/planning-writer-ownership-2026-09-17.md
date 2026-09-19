# Planning writer ownership — September 17, 2026

This record covers the local working tree. The core schema migration remains
unapplied to the working database; no deployment was performed.

## Result

Planning publication no longer locks fifteen complete PostgreSQL tables.
PlanningInputRevisions retains a global row and rows for every truck identity.
A known truck publication acquires a shared global guard and exclusive truck
ownership before its repeatable-read validation snapshot. Unknown membership
and automatic exchange-rate publication acquire exclusive global ownership.

Seventeen writer triggers cover work, accepted visits, load links, transfer
participants, resource catalogs, stored settings and saved roads. Writers
advance both old and new membership, retain deleted identities and resolve
number-only source assignments using trimmed, case-insensitive numbers. Truck
creation/removal/rename and shared resource/settings changes coordinate through
the global guard. Only the exchange checkpoint participates in planning;
unrelated synchronization rows remain independent.

The existing input-closure architecture check now requires exact trigger
coverage derived from the actual readers. It has no new exception. The pending
RebuildExecutionStorage migration installs and removes the revision table,
triggers, function and normalized-number index together.

## Verification

- The full suite before the wrapped-error correction passed 4,284 checks:
  2,708 server, 1,015 Client C# and 561 JavaScript.
  Evidence: `artifacts/managed/diagnostic-HrilA7/tests.log`.
- The first concurrency rehearsal exposed a provider-wrapped PostgreSQL 55P03
  error. Ownership was enforced, but publication did not classify the wrapped
  exception as a planning retry. The correction inspects inner exceptions.
- The corrected release gate passed the same 4,284 checks, strict builds with
  zero warnings/errors and validation of 273 assets and seven JavaScript graphs.
  Evidence: `artifacts/managed/diagnostic-JSqTNa/release.log`.
- The corrected probe built with zero warnings/errors and passed on the isolated
  PostgreSQL fixture `pulsr_core_fixture_6250f7dd8ab1480ba3e51d035a783c02`.
  Evidence: `artifacts/managed/diagnostic-m1N822/postgresql.log` and `build.log`.
- Independent connections verified concurrent unrelated-truck publication,
  same-truck rejection, blocked source writes/new membership/missing profiles,
  catalog and rate coordination, unrelated checkpoint progress and rollback.
- Revision deltas verified old/new assignment, deleted native links, normalized
  number-only sources and all 17 enabled trigger registrations.
- Empty upgrade/downgrade and the populated clean transition passed. All earlier
  acceptance, transfer, road, queue, retry and lease probes also passed. The
  fixture was removed; protected identity/configuration and password/role checks
  passed after resetting its 49 operational tables.

These are concurrency-correctness checks, not production throughput or latency
measurements. Shared source loads and global changes can still serialize work.
Remaining immutable consumer migration and compatible cutover are tracked in
[the core specification](../../architecture/core-rebuild.md).
