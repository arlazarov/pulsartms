# AMFTMS review — September 5, 2026

Historical assessment, translated and condensed into English. Findings describe
the working copy on that date, not the current release. Several items have since
been addressed. Consult ARCHITECTURE.md and DEVELOPMENT.md for current rules.

## Scope and structure

Reviewed C#, Razor, JavaScript, SCSS, configuration, EF migrations and deployment
scripts, including uncommitted work. Generated build outputs and EF snapshots are
not independent business logic. This was a code review with limited local checks,
not verification of every workflow against live providers. See TMS-FILE-INVENTORY.md.

The stack was Blazor WebAssembly, ASP.NET Core 10, MediatR, FluentValidation,
EF Core and PostgreSQL. The early TMS had a fleet map and integrations, but not a
complete transportation lifecycle. A wholesale rewrite was not justified.

| Area | Responsibility and observation |
| --- | --- |
| Client/Pages | Login, Home, Users, FleetMap; small pages with Razor/code-behind separation; Home was empty |
| Client/Components | Reusable forms, tables, pagination and popups; loading/accessibility gaps |
| Client/Services | HTTP, bearer, refresh, storage and polling; a separate map response format |
| Client/wwwroot/js/fleetMap | Layers, markers, popups and animation; textContent and disposal used |
| Client/Styles | Palette, scales, components, layouts and pages; CSS build was separate |
| Domain | Users, Fleet, Dispatch and Fuel entities |
| Application | Feature-oriented commands, queries, models and interfaces; explicit EF dependency |
| Infrastructure | Identity, PostgreSQL and external providers behind interfaces |
| API | Controllers, DI and middleware; authenticated fallback policy |

Request flow: Razor → HTTP → controller → MediatR → handler → database/provider
interface. Client had no server assembly dependency or generated shared contracts.

## Functionality at the review date

Authentication supported login, refresh, logout and security-stamp revocation;
roles, a current-user profile and reliable deactivation were missing. Users had
CRUD and pagination but lacked administrative constraints and activity/search UI.
Fleet imported Samsara equipment and assignments but lacked dedicated screens,
manual management and assignment history.

The map showed trucks, drivers, trailers, speed and fuel with polling and delayed
animation. Historical tracks, dispatch links, truck search and telemetry age were
missing. Selecting a date changed fuel prices, not truck history.

Fuel supported Gmail BVD CSV, Places lookup, dated prices, colors and IFTA.
Import correctness, fuel transactions and import-management UI were incomplete.
Dispatch imported Torque loads/stops/customers and equipment links, but had no
complete UI, local editing, status workflow or planning. Dashboard was only a shell.
Deployment used Docker, Cloud Build/Run, Firebase and EF migrations, without a
test pipeline, health checks or synchronization operations documentation.

FuelTransaction existed only as an entity/table. IFTA rate subtraction was not IFTA
reporting or jurisdiction mileage accounting. Billing, settlements, POD/documents,
maintenance and a complete transport workflow were absent.

## Original priority findings

1. **P1 — user administration permissions.** Authenticated callers could create,
   delete or change other accounts because neither endpoint policies nor handlers
   checked the administrative role. Hiding controls was insufficient.
2. **P1 — secrets in tracked configuration.** Integration keys and a database
   password appeared in appsettings. Their validity/repository exposure was not
   tested. Move them out and rotate exposed values; deleting files cannot revoke
   secrets. Public browser Maps keys require separate restriction checks.
3. **P1 — first station import lost prices.** Station creation remained in the
   change tracker while discount synchronization queried persisted rows. The email
   could be marked processed without prices. Save intermediate state inside one
   transaction or share the tracked station set; test first import and replay.
4. **P1 — unauthenticated Gmail webhook.** AllowAnonymous lacked sender identity,
   audience and notification validation. An arbitrary POST could trigger mailbox,
   Places and database work. Cloud Run protections were not assessed.
5. **P1 — incomplete deactivation.** LockoutEnabled could remain false and active
   domain state/security-stamp revocation were not checked consistently. Existing
   sessions could survive account deactivation.
6. **P1 — multiple attachments conflicted.** Each attachment added an import
   marker but GmailMessageId was unique. Use one transactional email marker or an
   explicit email/attachment identity, with concurrent import coordination.
7. **P2 — IFTA contract gaps.** EffectiveTo and unit conversion were not enforced
   consistently. Require explicit period/unit rules; this was an implementation
   observation, not a tax opinion.
8. **P2 — incomplete imported station fields.** Savings/Product were not populated;
   Country/PostalCode and retrying missing geocodes were incomplete.
9. **P2 — telemetry stream pagination.** Only the first page reached playback,
   despite provider HasNextPage/EndCursor support.
10. **P2 — per-browser upstream work.** Polls fetched snapshots/streams repeatedly;
    shared telemetry, request coalescing, 429 backoff and last-good snapshots were missing.
11. **P2 — stale stop relationships.** Unchanged source fields skipped FK
    reconciliation after fleet data arrived; changed stops were recreated with new IDs.
12. **P2 — inconsistent errors and validation.** Missing global exception mapping,
    parsing empty 401/ProblemDetails as response DTOs, stuck login loading state,
    unbounded paging, unordered pagination and invalid-quarter exceptions.
13. **P2 — non-atomic identity/domain updates.** Multiple saves lacked an explicit
    shared transaction and the domain identity relationship had no configured FK.
14. **P2 — unspecified shared Data Protection keys.** Tokens could fail across
    instances/restarts without persistent common keys. Production behavior was not tested.

## Other original limitations

- Torque imported a seven-day lookback/lookahead window; older changes and
  disappeared/cancelled records needed policy. Pagination lacked empty-page guards.
- Dispatch lists loaded all stops; filtering/paging and stop-level truck assignments
  needed completion.
- Driver/customer matching used name heuristics without a manual reconciliation
  queue or stable external customer link.
- Fleet synchronization used transactions and post-commit cache invalidation, but
  disappearance, duplicates and concurrent snapshots needed explicit handling.
- One driver/trailer per truck did not model team drivers or assignment history;
  stops already had a co-driver field.
- Gmail searched two days; watch renewal and long-outage recovery were not evident.
- Same-day station currencies had no explicit UI selection.
- Map external truck IDs did not match dispatch API internal GUIDs.
- Client auth state inferred login from token presence; no profile, roles or local
  expiry handling existed. Concurrent refresh was not coordinated.
- Logout revoked all sessions through the security stamp; preserve or explicitly
  change that behavior.
- The users grid could clip below its 660px minimum. Popups lacked complete dialog
  semantics, focus trapping, Escape and focus restoration. No browser visual review ran.
- SCSS depended on a global Sass watcher and was not reliably included in deployment.
- Naming inconsistencies included SyncDispatche/GetDispatche, User/Users and DTO/Dto;
  duplicated response projections and unused models/styles remained.
- No automated test project or cloud test step existed. migrate.sh both created
  and applied a migration; it was not a harmless file generator.

## Checks performed then

The solution built without warnings/errors. EF reported no pending model changes;
that did not confirm live migration state. All 13 JS files passed Node syntax checks
and Sass compiled to temporary output.

A temporary SQLite harness reproduced one new station, zero prices and one email
marker using the real import handler with fake CSV/Places providers. Identity DI
reported RequiredLength=4 and AllowedForNewUsers=false. No real database/provider
calls were used by that harness.

OpenAPI returned 200; protected anonymous GETs returned 401 and empty login returned
400 validation errors. Full login, real user mutation, synchronization, Gmail watch,
tax providers and production were not verified.

## Original recommended sequence

Fix permissions, deactivation, secrets, webhook validation and transactional
idempotent imports first. Add tests, consistent errors/DTOs, validation and pagination.
Complete Dispatch from list to stops to map, keeping Torque's ownership explicit.
Then add synchronization status, retries, checkpoints and shared telemetry before
expanding dashboards, transactions, documents and financial workflows.
