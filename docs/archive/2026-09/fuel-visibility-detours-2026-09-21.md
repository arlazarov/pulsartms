# Fuel visibility and detour recovery — 2026-09-21

The user requested fuel stations to remain visible regardless of the Next loads
layer and automatic route/fuel recovery after a detour.

## Findings

The map filtered fuel visits by current-load ownership when Next loads was off.
It also removed all recommendations when a saved plan required non-price
revalidation. The editor showed the complete horizon, creating inconsistent
visibility between the map and editor.

The background fleet cycle already advanced routes and separately refreshed fuel,
but fuel refresh did not inspect the saved plan projected against current fuel.
The on-demand route worker did not perform the subsequent fuel refresh at all.

Production 11007 had fresh GPS about 15.3 miles from its saved road, an unchanged
route from 18:32 UTC, and no persisted deviation start. The serving Cloud Run
revision reported exceeding its 512 MiB memory limit at 19:08:37 UTC. This proves
a process termination, but does not establish that it caused every delayed job.

## Changes

- Fuel markers no longer depend on Next loads. Invalid same-assignment plans
  retain station locations with a saved-plan warning, without stale distance,
  ETA, gallons, costs or an edit action. Changed assignment inputs clear them.
- Off-route GPS also puts markers into this non-actionable state, even when a
  price-only flag remains set. Old route progress cannot remove the station
  during arrival by another road.
- Fleet and on-demand route preparation use the shared truck-fuel projection
  before refresh. An invalid automatic plan on a fresh matching road triggers
  the existing revision-guarded recalculation command. Manual purchases and
  manual starting fuel remain protected. No new retry loop or persistence
  owner was introduced.
- The deployment wrapper now requests 1 GiB of memory. No production resource
  setting has been changed by this local edit.

11007 was separately recovered through the existing route-preview, choice and
fuel-recalculation API endpoints. Read-back showed `OffRoute = false` and
`NeedsRefresh = false`. The replacement retained a below-reserve warning:
about 1.9 US gallons at the selected station. This is not a verified fuel margin.
Evidence: `artifacts/managed/diagnostic-vnP22c`.

## Verification

The affected .NET run passed 2,537 server and 976 Client C# tests, including
architecture. After updating the interaction tests for the requested visibility
behavior, all 625 JavaScript tests passed. Type checking and JavaScript bundle
generation passed. Evidence: `diagnostic-008pku` and `diagnostic-a7UqDm` under
`artifacts/managed`. The former initially recorded three obsolete JavaScript
visibility expectations; the latter is the successful complete JavaScript run.

Browser rendering, sustained production memory and autonomous post-deployment
recovery were not verified. No schema migration is needed.

The final `verify-release.sh` gate passed 3,105 server, 1,052 Client C# and
625 JavaScript tests without failures or skips. Strict builds passed, and the
gate verified 297 Client assets and 11 JavaScript dependency graphs. Evidence:
`artifacts/managed/diagnostic-yl4AOG/release.log`; staged Client:
`artifacts/managed/release-DBqtUX/publish/wwwroot`. Publication is pending.
