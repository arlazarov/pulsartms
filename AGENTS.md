# Mandatory project rules

Read `docs/ARCHITECTURE.md` before changing server code and `docs/ui-controls.md`
before changing UI or styles. These are requirements, not optional recommendations.

## Execution environment

- Publish or deploy only after the user explicitly requests it or approves a
  deployment. Local implementation and verification do not authorize deployment.
- Work locally by default. An explicit request to implement and publish covers
  releases within that requested scope until completed or revoked; do not ask
  again at each implementation stage. An unrelated earlier cutover does not
  authorize a new release. During iteration use
  affected test groups and existing build caches, with full checks at the
  boundaries required below. See `docs/development/setup.md`.
- Docker is permitted for builds, tooling and deployment, including the existing
  Cloud Build/Cloud Run workflow.
- Local disposable SQL databases are permitted for tests and diagnostics,
  including Docker and Testcontainers. Use isolated test credentials, storage
  and connection settings; never use the working application or production
  database as a disposable fixture. Limit resource use and clean up only
  task-owned test resources. Report PostgreSQL checks as not run when no
  suitable fixture is available; SQLite does not prove PostgreSQL behavior.

## Documentation, comments and logging

- Follow the project's established code style in every change. Before editing,
  check the owning guide, formatter settings and nearby maintained code. Reuse
  existing naming, layout, shared implementations and UI tokens instead of
  introducing a parallel style. Existing violations do not override these rules.
- Prefer namespace imports over fully qualified C# type names in code. Use a
  meaningful type alias when names conflict; keep qualification only when needed
  for unambiguous binding. Do not change type identity to shorten a name.
- Use an 80-column target for all maintained source, styles, markup,
  configuration and documentation, including C#, JavaScript, SCSS and Razor.
  Wrap expressions, attributes and prose without changing behavior or content.
  Do not hand-format generated files; indivisible identifiers, URLs and literal
  contents that cannot safely be split may exceed the target.
- Format maintained C# with the pinned CSharpier tool and two-space indentation.
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

## Shared ownership and duplicate work (application-wide)

- Assign ownership by business responsibility, not by screen. Dispatch owns
  assignments and execution; routing, ETA, fuel and financial rules belong to
  their respective owners behind small layer-appropriate contracts. A shared
  state does not require one central service that depends on every module.
- Keep distinct facts explicit: cargo delivery, completion of truck work and
  GPS passage are not interchangeable. Screens must agree about the same fact
  at the same version, but must not collapse different facts into one status.
- Recalculation and repeated reads are allowed for changed dependencies,
  freshness, recovery or authorization. Name the reason and bound retries,
  concurrency and retained state. Coalesce equivalent work; do not suppress
  necessary validation to claim zero repeated work.
- Before adding a calculation, query, provider call, projection or cache, find
  its existing owner and trace the callers across all affected screens and
  jobs. Record what is reused and why any new work is necessary. This applies
  to every module, including Dispatch, Fleet Map, Messaging and financial flows.
- Keep each business rule in one authoritative owner. Consumers request its
  result and format it; do not copy formulas, validity rules or fallback logic
  into another screen or handler. Share through permitted layer contracts.
- Review the complete request and background chain, not one method in
  isolation. Reuse equivalent reads within an operation, batch collections,
  and coalesce concurrent expensive work for the same scoped dependency key.
  Repeated handler calls must not repeat database/provider reads or full-set
  materialization unnecessarily. Do not assume a cache hit makes work free.
- Read only the projection needed. A scalar or summary must not load,
  deserialize or decompress full geometry, history or large entity graphs.
  Rendering getters must not repeatedly rebuild or sort the same collection.
- Reuse existing caches before adding one. Cross-request reuse requires named
  tenant, authorization, assignment and input-version boundaries as applicable,
  bounded memory, explicit freshness and invalidation after commit. Do not
  remove correctness checks or retain stale results as current to save work.
  Do not introduce a cache or generic framework for trivial operations.
- For changes to shared reads or expensive calculations, review warm, cold
  and overlapping consumers, including background consumers. Add focused
  call-count or work-count regression coverage where practical; document any
  coverage gap. Distinguish calculations, database/provider calls, cache reads
  and materialization. Measure foreground and background cost separately.
- Completion evidence must name the owner, consumers, eliminated repetition
  and checks performed. Do not claim "computed once" from response time or a
  passing functional test alone. Justified repetition (different inputs,
  permissions or freshness) must be explicit. Cosmetic changes do not require
  performance tests, and unchanged passing gates must not be repeated.

## Invariant-first regression review

- Before changing behavior, state the product invariant independently of the
  implementation, its authoritative owner and allowed dependencies. Follow
  the contract matrix in `docs/testing.md#invariant-contract-review`.
- Shared reads and calculations must produce the same per-item result and
  dependency signature for the same authorized inputs, whether read alone,
  reordered, partitioned or with unrelated peers. Batch size, page composition
  and unrelated trucks/companies must not become business dependencies.
  Legitimate aggregation must have an explicit contract and separate tests.
- For affected paths, test both sides: irrelevant changes leave results and
  work requests unchanged; relevant changes invalidate them. Preserve tenant,
  authorization, assignment and version guards. Do not normalize away a real
  dependency merely to make signatures stable.
- Review test expectations against the product invariant. Tests, snapshots and
  current production behavior are not the specification. Never update an
  expectation solely because the implementation produces it. Record the reason
  for changing an old expectation and retain its valid safety assertions.
- For a reproducible bug, demonstrate that the regression fails on the old
  behavior and passes on the correction. If reproduction is unavailable,
  record the limitation and alternative evidence; do not claim red/green proof.
- Changes to queues or their input/demand producers require deterministic checks
  for unchanged-input idempotency, retry preservation, stale-worker completion
  and bounded progress of eligible lower-priority work under sustained arrivals.
  Declare the scheduling assumptions and progress bound; pending forever is not
  success. Never reset production priorities or retries to hide the defect.
- Review affected foreground and background consumers together. Record result
  equivalence and work-count coverage separately. A shared owner alone does not
  prove consumer agreement or absence of repeated work.
- A release gate proves only its tested contract. Close an incident only after
  an authorized, bounded read through the normal owner confirms the user-visible
  result. Health, queue completion and deployment success are not substitutes.
  Record remaining cases and runtime detection gaps with owners and criteria.

## Shared reads and background planning

- Before changing Dispatch, Fleet Map, planning reads or their persistence, read
  `docs/architecture/fleet-efficiency.md` and follow its ownership table.
- Reuse PlanningSummaryReader for shared display reads. Keep heavy fuel/geometry
  reconstruction in the background; do not add per-card database/provider calls
  or page-specific duplicate calculation/cache paths.
- Batch visible-page inputs, coalesce repeated work and preserve bounded payloads.
  Notify shared summaries after successful commit and read-cache invalidation.
  Preserve company/assignment/version guards and retained-display behavior.
- Follow the consistency contract in `docs/architecture/fleet-efficiency.md`
  for any prepared, cached, published or externally sent result: name its
  dependency versions, publish only through its owner after commit, never
  let a late result replace a newer one, keep retained results honestly
  stale, and make external sends idempotent without claiming exactly-once.
  Add a controlled-interleaving regression for each path a change touches.
- For a repeated-query fix, add call-count or data-loading regression coverage
  where practical. Report foreground and background cost separately; a fast HTTP
  response does not prove that total database or provider work decreased.

## Required completion checks

- Continue authorized implementation between coherent stages. Independent
  review gates integration and publication, not every intermediate edit.
  While a review is pending, continue independent work. Pause dependent work
  only for a concrete correctness blocker or a material unresolved product
  decision; explain the exact issue rather than requesting blanket approval.
- Before editing, identify the owning layer and existing shared implementation.
- Keep provider-specific SQL and provider detection in Infrastructure, behind an
  Application interface. Do not construct optional fallback business services.
- Keep financial formulas on the server; Client formats response values only.
- For UI changes, follow `docs/ui-controls.md` and reuse named style tokens.
- Select checks using `docs/testing.md` before editing. During an unfinished
  change, use the smallest relevant test class or filter for feedback. At the
  end of a coherent change, run affected categories, dependent categories and
  architecture checks, using focused filters when a category is expensive.
  Reserve the full suite and long browser/release checks for the final candidate
  being published, once before deployment. Shared contract, persistence, DI,
  authentication or test-infrastructure edits require focused dependency and
  safety regressions during development, not an automatic full-suite run.
  If a long check is essential to establish a specific fix before that boundary,
  explain the concrete reason first and run only that necessary check.
  Build the Client when Razor, Client C#, or Client contracts change.
  Add regression coverage for corrected invariants where practical; cosmetic
  changes alone do not require new behavior tests. Never claim a full pass
  after a narrow run. Every xUnit class must declare a Category trait; new or
  substantially changed classes also declare Kind per `docs/testing.md`.
- Do not repeat a passing gate on unchanged inputs. Main and isolated worktrees
  may run checks in parallel with separate build outputs and isolated database
  fixtures. Coordinate shared resources; serialize only conflicting operations
  or when measured resource contention warrants it. Record the
  candidate revision/diff and checks performed; later relevant changes require
  fresh affected checks and, at publication, the final release gate. Investigate
  flaky failures with controlled signals and bounded reproductions, not loops
  until green. Stop obsolete jobs owned by the task; preserve useful evidence.
- Keep server tests in `Server.Tests`, Client C# tests in `Client.Tests`, and
  JavaScript tests in `Client/tests`. Client C# tests must reference the compiled
  Client project, not linked production source or substitute partial components.
  Keep architecture checks under Architecture with Category=Architecture and
  reusable fixtures under Support. Run `bash test.sh` groups so both .NET test
  assemblies and the required JavaScript checks remain included after moves.
- Report checks not run, unapplied migrations and unmeasured performance claims.
  Passing tests do not prove production performance or complete visual correctness.
- Do not weaken an architectural test or add an exception to accommodate new code.

## Consistency audit and existing data

- For stateful workflow changes, follow
  `docs/architecture/consistency-auditor.md`. Identify existing invalid rows and
  their recovery path; a fix for new writes alone does not resolve old data.
- Keep business audit separate from liveness/readiness. Reuse authoritative
  owners, bounded company-scoped reads and explicit coverage reporting.
- Verify the incident after an authorized release and distinguish tested code,
  deployed code and repaired production state. Preserve execution history.
- Every feature or stateful bug fix must assess auditor coverage. Add or update
  the affected checks and regressions, or document why runtime detection is
  inapplicable and what evidence covers the invariant instead. Record deferred
  gaps with an owner and completion criterion; never label unimplemented
  checks as covered. Follow the coverage review in the auditor guide.

## Cohesion review

- No size limit fails a check; size is only a signal to look. Review owner,
  independent responsibilities, dependencies, public surface, lifecycle and
  cost of change, following `docs/architecture/cohesion-review.md`.
- A partial file does not fix a bloated class; extract a real owner or join
  pieces split only for a count. Do not replace line limits with method or
  dependency limits. Never compress formatting to change a number.
- Layer, dependency, authentication, tenant, style-token and behavior checks
  are unaffected and must not be weakened.
