# Manual stop operations — September 11, 2026

Implemented separate manual action and after-stop state, preserved independently
of imported job, assignments, cargo and actual completion. The shared Details
editor serves the load dialog and full load page. Dispatch summaries, Table,
Papers, current map stops and future-stop inspectors consume effective labels.

The truck itinerary excludes a personal prefix; bobtail/empty movement remains in
truck routing and fuel. Geometry is reused when included locations/profile are
unchanged. ETA/fuel signatures include operation changes. Equipment actions do
not inherit cargo-service time or receive assumed rest for appointment waits.
See [maintained rules](../../features/route-planning.md#stop-operations).

Verification on the final source:

- `bash test.sh all`: 1,507 server tests, 674 Client C# tests, 426 Node tests passed.
- Strict Client build: zero warnings/errors. SCSS and JavaScript assets compiled.
- Staged Blazor UI smoke passed at 1440/390/2344px, light/dark, 100%/200% root
  text size. The operation form was opened, Driver start selected, No truck
  verified, and cancelled without writes. Desktop/mobile screenshots inspected.
- EF pending-model check: no changes since the migration.
- Isolated PostgreSQL tests were not run: no safe fixture is available.
  SQLite integration tests are not PostgreSQL proof.

The first browser run found an inaccessible exact label lookup for nested select
labels; explicit label/control IDs corrected it and the full browser run passed.
An early full test iteration exposed legacy entity-reference assumptions when
projecting stop operations; legacy routes now retain their existing stop references.
An allocation check failed once during that iteration and passed on subsequent
full runs without changing its bound or implementation.

Production deployment completed on September 11, 2026:

- Local `bash verify-release.sh` passed: 1,507 server tests, 674 Client C# tests,
  426 Node tests, strict builds and 252 published assets / seven JavaScript graphs.
- Cloud Build `799c5e35-1c1e-4777-bf15-882f3b2eac11` succeeded.
- API image digest: `sha256:3c24309f3043146688785d51a3d7b4f809c1e6892972ad342111f95a4981e5ed`.
- Cloud Run revision `amftms-api-00102-p7j` is ready and serves 100% of traffic.
- Startup applied `20260911202945_AddStopOperations`; the public liveness endpoint
  returned `Healthy` after startup.
- Firebase Hosting published the exact verified `release-UoRDCa/publish/wwwroot`
  artifact successfully. No manual production stop records were changed.

Fuel uses the configured truck MPG and routing profile, not invented
bobtail/empty coefficients or inferred cargo weights. Personal gaps inside a
continuous truck route require separate assignments. Browser tests use offline
fixtures, not live providers or GPU route rendering; production performance is
unmeasured.

## Supplemental details follow-up

The dialog and full load page now suppress imported cargo fields for driver-only
and explicit non-cargo operations. Pickup/delivery retain action-related cargo,
including delivery followed by Empty. Raw source values remain unchanged; notes
and references remain accessible. Empty disclosures are omitted.

Expanding More details preserves card width, groups facts on a quiet surface and
keeps notes below. Operation/completion buttons use a wrapping footer with
full-width editing forms. This replaces the earlier full-grid expansion.

`bash test.sh dispatch styles` passed. The final release gate passed 1,507 server,
687 Client C# and 427 Node tests, strict builds and artifact checks. Offline UI
smoke passed all 12 theme/width/text-scale cases; final desktop/mobile screenshots
were inspected. Artifact: `release-brcdEl/publish/wwwroot`; browser report:
`browser-ui-ctbLFT/report.json` under `artifacts/managed`. Live authenticated UI
checks were not run. This follow-up changes no server code or database schema.
Firebase Hosting subsequently published the exact verified `release-brcdEl` artifact
on September 11, 2026. Asset integrity was rechecked before publication; the public
index SHA-256 matches the staged index and the public API liveness is Healthy.
No API revision or migration was deployed for this presentation-only follow-up.
