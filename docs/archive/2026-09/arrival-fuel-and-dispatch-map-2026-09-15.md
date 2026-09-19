# Arrival-date fuel pricing and Dispatch map verification

## Local changes

- Automatic fuel search replays captured HOS and appointment waits to estimate
  each station occurrence's local arrival date. Screening uses road-only timing;
  finalist chains include estimated station access before quantity optimization.
  Published quotes for that date take precedence. Missing quotes or ETA use an
  explicitly estimated current-price fallback. The map purchase section carries
  the arrival date, quoted price and fallback label separately from today's
  ordinary station price display.
- The existing lease-owned per-truck planning scan checks a durable USD quote
  calendar fingerprint, including future dates used by the saved search. Changed
  quotes refresh existing automatic plans; identical quotes do not. Failed work
  retains the saved fingerprint for retry. Manual edits and manual starting fuel
  are excluded and the saved revision is rechecked inside the calculation gate.
  Read validation also rejects invalidated future-price recommendations.
- Dispatch's map reads saved, input-matched base roads for completed and active
  native sections. Separate road sections are not joined across transfer gaps.
  Unsaved route edits hide saved roads. Reads do not call a routing provider.
- Shared loading indicators reuse the existing reduced-motion-aware dot pulse.
  Visible Loading sentences were removed from pending page reads. Map loading
  occupies a reserved header slot rather than a transient information row.
- The redundant Settings tab was removed from Fleet's resource tabs; Settings
  remains in main navigation.

## Verification

`PULSARTMS_RELEASE_UI=1 bash verify-release.sh` completed successfully:

- Node: 550 passed, no skipped tests.
- Client C#: 1,007 passed, no skipped tests.
- Server C#: 1,934 passed, no skipped tests.
- Strict solution build, style/JavaScript checks and published asset integrity
  passed; 273 assets and seven entry-point graphs were verified.
- Offline staged UI smoke: 52 pages across 12 viewport/theme/text-scale cases,
  no reported failures or unexpected requests.

The cold map component regression waits for explicit road interop publication,
not merely the preceding render notification. An existing Dispatch batch-refresh
test failed in an earlier full run, passed in isolation and passed in the final
unfiltered gate; no assertion or timeout was relaxed.

Verified artifact:
`artifacts/managed/release-30MBDL/publish/wwwroot`.
Offline UI evidence: `artifacts/managed/browser-ui-JITlg9/report.json`.

## Limits and release status

These changes have not been deployed. No database migration is required: the fuel
metadata is additive saved-plan JSON. Real PostgreSQL fixture checks were not run;
database regressions used isolated SQLite in-memory fixtures. Authenticated live
browser/provider checks and production performance measurements were not run.
The offline UI substitutes the map provider and does not verify road rendering
against Google in production. Generic fuel-price fixtures may render their
unavailable state; they are not an acceptance check of live station pricing.

ETA remains an estimate, and unavailable driver/assignment timing falls back to
estimated current prices. Candidate search is bounded, not a proof of global
optimality. Automatic refresh follows the existing rotating planning scan and
retry constraints; it is not an immediate all-truck recalculation guarantee.
