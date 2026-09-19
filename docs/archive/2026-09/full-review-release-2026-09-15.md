# Full release review and optimization — September 15, 2026

## Scope and implementation review

This release includes the pending [arrival-date fuel and Dispatch map changes](arrival-fuel-and-dispatch-map-2026-09-15.md).
Architecture and UI ownership were reviewed against the maintained project guides.
The entire working tree is covered by the automated release gate; this is not a
claim that every pre-existing line of code received a new manual audit.

- Removed repeated full-station quote conversion across candidate chains within
  `FuelPriceCalendar`. One calculation retains one converted dictionary per date
  for the current IFTA/exchange-rate basis. Changing that basis clears conversions.
  Purchases still receive independent stop instances. Cancellation is checked
  before conversion and for each candidate; output lists reserve candidate capacity.
- Added regressions for changed IFTA/FX inputs, independent purchase objects and
  cancellation without a station read. The existing future-price, missing-quote,
  manual-plan and revision checks remain unchanged.
- Added the Dispatch road response to Client source-generated JSON metadata.
- Verified that fuel refresh uses existing read caching and lease-owned scheduling;
  it does not introduce another polling loop or routing-provider request.
- Saved Dispatch roads retain input/anchor checks and separate native sections.
  Client read cancellation and request ownership reject superseded responses.
  Shared loading indicators reuse existing tokens and reduced-motion handling.

The conversion optimization removes repeated work by construction. Production
latency, .NET working set and monetary savings have not been measured.

## Verification and deployment

- `bash test.sh fuel`: 227 Client C#, 1,073 Server C# and 47 Node checks passed.
- `PULSARTMS_RELEASE_UI=1 bash verify-release.sh`: 550 Node, 1,007 Client C#,
  1,936 Server C# passed; none skipped. Strict build had zero warnings/errors.
  Published integrity verified 273 assets and seven JS entry graphs.
- Offline release UI: 52 pages across 12 viewport/theme/text-size cases passed.
  Artifact: `artifacts/managed/release-AW1J97/publish/wwwroot`.
  Report: `artifacts/managed/browser-ui-FezICC/report.json`.
- `npm audit --prefix Client --omit=dev --audit-level=high`: no known production
  npm dependency vulnerabilities reported. This is not a penetration test.
- Extra browser probes initially exposed stale tests: separate sidebar resource
  links, removed detailed-cycle editor content, and a missing saved-map fixture.
  The scenarios now exercise one Fleet entry with three internal tabs, row ETA
  warnings without duplicate editor forecasts, and a provider-free map fixture.
  Directory navigation waits for the target tabs to mount before counting them.
- The corrected lifecycle probe passed 16 SPA navigation cycles. Route polling
  stopped on map disposal. Each post-GC sample retained four documents and 36
  listeners; nodes settled at 1,249 with brief 1,260 samples. JS heap rose from
  6,000,988 to 6,546,888 bytes over the run; that observation does not establish
  either unlimited leakage or long-term stabilization. It does not measure managed
  WASM/server heap or Google/GPU resources. Report:
  `artifacts/managed/browser-hours-forecast-x29lT9/report.json`.
- The corrected Dispatch workspace probe passed all 11 scenarios, without browser
  errors or unexpected requests: `artifacts/managed/browser-ui-eYtX7d/report.json`.
  Desktop and phone screenshots were inspected for the retained table/editor and
  mobile pane layout. Synthetic uploads and writes remained in fixture memory.
- After the browser fixture updates, `bash test.sh all` passed all 1,007 Client C#,
  1,936 Server C# and 550 Node checks again; Prettier and `git diff --check` passed.

## Published release

- Cloud Build `01c98ea0-c156-40ee-83b6-70fe2ee72d19` succeeded, including 550 Node,
  1,007 Client and 1,936 Server tests and a strict build. Its Linux Client artifact
  check covered 255 assets/seven JS entry graphs; Hosting uses the separately
  verified local artifact listed above, not the Cloud Build Client output.
- API image digest:
  `sha256:5b508d8f1735482989766b6496775073fc9b117a36709a30c47b20c02805968f`.
- Cloud Run `amftms-api-00118-x4j` is Ready/Active and receives 100% traffic.
  The former `amftms-api-00117-g9t` is retired with TrafficShutDown. Its background
  lease was released, then acquired by the new owner; planning, dispatch, location,
  telemetry and assignment checkpoints subsequently advanced with zero failures.
- Firebase Hosting `amftms` published the 273-file local artifact. Production
  HTML, versioned CSS, Dispatch JS and `Client.hd6y12fvps.wasm` SHA-256 hashes
  match it exactly. HTML/CSS/JS aliases revalidate; the fingerprinted WASM is
  immutable. Direct API liveness and the Hosting API rewrite return HTTP 200.
- Fresh authenticated browser pages loaded AMF1377 and Fleet's nine trucks,
  with one Fleet sidebar entry and three internal tabs, without permission errors.
  No production forms were submitted or documents uploaded during verification.

## Live limitation discovered

AMF1377 still reports unavailable saved road sections. Read-only database inspection
confirmed **neither native section has a DispatchBaseRoute**. Completed 11005 has
no native saved route plan; active 54777 has a revision-matched current-position
plan with one destination, one road leg and no reference route. That current road
cannot stand in for the complete historical pickup/transfer/delivery itinerary.
The map's full-section reader therefore excludes it rather than drawing an
invented connection. Restoring complete geometry remains unresolved; this release
must not be described as proving full live Dispatch road coverage. Provider route
reconstruction and production load/assignment edits were not performed as tests.

## Database boundary

Read-only production schema inspection found 39 applied migrations, latest
`20260914214701_AddStopCorrections`, and no invalid workspace indexes. This release
adds no migration; fuel metadata is additive saved-plan JSON. No database server
was started, and production was not used as a disposable test fixture.
Real PostgreSQL fixture tests are not run because no approved isolated fixture
is available; SQLite integration tests do not establish PostgreSQL behavior.
