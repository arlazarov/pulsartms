# Dispatch summary binding and card wrapping

Local follow-up to `3557cf1c`; not deployed.

## Owners and behavior

DispatchList consumes the existing batched planning summary. A truck's next
planning load can differ from the first uncompleted board row. Accept only a
visible uncompleted load matching the truck, execution leg and assignment
revision; use that identity for the header and card summary. Identify the
load beside Left when it differs from board order. Preserve the existing
request cancellation, board-version and stale-callback guards. No new
calculation, cache, provider call or per-card read was introduced.

The 11005 read-only investigation reproduced a summary for 1410 while 1403
remained first on the board. Tracking had passed 1403's stops without its
final completion confirmation. This patch does not complete 1403 or repair
production state. The live process cache was not directly observed.

TruckReadings remains the shared telemetry owner. Fleet overrides its minimum
cell width through a scoped custom property; Dispatch retains its default.
Station comparison owns a three-column day grid, with complete wrapping rows
when its own container is narrow. Prices, formulas and semantic colors are
unchanged.

## Checks

- `bash test.sh dispatch styles`: Client 713 and Server 1650 passed. One
  CSS-shape assertion still expected the old fixed cell width and failed.
  Updated that assertion for the new overridable width, then ran the remaining
  JavaScript gates without repeating unchanged .NET tests: styles 139,
  Dispatch 16, architecture 67, all passed.
- Six controlled-response summary regressions cover the matching next load,
  wrong truck, unknown load, wrong leg, old assignment and completed load.
  They assert one batched read, the correct fuel/distance and no completion
  mutation. Existing stale callback and board identity checks remain.
- Client Release publish `scratch-5zTMZn` passed; styles compilation, pinned
  CSharpier and changed-file Prettier checks passed.
- Offline rendered Fleet matrix `browser-fleet-design-MHu1yt`: 1440, 1200,
  1024, 390, dark and 200% text; no failed steps, overflow, runtime errors or
  unexpected requests. All four telemetry readings share one row at desktop
  widths. Price comparison fixtures now include the DTO comparison fields;
  all three days render without clipping and align at desktop widths.
- Inspected rendered 1200 and 390 station screenshots and the 1200 truck
  screenshot. The 200% whole-page station screenshot is scrolled past prices;
  its price layout is covered by DOM bounds, not a full visual claim.
- PostgreSQL execution and production performance were not tested.

## Consistency coverage and limits

No persisted workflow or server calculation changes. Runtime auditor changes
are inapplicable to these display bindings; controlled-response component
regressions cover identity and one-read behavior. Existing missing completion
confirmation remains a separate operational state, not automatically repaired.
The UI screenshots use synthetic data and do not prove live incident repair.
Papers financial/trailer changes are separately owned and pending review.
