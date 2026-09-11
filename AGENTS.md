# Mandatory project rules

Read `docs/ARCHITECTURE.md` before changing server code and `docs/ui-controls.md`
before changing UI or styles. These are requirements, not optional recommendations.

## Execution environment

- Docker is permitted for builds, tooling and deployment, including the existing
  Cloud Build/Cloud Run workflow.
- Do not deploy or start local SQL database servers in Docker, through
  Testcontainers or through a substitute container runtime, even for disposable
  tests or diagnostics.
- Database tests require a safe, isolated fixture that respects this restriction.
  Never substitute the application or production database for a disposable test
  database. Report PostgreSQL checks as not run when no suitable fixture is
  available; do not automatically install a host database server as a workaround.

## Documentation, comments and logging

- Temporary builds and diagnostics must use `node scripts/artifacts.mjs run
  scratch -- COMMAND ...` (or the fixed `diagnostic` kind), with `{artifacts}` in
  output arguments. Do not create new task-named build directories under
  `artifacts`. Reuse `bash test.sh` for its single build cache. Browser/release
  scripts manage their own outputs. Pin required evidence with `.keep` inside
  its managed run; unpinned disposable outputs are subject to automatic retention.
  See `docs/development/artifact-retention.md`. Do not store secrets or unique
  source files in disposable output directories.

- Use `docs/README.md` to find maintained guides. Put dated audits, experiments and
  release evidence in `docs/archive`, not alongside current operating instructions.
  Update inbound and relative links when moving documentation.
- All maintained documentation and source comments must be in English.
- Keep comments minimal: explain only non-obvious invariants, security boundaries,
  compatibility constraints or performance trade-offs. Do not narrate obvious code.
- Use structured logging with stable templates and operation/trace identifiers.
  Never log credentials, tokens, request bodies, profile data or raw provider payloads.
- Log an unexpected failure once at its HTTP or background-job boundary. Expected
  cancellation is not an error; repeated normal polling must not generate noisy logs.

## Layer boundaries

- Application must not know API or concrete Infrastructure implementations.
  It accesses Infrastructure capabilities only through interfaces declared in Application.
- Infrastructure must not know API. It implements Application interfaces and
  interacts with Application only through interfaces, not concrete application
  services, handlers, or direct construction of application commands/queries.
- API may reference Application and Infrastructure in the composition root solely
  to register dependencies. Outside composition, API interacts only with
  Application through MediatR commands/queries.
- API must not know Domain or access persistence. Controllers must not inject
  application services, Infrastructure implementations, or database contexts;
  handlers own application checks and business logic.
- Interface contracts may expose the data types required by those contracts;
  this does not permit calling concrete implementations across layers.

Existing violations are technical debt, not examples to follow. Do not silently
relax these boundaries to make dependency injection or a background job easier.
These rules supersede older guidance allowing Infrastructure workers to construct
Application commands directly.

## Required completion checks

- Before editing, identify the owning layer and existing shared implementation.
- Keep provider-specific SQL and provider detection in Infrastructure, behind an
  Application interface. Do not construct optional fallback business services.
- Keep financial formulas on the server; Client formats response values only.
- For UI changes, follow `docs/ui-controls.md` and reuse named style tokens.
- Select checks using `docs/testing.md` before editing. During iteration, run the
  affected categories and their dependent categories, plus architecture checks.
  Run the full suite before deployment and after shared contract, persistence,
  dependency-injection, authentication, or test-infrastructure changes. Build the
  Client when Razor, Client C#, or Client contracts change. Add a regression check
  for each corrected invariant where practical. Do not claim a full pass after a
  category-only run. Every xUnit test class must declare a Category trait. New or
  substantially changed classes must also declare a Kind trait according to
  `docs/testing.md`; Kind must not replace the feature Category.
- Keep server tests in `Server.Tests`, Client C# tests in `Client.Tests`, and
  JavaScript tests in `Client/tests`. Client C# tests must reference the compiled
  Client project, not linked production source or substitute partial components.
  Keep architecture checks under Architecture with Category=Architecture and
  reusable fixtures under Support. Run `bash test.sh` groups so both .NET test
  assemblies and the required JavaScript checks remain included after moves.
- Report checks not run, unapplied migrations and unmeasured performance claims.
  Passing tests do not prove production performance or complete visual correctness.
- Do not weaken an architectural test or add an exception to accommodate new code.
