# Quiet ETA across in-flight reads — 2026-09-08

Deployment follow-up: the [September 9 release](fuel-eta-release-2026-09-09.md)
records the final gate, deployed revisions and live verification.

Follow-up to the earlier quiet-label change: old ETA could still disappear when
its validity timer elapsed before the refresh HTTP response, or when unchanged
stops received a new route geometry version. The earlier pending-response checks
did not cover those intervals.

## Changes

- Fleet and Dispatch activate display retention before awaiting their existing
  refresh requests. Matching complete ETA, Cycle, recap and alternative rows keep
  their text and colors until a complete replacement is available.
- Same-query transient failures keep mounted Dispatch cards. Null ETA replies and
  individual pending future loads retain their matching previous snapshot. Search,
  page/view ownership changes and denied access do not reuse a previous board.
- Fleet separates stop/schedule identity from geometry identity. A geometry-only
  update retains ETA and displayed distance, but never sends the old geometry's
  progress coordinates to the new route. Request-start interop publishes ETA
  metadata only, not geometry.
- Changed destinations/appointments reject a re-sent old calculation; changed
  truck/load/stop and completed stops clear retention. Explicit non-pending
  unavailable results remain authoritative. Older JS completion replies cannot
  replace a newer snapshot or move its deadline.
- Client HTTP responses carry a nonserialized status code so authentication and
  authorization failures can clear data without parsing message text.

Retention remains display-only, capped at the original `ValidUntil + 15 minutes`.
Repeated requests cannot renew that deadline. Razor evaluates it on existing
renders; the map retains its independent expiry timer. No provider calls, polling
loops, server ETA calculations or database schemas were added by this change.

## Verification

- Final `bash test.sh all`: 746 Server, 401 Client C# and 178 Node tests passed;
  zero failures or skips. Architecture checks are included and unchanged.
- Strict Client Release publish and JavaScript type checks passed. Artifact
  verification checked 222 assets and six JavaScript entry-point graphs.
- Staged Blazor UI smoke: 44 page checks passed across desktop/mobile, light/dark
  and normal/enlarged text. Report: `Client/test-results/ui-smoke/report.json`.
- Extended staged ETA/fuel smoke: four scenarios passed. Held HTTP responses cross
  the original ETA deadline; mutation probes reject missing/incomplete intermediate
  card states and verify replacement. Existing fuel quantities, distances and
  historical check details stay unchanged. Final report:
  `test-results/quiet-eta-hours-verified/report.json`.
- Offline GPU/popup probe: four GPU, eight popup, twenty hours and two constrained
  cases passed. Report: `test-results/quiet-eta-stop-cards/report.json`.
- Browser runs had zero errors and unexpected provider/network requests.
  Representative desktop/mobile pending cards and a current map popup were
  visually inspected.

The first staged run exposed a missing optional pending-dispatch collection in a
forecast; it is now null-safe with a component regression. A later fixture wait
was corrected to distinguish request-start retention from an accepted pending
response. The final staged run passed after both corrections.

Verified Client artifact: `artifacts/quiet-eta/publish/wwwroot`. The running local
API/Client were not restarted and production was not deployed. Live authenticated
provider integration, PostgreSQL execution and production performance were not
measured. The earlier fuel migration `20260909020907_StoreTruckFuelPlans` remains
unapplied; this ETA presentation change adds no migration.
