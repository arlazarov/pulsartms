# Current load rollover and compact route panel — September 9, 2026

## Diagnosis and ownership

A bounded read-only application-data inspection confirmed that truck 54777's
AMF1375 was still `in_transit`, with an actual pickup and no final delivery or
departure. Its scheduled delivery was September 9. AMF1373 was only assigned for
September 10. Immediately after midnight UTC, the shared dispatch board excluded
the overdue active load. Map previews and ETA-chain inputs consequently selected
AMF1373. AMF1375's last forecast was calculated at 23:59:39 UTC and expired at
00:01:39 UTC. No actual stop dates or dispatch statuses were manually changed.

The Application dispatch-board handler now keeps started, unfinished loads
regardless of appointment date and orders them before unstarted assignments.
Recorded pickup also identifies a started load if its status has not caught up.
Final actual delivery/departure and terminal statuses still exclude completed
loads. Date filtering remains for unstarted assignments. The same existing owner
serves map previews, planning reads, ETA chains and automatic planning.

This corrects the production calendar behavior missed during the
[previous release](map-loading-and-fuel-radius-2026-09-09.md); that release's
fixture-date correction was not coverage of this invariant.

## Compact route summary

The route panel's ordinary desktop minimum is reduced from 144 to 112 CSS pixels.
Its ETA and address reservations shrink with it, and the outer selected-truck
reservation follows the shared tokens. Stacked addresses retain a separate
minimum for wrapping. No fixed maximum height, clipping or smaller typography is
introduced. Street/locality emphasis and the ETA content are unchanged.

## Verification

- Four new regression cases failed before the server fix, then passed. The two
  affected integration classes passed all 28 cases using SQLite fixtures. Tests
  cover both status/pickup detection, calendar rollover, map/fleet previews,
  planning reads, ETA-chain ownership and actual completion advancing the queue.
- The final local release gate passed 1,180 Server tests, 486 Client C# tests and
  251 Node tests, with no failures or skips. Strict solution build and 231 staged
  asset integrity checks passed. Client Debug build also passed without warnings.
- Initial Cloud Build `da449432-fe9a-4cb3-a7db-7983c1f62fac` stopped before
  deployment on a one-second bUnit forecast-publication timeout. The affected
  test now explicitly waits for the planning request, releases its held response,
  and allows five seconds for the unchanged assertions. No assertion was removed
  and no architecture rule was relaxed. The full local gate passed again.
- An initial compact-panel browser run caught a stacked-address loading shift at
  390px. A dedicated stacked-address token corrects that without enlarging the
  desktop summary. The final artifact passed eight offline browser cases across
  2344/1440/1200/390px viewports in both themes, with zero browser errors or
  unexpected requests. Wide route summaries measured 112px both ready and empty;
  stacked 390px summaries measured 299.83px in both states. Screenshots were
  inspected. This checks panel geometry and fixture ETA presentation, not live
  map providers.

No isolated PostgreSQL fixture was available; PostgreSQL integration tests were
not run. Read-only application diagnostics are not database tests. No migration
was added. No new provider-route calculation, fuel-profile edit or manual fuel
recalculation was used. Browser fixtures do not establish production performance,
real-provider camera behavior or a complete visual audit.

## Deployment

Firebase Hosting published the verified `artifacts/release.Bxqx2P/publish/wwwroot`
artifact. Public index, map entry and stylesheet SHA-256 checks matched this
artifact on both configured domains.

Cloud Build `15ff09a8-10b6-45e2-9f5b-0e3bfd9349c0` passed all 1,917 tests and the
artifact gate. Cloud Run revision `amftms-api-00090-lqx` serves 100% of traffic
with Ready, ConfigurationsReady and RoutesReady all true. Its immutable image
digest is `sha256:cc9e1084ce7ceca87e4da09524b9c5fa44fb5dde5c6576eb3e2412de34e8ec4b`.
All three public/direct live-health endpoints returned HTTP 200 `Healthy`. The
bounded initial error-log query for this revision returned no entries; it is not
a long-running production soak.

After rollout, a normal production map reload for truck 54777 showed AMF1375,
order 567086821, the Fort Mill delivery address, 75 remaining miles and a visible
ETA of September 10 at 08:32 with its lateness/cycle values. The current-load link
also targeted AMF1375. The local API and Client were rebuilt/restarted and showed
the same current assignment and restored ETA. This verifies the corrected load
ownership and presence of a forecast, not independent accuracy of every ETA input.
