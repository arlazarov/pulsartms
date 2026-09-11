# Quiet ETA refresh — 2026-09-08

User-requested presentation change: keep the previous ETA, Cycle, lateness and
conditional arrival text and colors unchanged during recalculation. Removed
Previous/Previously late/Updating labels from shared Blazor stop cards, Dispatch
summaries and current JavaScript map popups, plus their unused style selectors.
This supersedes the visible pending-marker policy in earlier dated reports.

The existing internal pending flag, whole-snapshot memory, identity/completion
checks and fifteen-minute grace remain unchanged. Server cache validity is not
extended. Expired recap timestamps remain unavailable instead of being resurrected;
complete new forecasts replace the old display. Unchanged pending JavaScript
metadata reuses the same popup content without another DOM construction.

Verification:

- `bash test.sh dispatch fleet styles`: 426 Server, 327 Client C# and 162 Node
  checks passed, zero failures/skips. This is affected-category coverage, not a
  fresh full-suite run.
- Strict Client Release publish, style/JavaScript compilation, typed JavaScript
  checks and integrity verification of 222 assets/six entry graphs passed.
- Offline browser checks: four staged Blazor desktop/mobile theme scenarios and
  34 GPU/popup cases passed with zero errors, unexpected requests or bounds failures.
  Pending snapshots compare exact forecast text/classes/computed colors after
  independently confirming pending data reached the component. Representative
  desktop/mobile pending screenshots were inspected.

Local reports: `Client/test-results/quiet-eta-hours/report.json` and
`Client/test-results/quiet-eta-stop-cards/report.json`. Verified staged artifact:
`artifacts/quiet-eta.HMLWA4/publish/wwwroot`.

Only Client presentation and related tests/documentation changed. No server
calculation, request cadence, paid-provider policy, DTO, DI or database change;
no migration is required. Production deployment, PostgreSQL execution tests and
production performance measurements were not performed.
