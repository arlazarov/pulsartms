# Application design composition follow-up — September 10, 2026

## Scope and user review

This follows the [initial foundation and fuel timeline pass](application-design-and-fuel-timeline-2026-09-09.md).
The user correctly identified that restyling the original stacked Dispatch layout
did not reproduce the approved mockups. This pass changes the composition itself,
using the approved Cards, Table, Papers and Fleet Map images as references.
The user also requested supporting information on demand without overloading the
default Cards presentation.

Cards now use an icon-led compact truck header, horizontal load lanes on wide
screens and vertical pickup/delivery timelines. Completed pickups remain visible.
Current has a blue outline; Next uses named purple semantic roles. Rate and both
server-provided RPM values remain visible. Native Details and Mileage & hours
disclosures retain addresses, references, time zones, cycle forecasts and mileage.
The header's route distances also expand explicitly. Disclosures perform no
business writes, provider requests or financial calculations.

Table uses eight columns and keeps the same financial and historical assignment
data. Papers uses folder queues with an inline selected document; explicit
selection focuses the document, background refresh does not. Unsupported load
creation actions were not added. Active/Completed switching is preserved.

Fleet Map now has unified truck identity, unboxed readings, HOS, current-driver
recap and six route groups. Planned-station popups show all four price fields,
paired gauges, distinct order badges and the server purchase cost/edit footer.
Fuel marker colors, geometry and fuel calculations were not changed in this pass.

## Verification

- `bash test.sh all`: 2,121 passed (1,277 Server, 531 Client, 313 Node).
- Strict Release solution build: zero warnings/errors; final Release run passed
  all 1,808 .NET tests.
- JavaScript typecheck/build and SCSS compilation passed.
- `composition-final.ARw5Nf`: 234 published assets and seven JavaScript dependency
  graphs verified. Offline fuel editor 4/4, HOS/ETA 8/8 and station popup 4/4 passed.
- Frozen-source GPU/stop popup probe: 40/40 passed. This probe uses production
  source and compiled CSS, not the staged Blazor assemblies.
- Browser screenshots exposed and corrected a planned-station title/badge overlap,
  route-panel selectors leaking into stop popup headers, loading height shifts and
  narrow text-zoom disclosure overflow. Layout tests were not treated as proof of
  visual fidelity; desktop/mobile screenshots were inspected against the references.
- A final Dispatch-only SCSS correction places Details in normal flow when the
  card is too narrow for its header target. Its fresh strict Client publish is
  `artifacts/composition-polish.LdMClX/publish/wwwroot`; the 234-asset/seven-graph
  verification and `bash test.sh styles` (including architecture) passed.
- The final staged UI matrix passed all 44 page cases at 1440/390px in light/dark
  and 100%/200% text sizes, plus wide Dispatch cases at 2344px. It captures the
  default compact composition separately, opens every supporting disclosure with
  the keyboard and verifies all retained values and expanded geometry. There were
  no browser errors, unexpected requests or geometry failures. Evidence:
  `Client/test-results/ui-composition-polish/report.json`.

One intermediate Release run of the unchanged dense fuel-geometry allocation test
reported 7,152 bytes for 40 matches and failed its existing threshold. The Debug
full suite and subsequent complete Release run passed without changing production
geometry code or the test threshold. The cause remains undetermined; this is not
claimed fixed. Evidence is preserved in
`artifacts/composition-final-dotnet-allocation-failure.log`.

## Handoff and limits

Local API/client were restarted on 5086/5067 with the new Release build. Migration
application, synchronization and Gmail background maintenance were disabled.
There was no production deployment or database migration in this follow-up.
PostgreSQL execution, live provider/fuel calculation, authenticated map soak and
production performance were not tested. Offline fixtures do not establish those
properties. The old in-app tab remained on a connection-error document after the
restart; automated browser access to that document was blocked, so manual refresh
was requested. HTTP localhost returned 200.
The stylesheet returned by localhost was hash-compared with the final staged CSS
and matched; no later source/style changes were made.
