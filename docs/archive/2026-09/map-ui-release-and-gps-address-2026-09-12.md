# Map UI release and GPS address — 2026-09-12

Published the accumulated Client changes: bounded group labels with geographic
anchors, compact inspectors without disclosure animation, summarized Dispatch
table stops and planned-station price colors when ordinary stations are hidden.
The first artifact was `artifacts/managed/release-RqO5aO/publish/wwwroot`.

During publication, the user requested the truck's current address in its card.
The follow-up uses the existing `TruckLocationMapDto.FormattedLocation` and
`UpdatedAt` from Samsara telemetry. Both inspector densities show that address
separately from the next stop, with the GPS observation in the viewer's local time.
Historical selections say Recorded location. Missing addresses are explicitly
unavailable and changing trucks does not retain another truck's address. No new
HTTP request, reverse geocoding, server contract or migration was introduced.

## Verification and publication

- `bash test.sh fleet styles`: 290 Server and 209 Client tests passed, with map,
  style and architecture Node checks.
- Final `bash verify-release.sh`: strict build, 1,589 Server tests, 724 Client tests,
  452 Node tests and integrity validation of 264 assets passed.
- Initial release offline UI smoke: 44 page cases passed with no reported errors.
- Final Fleet-only hours/inspector browser smoke: 10 cases passed with no geometry
  failures, browser errors or unexpected requests. Desktop expanded and mobile
  compact screenshots were inspected with the GPS address visible.
- GPU cluster regression: two density cases passed. Focused fuel visibility smoke:
  four light/dark and density cases passed.
- The broader stop-card probe still has the previously reported, unrelated 5px
  route-width expectation failure. It was not changed or represented as passing.
- Authenticated live provider interaction and PostgreSQL tests were not run.

Final artifact: `artifacts/managed/release-7EOVil/publish/wwwroot`.
GPS browser evidence: `artifacts/managed/browser-hours-forecast-yAMbdO/report.json`.
Firebase Hosting project `amftms` accepted the final artifact. The production
`https://tms.amfcarrier.com` index, CSS, map controller, GPU renderer and fingerprinted
Client WASM returned HTTP 200 and matched the staged SHA-256 values. API, cloud
configuration and database were not deployed or modified.
