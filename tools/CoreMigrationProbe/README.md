# Clean operational transition verification

This probe verifies the user's September 17 decision to retain users while
allowing operational data replacement. It creates the previous schema in an
isolated fixture, seeds synthetic identity and business records, explicitly
clears operational tables, and applies the current migrations. It starts no
database server or application workers and never accepts the application
database as its target.

Protected profiles, Identity rows, role claims, logins, tokens, Data Protection
keys and integration configuration are compared without printing their values
or digests. Password verification and effective application-role lookup must
still work after the transition. New accepted work must retain its own visit
order, unknown actual times and immutable history. Transfer confirmation comes
only from its participant; accepted stop rows own its location and operation.
The probe verifies that migration rejects old execution before cleanup and that
downgrade rejects new accepted work without changing migration history.

Independent PostgreSQL connections also exercise the native execution outbox:
claiming around a locked row, recovery after lease expiry, rejection of former
owners, persistent retry deadlines and separate acknowledgements for newer
requests. These checks verify ownership semantics, not fleet throughput.

The durable on-demand refresh queue additionally verifies concurrent duplicate
demand, new input arriving during a leased calculation, preserved failure
deadlines and successful cooldowns across connections. Downgrade must reject
pending requests without changing migration history. All fixture work completes
before the accepted-execution downgrade guard is checked separately.

Existing-leg stop acceptance also runs through the shared Application operation
on PostgreSQL. Import and manual changes must retain accepted order, immutable
actor history and durable rebuild requests. Insertion retires obsolete automatic
planned mileage, identical reimport is a no-op, and rollback retains all prior
facts together. Ordinary import acceptance and explicit initial assignment have separate
PostgreSQL probes covering bootstrap, competing assignments and persisted replay.

The fixture runs the same reviewed `scripts/sql/reset-core-storage.sql` used by
the [cutover runbook](../../docs/operations/core-storage-cutover.md). Its explicit
inventory is checked against mapped tables. Missing acknowledgements, another
connected client and an unknown table reject without deleting work. Protected
identity/configuration remains equal. No CASCADE reset is used. The probe itself
still refuses the working database and grants no deployment authorization.

Build through the managed diagnostic runner:

```bash
node scripts/artifacts.mjs run diagnostic -- \
  dotnet build tools/CoreMigrationProbe \
  --artifacts-path artifacts/tests -warnaserror
```

Run against a separate, initially empty database named
`pulsr_core_fixture_` followed by 32 lowercase hexadecimal characters. Supply
its connection through `PULSR_MIGRATION_TEST_CONNECTION`; do not print it or
place credentials in command arguments or disposable artifacts.

```bash
node scripts/artifacts.mjs run diagnostic -- \
  dotnet artifacts/tests/bin/CoreMigrationProbe/debug/CoreMigrationProbe.dll
```

The explicit `--create-isolated-fixture` option creates a uniquely named
database on an existing PostgreSQL server, runs the synthetic checks, and
removes only that database in its disposal path. It uses
`PULSR_MIGRATION_ADMIN_CONNECTION`, or the configured local user-secret
connection, to connect to the server's `postgres` maintenance database. It never
uses the application database as a fixture. The configured role needs permission
to create and remove databases. No server installation or SQL container is used.

The earlier preserving-backfill and restored-backup modes were retired after
the user's reset decision. Their archived reports describe the tool version
and checks at that time, not the current transition contract.

These are database migration checks, not production performance measurements or
deployment authorization. Stop application writers only as part of an explicitly
approved cutover; this probe never runs migrations against the working database.
