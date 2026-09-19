# Core storage cutover

This procedure applies only to the approved September 17 core rebuild, from
`20260914214701_AddStopCorrections` to `20260917055902_RebuildExecutionStorage`.
It is a nonrolling release. The user has authorized operational replacement and
selected the working database; deployment still requires the explicit approval
specified in `AGENTS.md`.

This transition was applied on September 18, 2026 (UTC). See the
[production record](../archive/2026-09/core-cutover-2026-09-18.md).
Do not repeat the reset on the rebuilt database. The procedure below documents
the reviewed transition and its recovery prerequisites.

## Release inputs

- Run the complete release gate and isolated PostgreSQL rehearsal for the final
  source state. Keep the exact published Client and API image digest together.
- Inspect the actual Cloud Run service, traffic, tags, scaling and database
  connection target. Keep secret values out of evidence and command arguments.
  Identify local APIs and every other writer sharing the database.
- Record a read-only inventory. Expect 60 public tables and 39 applied
  migrations at this starting schema. Any difference needs review before
  cleanup.
- Use an existing PostgreSQL server for the isolated rehearsal. Do not start a
  database container, install a server or use the working database as a fixture.

The reviewed [reset script](../../scripts/sql/reset-core-storage.sql) explicitly
lists 49 operational tables. It retains Users, all seven AspNet identity/role
related tables, DataProtectionKeys, IntegrationCredentialSettings and EF
migration history. Identity profiles, passwords, role claims, external logins,
token rows, preferences and key XML retain their values. Deployment-provided
integration credentials remain unchanged. The script uses no CASCADE and rejects
an unknown table, unexpected schema, another database client or a missing
acknowledgement. Protected row fingerprints are compared inside the same
transaction and never printed. A fingerprint check is not a substitute for the
backup.

## Approved maintenance sequence

1. Build and verify the replacement before stopping users. Record the API image
   digest and matching Client artifact. Do not let its automatic migration start
   against the old live service.
2. Stop every application reader/writer, including old tagged revisions and
   local APIs. Disable traffic and drain background workers. Verify that the
   database has no other client connection before cleanup; do not merely set
   traffic to zero while an old worker still runs.
3. Create a fresh full custom-format PostgreSQL backup with a compatible client.
   Store it outside disposable artifacts in a private directory (0700, file
   0600). Keep its checksum, verified archive inventory and recovery location. A
   prior preflight backup while writers were live is additional recovery
   evidence, not the final cutover backup. Do not print row contents, hashes of
   individual identities, credentials or key material.
4. Connect directly to the verified database with credentials supplied securely.
   On this connection, set the following session settings only after completing
   their corresponding checks: `pulsr.reset_database` to the exact database
   name, `pulsr.reset_ack` to the new migration ID, and both
   `pulsr.reset_writers_stopped` and `pulsr.reset_backup_verified` to `true`.
   Execute the reviewed SQL with error-stop enabled. Its transaction either
   clears all 49 listed operational tables while retaining protected rows, or
   rolls back. It does not grant deployment authorization or stop writers
   itself.
5. Keep writers stopped. Apply the single pending migration with the compatible
   release. Confirm its history row, zero pending model changes, typed accepted
   stops, immutable revisions, all 17 planning triggers and durable queues.
6. Start only the compatible API, verify its image digest and readiness, and
   move all traffic to it. Do not reactivate old tags. Publish the matching
   Client. Restore the reviewed scaling settings and resume integrations.
7. Verify existing sign-in, refresh, roles and preferences without changing any
   password. Inspect new source counts, unresolved assignments, accepted native
   work, queue recovery and route/ETA/fuel reads. Unknown source assignments
   must remain under review. Reimport must not manufacture transfer confirmation
   or actual times. Verify provider-disabled native create/assign/complete using
   the permitted smoke scenario, then close the release record.

The reset also clears operational checkpoints. If the quiesced backup proves
that PulsR already owned the Gmail watch, recover only that registration row
after the reset and schedule its normal bounded recovery. Require an expired
old lease and an absent target row; do not overwrite a newer registration or
restore fleet telemetry checkpoints. Deployment must not register a previously
unowned mailbox. See [Gmail watch ownership](gmail-watch.md).

If cleanup or migration rejects, stop and retain the old data/backup and exact
error. Do not relax the inventory, add CASCADE or automatically reenable old
binaries against a changed schema. After new work exists, rollback means a
reviewed forward repair or full coordinated recovery; downgrade intentionally
refuses to discard accepted work. Restoring a backup discards post-backup work
and must be a separate recovery decision.

## Scope and evidence

`CoreMigrationProbe` exercises the same reset SQL on synthetic data. It verifies
rejection before approval, with another connected client and with an unknown
table; preserved password/role behavior; schema upgrade/downgrade guards; source
bootstrap/replay; immutable history; and independent publication/queue
ownership. Its fixture is removed afterward. These checks do not measure
production throughput and do not certify the current Cloud Run state.

Local release gates and browser fixtures do not establish working authentication
or provider behavior after cutover. Keep those remaining checks explicit in the
release evidence. Accounting, settlements, toll quotes and actual expense
imports remain later product work under the core specification.
