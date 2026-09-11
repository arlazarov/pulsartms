# Mobile cards and load references — September 8, 2026

## Scope

- Restored visible Order numbers with independent clipboard actions on current
  and future Dispatch Cards, including success/failure feedback.
- Bounded the two-stop grid using the shared `dispatch-stops` size token rather
  than spreading pickup and delivery over the entire wide truck card.
- Added current pickup/delivery Load and Order headers. The existing dispatch
  detail response supplies identity through a small separate Client-to-JS bridge;
  no new HTTP request, paid provider request or route-geometry publication is
  triggered by detail arrival. C# and JS reject another dispatch's identifiers.
- Mobile map cards follow their content height, bounded by the map and viewport
  with a touch-sized map strip reserved. Normal cards no longer scroll at 240px;
  genuinely oversized content remains scrollable in short landscape layouts.
- Compact Dispatch route summaries use separate aligned Total Distance,
  Remaining and Next Stop cells on phones. Enlarged text can stack naturally.
  Desktop metric presentation remains inline.

## Verification

- `bash test.sh all`: 563 Server, 216 Client and 155 JavaScript tests passed;
  architecture checks included. New regressions cover independent load/order
  copying, clipboard failure, dispatch changes, both detail/route arrival orders,
  late responses, unchanged content reuse and no extra requests/route bytes.
- Typed JavaScript, style compilation and JavaScript build passed.
- Strict Client Release publish passed with warnings treated as errors.
  Final local stage: `artifacts/mobile-cards-final.xfRiuZ/publish/wwwroot`.
  Artifact verification passed for 222 assets and six JS dependency graphs.
- Offline staged UI: all 40 viewport/theme/font/page cases passed. Inspected
  mobile light/dark Dispatch and desktop Dispatch screenshots.
- Offline current-stop/GPU check: four GPU, eight normal PU/DEL, two constrained
  viewport/content cases passed, including independent current-number copying.
- Offline future-stop check: five cases, ten stops, twenty screenshots passed;
  all four normal mobile PU/DEL variants had equal client and scroll heights.
  Updated its obsolete provider stub to implement and assert `setLoadReference`.
- Browser fixtures reported no unexpected requests or page errors. They use
  deterministic offline data, not live provider routing or production performance.

The local Client was restarted on port 5067. These changes are not deployed.
No server, financial formula, database schema or database content changed. No
migration is required. PostgreSQL execution checks were not run; no isolated
database fixture was provisioned or substituted with an application database.
