# Mobile location/load disclosure height

The map overlay geometry owner captures the current truck inspector height
before the mobile Location & load details click reaches Blazor. Pointer and
keyboard activation use the same capture listener. Only the mobile truck's
expanded secondary section consumes that height; desktop styles are unchanged.

The expanded card scrolls inside its retained height, capped by map space. The
header remains part of the scroll flow on short or enlarged-text screens.
Closing or selecting another truck restores content-driven summary sizing.
Disposal removes the capture listener and its geometry property.

Verification:

- `bash test.sh styles map`: affected .NET, map, styles and architecture checks
  passed (253 Client and 126 Server tests).
- JavaScript type check, style compilation and Client build passed.
- Fleet browser fixture: 12 width/theme cases passed; no browser errors or
  unexpected requests. The mobile scrolling regression checks unchanged height
  on portrait, short and 200% root-text layouts.
- Evidence: `artifacts/managed/browser-hours-forecast-NofDIt` (pinned).

Localhost only; no deployment or database changes. Full automated suite,
cross-page browser suite and live provider validation were not run this turn.
