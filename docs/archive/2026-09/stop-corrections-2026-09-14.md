# Stop correction implementation — 2026-09-14

The full load workspace now has an explicit correction action for ordinary stops,
including completed loads. Completion overrides retain imported actuals; resource
corrections retain completion and name their whole-leg or source-load scope.
The write uses actor authorization, a serializable transaction, opened workspace
and source versions, and an actor-bound idempotent receipt with before/after audit
snapshots. Recorded transfer custody and shared legs require coordinated changes;
this action does not undo a completed Drop/Hook. Payroll is unchanged.

## Verification

- `bash test.sh all`: 981 Client C#, 1,896 Server and 544 JavaScript tests passed
  (3,421 total). The preceding run hit the existing five-second
  `DispatchBatchRefreshTests` wait; no test or timeout was weakened. The complete
  rerun passed. New tests cover provider completion overrides, unknown completion
  time, completed-load assignment correction, native leg scope, stale writes,
  authorization, invalid resource IDs, retained drafts and idempotent retries.
- Strict API build and strict Client Release publish passed. The SDK reported its
  existing optional wasm-tools notice; no AOT or performance claim is made.
- EF model check: no pending model differences after `AddStopCorrections`.
- The actual published Client was inspected at 1440px and 390px in a local-only
  synthetic fixture: the completed-stop editor opens, shows its resource scope,
  loads selectable resources and has no horizontal page overflow or page errors.
  This read-only preview does not exercise authenticated production saves.

`20260914214701_AddStopCorrections` adds nullable `DispatchStops.CompletionOverride`
and `DispatchWorkspaceRevisions.BeforeJson`. It was generated, not applied. The
prior broker migration also remains pending. The production database and server
were not modified. No isolated PostgreSQL fixture was available; database checks
used the existing disposable in-memory SQLite fixture, not the application DB.

Known scope limits: resource corrections cover a native assignment leg, or every
stop of a source-only load; no one-stop resource override or arbitrary selected
range was introduced. Historical measured mileage retains original evidence and
may need review. Native transfer events cannot be corrected through this ordinary
stop form. Separate migrations/rollout are required before live use.
