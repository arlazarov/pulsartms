# Application design fidelity and equal-height cards — September 10, 2026

## Scope

This follows the [composition pass](application-design-composition-2026-09-10.md)
and the user's review of the approved Dispatch Cards, Table and Fleet Map images.
It replaces mileage disclosures with always-visible primary mileage fields and
smaller secondary server-provided rate/RPM values. The shared FuelReading pill
uses a pump icon, dark text and soft warning/success surfaces beside motion status.
No fuel-price marker colors or server financial formulas changed.

The sidebar uses the authenticated account's existing name/role claims. Table and
Papers share current/next/completed phase presentation and complete appointment
windows. Completed pickups, historical assignment details, pagination and
Active/Completed switching remain available. Unsupported load-creation controls
and illustrative profile/notification data were not added.

Dispatch retains visible HOS clocks and Next recap, with duty/rest detail in a
native disclosure. Current remaining mileage and per-load fuel-stop counts read
the existing planning cache, with ownership/freshness guards and no new HTTP
requests or geometry copies. Fleet Map uses shared icons, the FuelReading pill,
an explicit HOS heading and an honest relative-price legend; existing popup
prices, gauges, server purchase costs and edit controls remain intact.

The final user review found unequal card heights. Horizontal lanes now stretch
cards to the tallest natural content height and align their footers. There is no
fixed/minimum card height; stacked mobile cards size independently. Opening
Details naturally increases the horizontal row height.

## Verification

- `bash test.sh all`: 2,163 passed — 1,277 Server, 566 Client and 320 Node tests,
  including architecture checks. PostgreSQL execution was not run.
- Strict Release Client publish and local build passed with zero warnings/errors.
- Final artifact: `artifacts/design-fidelity-delivery.InYrsD/publish/wwwroot`.
  Artifact verification checked 234 assets and seven JavaScript dependency graphs.
- Offline UI: 44 page cases over 12 viewport/theme/text-size combinations passed,
  including Cards/Table/Papers interactions and 100%/200% text size. Equal card
  height and footer alignment are checked before and after keyboard disclosure
  expansion with different stop counts. Selected Table color is checked after
  its real CSS transition finishes, not during an interpolated frame.
- Final staged HOS/ETA matrix: 8/8; station popup: 4/4; fuel editor: 4/4. No
  browser errors or unexpected requests in these or the UI matrix. Selected-map
  loading-height shifts and checked horizontal overflow were zero in the fixtures.
- Desktop/mobile screenshots were inspected separately from assertions. Actual
  authenticated localhost Cards, Table and Papers were also inspected. The final
  Cards recheck measured all three first-row heights at 526.63px and all footers
  at the same bottom coordinate; opening one Details increased every card to
  572.38px with aligned footers. Measurements depend on the current live content.

Final evidence is local and ignored: `artifacts/design-fidelity-delivery-*.log`,
`Client/test-results/ui-design-delivery/report.json`,
`Client/test-results/hours-design-delivery/report.json`,
`Client/test-results/station-design-delivery/report.json`, and
`Client/test-results/fuel-editor-design-delivery/report.json`.

An intermediate complete run exposed a style assertion that incorrectly treated
a hidden descendant disclosure summary as a hidden driver container. The test now
targets the actual HOS/recap/driver containers and explicitly tests disclosure
defaults. Another intermediate run exposed concurrent JS-interop enumeration in
an existing fuel-editor component test; enumeration now occurs on the renderer
context with the same focus/import-count assertions. Neither failure was skipped
or accommodated by an architecture exception.

## Limits

Localhost Client was rebuilt/restarted; the existing API remained running. No
production deployment, database migration, provider-route/fuel recalculation or
business-record write was performed. Offline browser fixtures do not prove live
fuel economics, provider/GPU integration, PostgreSQL behavior or production
performance. The visual comparison is not a claim of universal pixel equivalence
for every data combination.
