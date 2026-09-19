# Verifying that a backup can be restored

Status: a procedure to run, not a record of a run. No full restore of this
database has been tested. `pg_restore --list` succeeding proves the archive is
readable, not that the system runs on it.

Run this before the next schema change, and again whenever the backup method,
the PostgreSQL version or the protected identity boundary changes. Record each
run in `docs/archive` with its date, the archive it used and what failed.

## What a restore has to prove

A restore is verified when the application starts against the restored
database, a user signs in, and the data the business depends on is present and
readable. Three of those are easy to skip and each has failed elsewhere:

- **Sign-in.** Data Protection keys are stored in the database with the
  application name `AMFTMS` and are not encrypted. A restore that loses the key
  ring, or that is opened by an application with a different name, leaves
  password hashes intact and every session and integration credential
  unreadable. Verifying a restore without signing in proves nothing about it.
- **Integration credentials.** They are protected with the same key ring. If
  they do not decrypt, the restored system cannot reach any provider, which a
  row count will not show.
- **Border personal data.** Encrypted per crossing. The same applies.

## Before starting

Never restore into the working database, and never point a restored instance at
it. The restore target is a separate database that nothing else uses. Confirm
which database the target connection names before running anything.

Take the archive to verify, its size and its SHA-256, and confirm the digest
matches what was recorded when it was taken. A backup whose digest has drifted
is the thing under test, not a tool for the test.

Match the client to the server: `pg_restore` from a version older than the
server can silently omit objects.

## The procedure

1. Create an empty target database with its own role. The role needs schema
   creation on that database and nothing anywhere else.
2. Restore the archive into it. Record the exact command, the warning count and
   the exit status. Warnings are part of the result; a restore that emits them
   is not verified until each is explained.
3. Compare the restored schema against the model. The schema must already carry
   every applied migration: a restore is not the place to run pending ones.
4. Count the protected identity boundary: Users, AspNetUsers, AspNetUserClaims,
   AspNetUserLogins, AspNetUserTokens, AspNetUserRoles, AspNetRoles,
   AspNetRoleClaims and DataProtectionKeys. Compare each against the source.
   Never print their values.
5. Start the application against the restored database with
   `Database:ApplyMigrations=false`, `BackgroundOperations:Enabled=false` and no
   dispatch import, so nothing writes and no provider is called.
6. Sign in as a real account. This is the check that proves the key ring
   survived. A failure here is a failed restore however complete the rows are.
7. Read one load with its stops, one execution leg with its accepted stops and
   its revision history, and one saved fuel plan. Confirm the numbers match the
   source.
8. Open Settings and confirm the integration credentials decrypt. Do not save.
9. Stop the application and drop the target database.

## Recording the result

A run is evidence only with its date, the archive identity and digest, the
client and server versions, the restore warnings, the identity counts, whether
sign-in succeeded and whether credentials decrypted. State what was not
checked. A restore verified once says nothing about the next backup; the point
of the procedure is that it is repeated.

## What this does not cover

Point-in-time recovery, the recovery window the provider actually retains, and
how much work a restore would discard. Those are separate questions about the
backup schedule, and none of them is answered by a successful restore.
