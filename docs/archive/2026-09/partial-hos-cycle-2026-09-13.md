# Known Cycle with partial history: September 13, 2026

## Change

Cycle feasibility now accepts a fresh, continuous observed suffix when older
days are missing. Current ELD Cycle anchors projected driving and on-duty work;
missing historical periods are not filled with assumed rest or duty. The normal
Cycle remaining row can show the signed balance without an additional label.

Historical recap still requires the full applicable cycle window and ELD
reconciliation. The daily/split-rest timeline is unchanged. Empty/stale recent
history, internal gaps, conflicting periods, unknown status, missing rules,
invalid Cycle values and unverified jurisdiction changes remain unavailable.
Canada Cycle 2 retains its additional rest-history constraint. Explicit restart
credit still requires a completely observed qualifying rest; baseline waiting
does not credit a restart.

## Checks

- `bash test.sh routing fuel fleet`: passed, including dependent architecture.
- `bash test.sh all`: 821 Client C#, 1,694 Server C# and 513 Node checks passed.
- Added 16 cases covering the 63h57m anchor, duty/rest debits, absence of invented
  recap, shortage tracking, invalid evidence, border/Canada guards and the same
  anchor across current and future load stop forecasts.
- `git diff --check`: passed.

No Client rendering code, live provider data or database rows were changed.
No browser/live-truck verification or PostgreSQL execution was performed in
this change. The running localhost API was not restarted because the working
copy also includes personal-settings migrations awaiting user approval.
No deployment or migration was performed.
