# Truck 54777 stop identity recovery

## Cause and change

AMF1376 (`360b616c-aae1-4a9f-acf2-58f82f72eb34`) had a saved route whose
former pickup ID had been reused for the delivery when upstream stops were
renumbered. Sequence-only import matching preserved the manual starting-stop
reference on that reused ID. The effective itinerary therefore contained only
the delivery, and the saved route failed its current input signature.

Application import now matches operation/source location and disambiguates repeat
visits by appointment. It retains unchanged indistinguishable slots but does not
transfer manual facts to ambiguous replacements. A missing manual starting stop
continues to require confirmation. Automatic planning uses the authoritative
board to allow GPS-to-one-stop routes for current assignments, not future loads.

## Approved operational recovery

The user approved the fix and confirmed the Webster pickup is still ahead.
The application database was used only for the targeted diagnosis/recovery,
never as a test fixture. A serializable, incident-specific repair checked the
load/truck/stop IDs, assignment revision, uncompleted state and saved route
coordinates before updating only the starting reference and its concurrency
revision. The original assignment actor/time remain unchanged because this
restores the original visit identity rather than recording a new assignment.

- Previous anchor: `da63c6f0-c232-41eb-9d23-b66f3868ad12` (now delivery).
- Restored anchor: `5dc8564b-f6b1-4dac-8f75-7ac2bc83cefa` (current pickup).
- Assignment revision: 1 → 2.
- No stop status, completion, operation, imported address or truck was changed.
- The normal scoped `PrepareDispatchPlanningCommand` then saved route version 9,
  with both current stop IDs and the pickup as next stop. Its input signature
  matches. No fuel search was requested.

The guarded recovery modes in `tools/RouteMemoryProbe` are incident-specific:
`--read-only --repair-1376-start` for preflight, `--repair-1376-start --apply`
for the approved reference repair, and `--prepare-1376-route --apply` for the
normal route worker command without hosted workers or migrations. The repair
refuses replay after revision 1. Provider calls and route persistence occur only
in the explicit preparation mode. Use the managed scratch runner for outputs.

## Verification and release boundary

- `bash test.sh routing dispatch synchronization`: 895 server, 456 Client C#,
  6 dispatch JavaScript and 46 JavaScript architecture checks passed.
- Full `bash test.sh`: 1,656 server, 765 Client C#, 474 JavaScript checks passed.
- Strict API build: zero warnings/errors.
- Read-only database verification confirms the restored two-stop itinerary and
  matching saved-route inputs.
- Local API restarted with database migrations disabled; localhost Client and
  API liveness respond with HTTP 200.
- No isolated PostgreSQL fixture was available; PostgreSQL test execution and
  migrations were not run. Operational recovery is not a PostgreSQL test pass.
- Authenticated map rendering was not visually verified: the computer-use runtime
  was unavailable and the separate browser session was at Login.
- No code deployment or website publication was performed.
