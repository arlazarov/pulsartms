# Swapped truck assignment repair

## Incident and authorized data repair

The operator confirmed swapping AMF1376 to truck 11007 and AMF1383 to 54777.
The imported header and both stops reflected those assignments, but AMF1376
retained a starting-stop confirmation for the former truck, 54777. The effective
itinerary was empty, causing planning validation failures rather than a provider
timeout. The Client incorrectly described that validation error as temporary.

The scoped `SwappedAssignmentRepair` maintenance entry point used a serializable
transaction, exact load/truck guards and a matching read-only preflight SHA-256
fingerprint. It released only AMF1376's old confirmation and starting anchor,
incrementing assignment revision from 2 to 3. AMF1383 was unchanged. Stop
operations and manual completion revisions were preserved; no route geometry,
GPS facts, prices or fuel purchases were written by the repair.

Dispatch IDs:

- AMF1376: `360b616c-aae1-4a9f-acf2-58f82f72eb34`
- AMF1383: `fdb9bafe-72e5-4013-8a52-2c29f2e5ccff`

Preflight fingerprint:
`F6CB49AE223B4F28C19EB3FDECE95E311296ECB1BCAADBE8ACD41D435168A8AF`

A subsequent read-only route-input inspection resolved AMF1376 to 11007 and
found saved route version 11 owned by that same truck, with matching current
inputs and `InputsChanged=false`. This establishes ownership recovery, not a
complete end-to-end ETA/fuel or visual verification.

## Local changes

- Synchronization releases a stale confirmation only when the imported header
  and every stop resolve to one different active truck. Mixed/missing assignments,
  manual operations, removed anchors and completed loads retain review behavior.
- Existing dirty-route handling includes both former and replacement trucks.
- Client planning validation displays the server message and backs off automatic
  retries for one minute; other selections and explicit refresh remain available.
- Fleet's route/load link is an accessible external-link action before Details.
- Shared distance text keeps numbers and their units together while allowing
  wrapping between the miles and kilometers values.

## Execution evidence and limits

Client and API builds completed with zero warnings and errors. The local Client
was restarted on port 5067 after rebuilding. Regression test sources were added
for assignment reconciliation and distance formatting, but no test suite or
browser checks were run, as requested by the operator. No migration was added or
applied. No performance improvement was measured. These code changes have not
been published to production; only the scoped incident data repair was applied.

## Subsequent authorized deployment without checks

The operator subsequently requested publishing Client and API without checks.
The Client was published from the fresh retained directory
`artifacts/managed/release-TvRxfc/publish/wwwroot`; Firebase reported release
completion. API build `3cbcb479-c8d5-4bd4-8df8-bab777da4971` succeeded using the
existing Dockerfile and a temporary build-only Cloud Build configuration. The
maintained release gate and deployment scripts were not modified.

Cloud Run deployed `amftms-api-00111-7fd` and reported 100 percent traffic on the
new revision. The deployed image digest is
`sha256:2da542a4dcc9750eec6ba34c1b30572148060729abc9d6343d2ad160e045a0a6`.
No automated tests, browser checks, post-deployment smoke requests or asset
verification were run. Deployment completion is not an application test pass.
