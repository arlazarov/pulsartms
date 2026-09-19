# Core rebuild phase 1E: ETA consumes complete work snapshots

Date: 2026-09-16. Status: implemented locally; full-suite gate has an unresolved failure.
No deployment, migration or application-database experiment was performed.

## Delivered behavior

ETA preparation now reads TruckItinerarySnapshot. The Board boundary supplies
truck IDs only. Reversed, incomplete or forged screen load identities cannot
select the calculation root, omit accepted work or replace assignment facts.
The optional ordered-load input has been removed.

TruckItineraryReader.ReadManyAsync shares candidate selection, legacy/native
hydration and dependency reads across requested trucks. Each snapshot retains
only its relevant evidence; single and batch signatures agree. The shared reader
still uses a lightweight fleet identity map for number-only assignments.

EtaChainInputsService no longer loads Dispatch assignments, queries truck-driver
assignments or calls the cached ResolveAssignmentAsync path. Driver external IDs
are read for assignments already captured in the snapshot. EtaWorkProjection is
an internal, pure bridge to existing route algorithms, which still accept
Dispatch.
It preserves visit IDs, selected truck path, manual operations, completion
revisions and source-address verification/retry facts. No alternate writer or
new business table was introduced.

Each usable ETA description retains the complete snapshot and typed exclusions:
legacy work not assigned, upcoming overdue work, inactive native work,
unsupported
native continuation, unresolved work, work blocked by an earlier boundary, and a
completed matching saved root. Existing current/upcoming calculation limits are
preserved separately from complete input scope.

Explicitly corrected behavior:

- A native active assignment stored in the database takes its canonical
  priority,
  even when a supplied screen list puts a legacy load first.
- Malformed or source-review-blocked native current work cannot silently fall
  through to a legacy root.
- A changed appointment on excluded planned work changes ETA InputHash. The
  retained full itinerary participates in freshness checks, while GeometryHash
  remains independent when route inputs have not changed.

When no usable root remains, ETA returns no description. The Execution snapshot
query retains the unresolved work and its problems for inspection.

## Consistency and remaining compatibility

Each description uses a fresh IExecutionReadScope for operational work, driver
identity, saved-route metadata, future versions and predecessor evidence.
Revalidation rejects an older caller transaction. Provider/HOS work, route
calculation and result publication run outside that read transaction.
Existing profile/settings caches retain their own policy; the database snapshot
does not turn them or telemetry into transactional values.

Saved-root owner, leg/revision and input-hash checks remain. Future connections
still require exact saved predecessor, road signatures and anchors. The existing
forecast store retains its newer-only/revision publication guards. A fresh
comparison does not eliminate every legacy read-to-write race or establish
cross-process atomicity.

Historical predecessor reads remain in DeadheadHistoryService and share the read
transaction. They include completed work and unknown-start evidence beyond the
remaining itinerary, and may read overlapping facts. Using only remaining
segments would lose those guards. This stage removes repeated assignment
resolution in ETA preparation, not every overlapping historical query.

The immutable model now includes normalized source addresses and verification
retry facts, plus manual operation inputs. It contains no raw provider payload,
tracked entity or Dispatch screen DTO. Route algorithms still consume the
internal
compatibility projection; their native input contract is a later migration.

## Verification

Added 11 server cases covering screen-input independence, excluded-work
freshness, overdue classification, verified/retry address evidence, saved-route
signature parity, cached-assignment replacement, fresh-transaction revalidation,
manual truck starts, malformed/source-review native roots and isolated batch
signatures. Existing native-cutoff tests now create real stored native assignments
instead of fabricating them only in screen DTOs. The architecture guard prohibits
ETA from querying Dispatch/Truck assignments or resolving assignments again.

Affected-category `bash test.sh dispatch routing` passed:

- Server.Tests: 1,196 passed.
- Client.Tests: 625 passed.
- JavaScript architecture: 52 passed.

Two `bash test.sh all` attempts each finished with one failure in the unchanged
FuelSearchGeometryAllocationTests 50,001-points case. The first measured 8,520
bytes and the second 7,248 against the existing 4,096-byte bound. Each attempt
passed the other 2,246 server cases and all 1,012 Client cases, with none skipped.
The same test family had an intermittent full-suite failure during phase 1C.
The cause has not been established; neither the test nor fuel production code
was changed. This is an unresolved full-suite gate, not a passing full run.

Both allocation cases passed in a separate isolated run, each measuring 1,280
bytes for forty matches. That result does not cancel the full-suite failures.
The runner stops before Node after a .NET failure, so `npm test --prefix Client`
was run separately: all 561 JavaScript cases passed, none failed or skipped.

Pinned CSharpier, scoped whitespace and documentation links passed. A pre-edit
hash baseline confirms 18 intended changed/new files, no removed files and no
changes elsewhere in the existing dirty worktree. No Client sources changed.

Managed local evidence is pinned under `artifacts/managed/diagnostic-XvdZNk`;
the isolated allocation test result is in `artifacts/managed/diagnostic-Gdf85p`.
Initial iterations exposed two removed-overload call sites and test-fixture
issues
with duplicate external truck IDs, decimal serialization before/after SQLite
persistence and an explicitly added stop's tracking state. These were corrected;
no assertion, architecture rule or production validation was weakened.

No isolated PostgreSQL fixture was available. PostgreSQL execution/isolation,
concurrency under load and production performance were not measured. Browser and
release checks were not run. Client UI and public contracts were not changed.
No migrations were authored or applied, and nothing was deployed.

## Next bounded slice

Establish and correct the intermittent allocation-test cause before claiming a
passing full-suite gate. The next consumer migration is saved truck preview and
live planning on the same complete snapshot. Make
consumer calculation limits explicit, preserve route owner/stop identity and
remove their remaining dependence on screen-selected work. Then migrate the fuel
horizon and consolidate historical route inputs. Native successor forecasting,
normalized visit storage and financial evidence remain separate work.

See the [core specification][spec] and [acceptance scenarios][scenarios].

[spec]: ../../architecture/core-rebuild.md
[scenarios]: ../../architecture/core-rebuild-scenarios.md
