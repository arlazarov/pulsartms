# Border preparation PostgreSQL probe

This tool verifies the shipment and Border persistence boundary without using
application data as a disposable fixture. Read `docs/testing.md` first.

Supply the connection through `PULSR_PREPARATION_CONNECTION`; never pass it on
the command line or put credentials in retained diagnostics.

- `--fixture` requires role `pulsr_test_runner`, a database matching
  `pulsr_core_fixture_[0-9a-f]{32}`, and an initially empty public schema.
  It applies migrations and tests synthetic saves, protected person data,
  idempotency and stale revision rejection. Its finally block drops and
  recreates that disposable public schema. Never use an application database.
- `--apply-development` requires database `pulsr_development` and role
  `pulsr_developer`. It permits only the preparation migration as pending,
  applies it and seeds no data. It does not clean the database.

Build and run through `node scripts/artifacts.mjs run scratch -- COMMAND ...`
(or `diagnostic` for the run), using `{artifacts}` for output paths, as required
by the project. Neither mode authorizes production migration or deployment.
