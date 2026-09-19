# Expanded Fleet inspector density — 2026-09-12

The expanded inspector is capped at 76rem instead of 84rem. Truck and route
sections use content-driven heights and small vertical padding. Removed reserved
address/ETA row heights, wide-screen enlargement, duplicate truck identity artwork
and the obsolete reveal/fade keyframes. The header retains the truck number;
driver, trailer, telemetry, HOS, route data and actions remain accessible.
Disclosure does not animate, independently of the reduced-motion preference.

Verification against `artifacts/managed/release-wWyK48/publish/wwwroot`:

- Full gate: 1,589 Server tests, 723 Client tests, 451 Node tests passed; strict
  build reported zero warnings/errors and verified 264 published assets.
- Fleet inspector: ten desktop/mobile theme cases passed with reduced motion
  disabled, no animation and a desktop expanded-height bound of 260 CSS pixels
  (`browser-hours-forecast-5QecB6`).
- Fuel editor: eight browser cases passed (`browser-fuel-editor-20azlA`).
- `git diff --check` passed.

No deployment or migration was performed. Browser data/provider responses were
synthetic. Live integration and PostgreSQL execution checks were not run; no safe
isolated PostgreSQL fixture was available. The separate AMF1376 starting-stop
assignment question remains unresolved and was not changed by this styling work.
