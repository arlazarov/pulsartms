# Dispatch interaction and Fleet Map design follow-up — 2026-09-10

## Scope

This follows the design-fidelity review and the user's additional request to
replace inline disclosures with predictable interactions, refine Table, and bring
the actual map markers closer to the approved Fleet Map reference.

- Cards, Table and Papers open the same native `DispatchLoadDialog` from their
  existing board data. All stops, completed pickups, references, forecasts and
  server-provided financial values remain available without another request.
- Compact cards keep addresses and ETA; miles remain more prominent than rates.
  HOS, current duty, Next recap and route mileage no longer require accordions.
- Order copying changes its own icon, not the board's height. Dialog closure uses
  Escape, Close or a deliberate backdrop click and restores focus and scrolling.
- Table preserves intermediate stops, historical equipment and all financial
  fields. Native load buttons and map links have separate keyboard/click behavior.
- Fleet Map uses a filled truck illustration, separately aligned telemetry,
  larger HOS readings, a compact unselected prompt and a larger planned fuel card.
  The fuel editor's gauge dimensions remain independent.
- Actual GPU markers use 46px blue cab/trailer glyphs, 34px round stop numbers,
  a violet first future route and spaced circles containing real station prices.
  Existing station points and price colors remain selectable and unchanged.
  Planned visits retain distinct `Fuel 1` badges. Text contrast is checked across
  the interpolated price palette; unknown engine status is neutral, not green.

## Review findings

The first staged run exposed a missing `loadDialog.js` build entry. The module is
now explicitly registered; an architecture test checks all generated JS imports
from Razor/Client C# against the entry-point graph.

Native modal focus alone did not lock background scrolling. The dialog now reuses
the existing reference-counted popup lock. A separate Chrome probe checked wheel
scrolling, Escape and replacement-opener focus after a refresh at desktop/mobile
sizes. Programmatic heading focus does not draw a control-sized outline; Close
retains its keyboard focus ring.

Visual inspection caught a literal `Phase` string in the Cards popup; the binding
now passes the actual phase, with component and staged-browser assertions.
The held-response forecast scenario also exposed missing refresh-state propagation
to the popup. It is covered independently from compact-card retention.

The marker audit found unnecessary hidden-station label preparation on zoom.
Price candidates now use a last-entry visibility cache; with fuel hidden and no
active edit, an empty selection is reused across zoom changes. This removes that
source-level work, not a measured claim about production memory or frame timing.
The old GPU/popup fixture also exposed 12px of clipped bottom padding in an
ordinary mobile two-visit card. A scoped padding adjustment preserves 64px gauges.
Longer cards with purchase-cost/actions can still scroll inside the bounded popup.

## Verification status

Final artifact: `artifacts/interaction-completion.mIf8Fv/publish/wwwroot`.

- `bash test.sh all`: 1,277 Server, 584 Client and 337 Node checks passed (2,198 total).
- Strict Client Release build and publish passed; artifact verification checked
  246 published assets and seven JavaScript dependency graphs. Targeted JS type
  checking passed before the final hidden-marker fast path.
- Final staged UI: all 44 page checks across 12 viewport/theme/text-size profiles,
  including popup phase, references, cycle details, focus and scroll restoration;
  no browser errors, failed checks or unexpected requests.
- Final staged full hours/forecast matrix: 10/10; station popup: 4/4; both have no
  browser errors or unexpected requests.
- Frozen-source GPU/current-stop fixture: 40 scenarios passed, plus physical
  round-marker checks at both pixel densities. The focused new marker fixture
  passed mouse/touch edge picking at DPR 1/2. These use production rendering on
  synthetic canvases, not the Google provider or live business records.
- Fuel editor: 4/4 passed on the preceding verified artifact, before the final
  hidden-marker optimization and popup-only mobile padding change. This is not
  claimed as a final-artifact fuel-editor run.
- Authenticated localhost was rebuilt and inspected: Cards, Table, Papers popup,
  selected truck 54777, enabled fuel/next-load layers and the live vector map.
  The final map inspection showed no UI alerts or panel horizontal overflow.

Ignored reports live under `Client/test-results/interaction-completion-*`,
`stop-cards-interaction-verified`, `map-markers-interaction-verified` and
`fuel-editor-interaction-verified`. Earlier staged results are not substituted for
final-artifact checks.

No server behavior, persistence schema or financial formula is intentionally
changed in this follow-up. PostgreSQL execution, iOS Safari, a new authenticated
memory soak and production deployment are not part of this verification.
