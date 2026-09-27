# Dispatch and Fleet integration review

## Result

Local integration is complete on `codex/dispatch-fleet-integration`.
Dispatch Cards and filters (`242ab81a`) are combined with the Fleet SCSS
corrections and the reviewed browser-probe migration (`3fbd6a5d`). No root
merge, deployment, database change or production action was performed.
Unrelated root work and Driver Pay were preserved.

## Product corrections

- Details and load-number links use the existing accessible link color.
  The load link's dark contrast improved from 2.41 to 7.59; the regression
  failed before the correction and passed after it.
- The load block stays top-aligned when its first forecast badge arrives.
  Its loading dash uses the same lead size as the eventual link, removing
  the separate 2.8px line-height jump.
- Fleet text clocks use the available column instead of the shared dial
  panel's 20rem cap. Below the named 54rem truck-card breakpoint, the whole
  clocks row moves below vehicle readings before the column squeezes it.
  The normal wide card retains its paired layout.
- The route visit grid caps its 11rem minimum at available width, fixing
  64px horizontal overflow on a phone with 200-percent text.

Owners are the existing Fleet inspector styles and named screen tokens.
Shared clock business rules, semantic colors, calculations, provider reads
and Dispatch behavior were not duplicated or changed by these corrections.
Runtime consistency auditing is inapplicable to style and browser-probe
changes: there is no new persisted state or workflow. Browser assertions
cover the corrected layout invariants.

## Verification

The compiled Client artifact is the final Dispatch `scratch-riVj71` output,
staged by copy-on-write into `scratch-sibwn1`, with final integration CSS.
No production C# or JavaScript changed after that compiled artifact.
Final production CSS revision: `f6184f83`; source and staged main.css SHA-1
both equal `575b876238e238507e2cf6b7cfcb24c0e7433695`.

- Independent full suite on `930575bf`: Client 1244, Server 3773,
  JavaScript 663; exit 0. Original log is retained in
  `diagnostic-lvSyI0/full-check.log`. Later production changes were SCSS
  only and received affected checks. PostgreSQL execution was not enabled;
  test counts must not be described as PostgreSQL coverage.
- Styles 139 and formatting passed after the final style changes. Earlier
  styles/architecture boundary checks passed Client architecture 2,
  Server architecture 113 and JavaScript architecture 67 plus TypeScript.
- Independent mapToolbarSmoke `browser-ui-I4JXY9`: 16 cases, no errors or
  unexpected requests. The probe follows the search listbox, checks exact
  truck IDs and verifies the existing q-parameter restoration contract.
- Final independent fleetDesignSmoke `browser-fleet-design-ut3i6S`: all
  five cases passed, including 1024, dark, phone and phone/200-percent text.
  No layout failures, outside panels, browser errors or unexpected requests.
  Actual dark and 1024 compact screenshots were independently reviewed.
- Hours matrix `v1g25L`: 12 cases on unchanged final CSS; ten passed and two
  1200px cases exposed an obsolete toolbar-centering assertion. Only that
  assertion changed afterward. Targeted 1200 light/dark runs `tIZVA2` and
  `Qt1x6D` passed and supersede those two failures. Original failures remain
  retained; this is not represented as a single clean matrix run.
- Lifecycle `jjqGYN`: 16 Dispatch/Map transitions passed. Settled documents
  remained 4 and listeners 38. This measures synthetic Client behavior,
  not managed .NET memory, GPU retention or production performance.

The hours reports were independently inspected, including controlled
requests crossing ValidUntil, retained forecasts, read counts, map bounds,
loading geometry and separate shell polling counts. Duty growth remains
bounded by newly arriving content; phone height has an independent half-map
ceiling. No arbitrary layout tolerance was added to conceal product defects.
See [probe migration](hours-forecast-probe-migration-2026-09-26.md) for the
specific test-contract changes, retained runs and findings. Evidence runs
carry `.keep`; build outputs remain disposable.

## Limits

This completes local Cards/filter integration and its affected verification.
Table/Papers redesign, the broader load workspace and production verification
are not included. No release readiness claim substitutes for the repository's
mandatory release procedure and an explicit deployment request.
