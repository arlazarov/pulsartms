# Fleet SCSS review corrections

Base: `66f8801e`; isolated branch `codex/fleet-scss-review`.
No Dispatch files or root working changes were modified. No deployment.

## Owners and behavior

- Fleet truck telemetry owns its responsive equal-cell grid. It now wraps
  according to available reading width, including tablet widths. Named
  `map-telemetry-reading` sizing preserves tokens and operational colors.
- The station card owns ordinary quote internals. Inspector shell retains
  placement and width only. The earlier review described this as a shared
  application component; it is actually local to Fleet Map. Moving the
  internal rules does not change Dispatch or planned-station presentation.
- On phones the fuel editor scrolls as a whole within half the map stage.
  This keeps quantity and footer controls reachable at 200% text, when
  fixed head/totals/footer previously consumed the whole available height.
  Footer actions wrap using a named minimum width, rather than splitting
  their words into narrow columns. Desktop keeps its scrolling list.

## Verification

- `bash test.sh styles`: .NET architecture Client 2, Server 113 passed.
  An old selector pin failed after ownership moved; updated to assert the
  same station behavior under its owning selector. Final style tests:
  139 passed. Telemetry pins now express bounded, adaptive equal cells.
- `bash test.sh`: Client 1244, Server 3773, JavaScript 663 passed, zero
  .NET skips. Log: `artifacts/managed/diagnostic-y7fOuv/full-check.log`.
  No server source or contracts changed during this run; final style
  refinements were additionally checked with the 139 style tests.
- Style compilation and formatting checks passed. Client compiled in the
  test build; no Razor/C# changes.
- Rendered production Client from existing `scratch-cJxMMx` candidate with
  this branch's compiled CSS. Synthetic browser matrix `KOi2GA`: 1440 light,
  1024 light, 390 light, 1440 dark, 390 at 200% text all passed. Final footer
  refinement additionally checked in `Lih67k` (390 at 200%) and `e8tpXt`
  (390 light). These are managed `browser-fleet-design-*` runs.
- Reviewed screenshots of tablet telemetry, normal phone, ordinary station
  and large-text editor controls. The first strengthened run failed on the
  pre-correction tablet overlap, then passed after adapting the grid.

The browser probe now selects the editor itself, rejects missing/zero-size
panels, detects overlapping telemetry, verifies quantity and Save can be
scrolled into the card, and fails on caught steps, page errors or unexpected
requests. Expected ellipsis/screen-reader clipping remains diagnostic, not
an indiscriminate failure. This is synthetic presentation coverage, not a
live provider/camera or production acceptance test.

## Integration

Cherry-pick this branch's correction commit into the active integration
branch when its owner is ready. Preserve any concurrent changes to the size
token map, UI guide and generated style stamp; rebuild the stamp after
integration. Do not replace whole files from this checkout. No schema,
financial formula, API contract, cache or business-state change; runtime
consistency auditor checks are inapplicable to these presentation fixes.
