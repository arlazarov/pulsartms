# Shared UI folders and dependency cleanup — September 11, 2026

## Findings and changes

The audit found reusable components under page ownership: Fleet Map used
Dispatch's `DriverHours` and `DriverNextRecap`, while the shared load dialog used
Dispatch's stop and cycle components. The planning response also referenced a
HOS DTO declared inside `Pages/Dispatch`, and Dispatch consumed Fleet Map DTOs.

Moved reusable application UI and its display helpers into these shared groups:

- `DriverStatus`: HOS clocks, current duty, next recap, arrival and stop hours.
- `Fuel`: editor, gauge, telemetry reading and recalculation button.
- `Dispatch`: shared load dialog, stop/cycle details, load numbering and display
  helpers used by the dialog and Dispatch views.
- `Search`: search input and suggestion model.
- `Trucks`: illustration, camera and telemetry tones.

Moved Dispatch response DTOs into `Models/DTO/Dispatch`, map DTOs into
`Models/DTO/Fleet` and `DriverHosClocks` into `Models/DTO/Planning`. Split the
former `FleetDtos.cs` collection into named files, with the response envelope in
`Models/DTO`. Updated namespaces, Razor imports, consumers and test source paths.
Removed the unused page import from the shared search input. No compatibility
copies or forwarding components remain at the old paths.

Page-specific card/table/paper views, map selection/detail coordination and local
components remain under their page features. Generic table/form/popup controls
remain in `Components`. The audit's scope was Client component/model placement
and dependencies, not a second full server or SCSS audit. Runtime polling,
selection, calculation, JSON member names and visual styling were not changed.

## Regression protection and verification

Added four architecture checks: common layers cannot depend on page namespaces;
shared namespaces follow folders; DTOs cannot depend on UI components; page-local
Razor components cannot be consumed by another page feature or shared UI.

- `bash test.sh all`: 1,429 server, 635 Client C# and 416 JavaScript tests passed
  (2,480 total; no runner skips).
- Strict isolated Release Client publish completed.
- Staged UI smoke: 44 page checks passed across light/dark themes, desktop/mobile
  layouts and 100%/200% root font sizes, including shared load dialogs.
- Staged fuel editor smoke: eight scenarios passed, including HOS responsive
  sizing, both themes and mobile editor interactions.

Evidence is disposable under `artifacts/managed/diagnostic-DtS50E`,
`scratch-oq8EPX`, `browser-ui-m5QBDM` and `browser-fuel-editor-zswA9m`.
Browser checks use intercepted fixtures, not live integrations or database writes.
No PostgreSQL execution checks, migrations, deployment or localhost restart were
performed. The separate HOS lifecycle browser suite was not rerun.

## Component-local folder follow-up

At the user's request, all 18 shared Razor/code-behind pairs now have their own
component directory beneath the existing responsibility group. For example,
`Shared/DriverStatus/DriverHours/` contains `DriverHours.razor` and
`DriverHours.razor.cs`. Namespaces, imports, qualified references and source-path
checks were updated. Standalone helpers remain in their owning groups; page and
generic-control folders were not reorganized in this follow-up.

The existing namespace/dependency checks remain intact, with an additional check
requiring each shared code-behind file and its Razor sibling to live in their
named component folder. Strict isolated Client build passed with zero warnings
and errors. `bash test.sh all` passed 1,429 server, 635 Client C# and 417 JavaScript
tests (2,481 total). Evidence: `artifacts/managed/scratch-EUlgZl` and
`artifacts/managed/diagnostic-VP7eTD`.

This follow-up changed file organization, not UI behavior or SCSS. Browser and
PostgreSQL checks were not rerun; no deployment or localhost restart was performed.
