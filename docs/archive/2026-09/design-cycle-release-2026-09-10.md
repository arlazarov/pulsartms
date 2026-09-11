# Design and ELD Cycle release — September 10, 2026

## Release state

The pending Client publication recorded below was completed at 19:31 UTC in the
[integration settings release](integration-settings-2026-09-10.md#production-release--completed-at-1931-utc),
which includes this design/Cycle work and the later Settings changes. The details
below preserve the earlier 16:00 UTC handoff state.

The server was deployed successfully at 16:00 UTC. Client publication remains
pending because Firebase CLI requires `firebase login --reauth`. No Firebase
deployment was attempted after the failed read-only authentication check; the
previous Hosting artifact remains published. This is not a completed UI release.

Scope includes the existing compact application design, unified map inspector,
manual fuel-plan work and the [authoritative ELD Cycle anchor](eld-cycle-anchor-2026-09-10.md).
Current ELD time is the calculation anchor; the observed 50:02:38 is not hardcoded.

## Server deployment

- Official command: `bash deploy-server.sh`; completed with exit code 0.
- Project `amftms`, service `amftms-api`, region `us-east4`.
- Successful Cloud Build: `90cc03d4-e14a-4547-b970-3fcb902538f6`.
- Image: `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:b3891204c4a8a0d262a3f96e3bb62983617fd82388a7bff8b82f9d4af187d327`.
- Ready revision: `amftms-api-00091-rgh`, receiving 100% of traffic.
- Ready, ConfigurationsReady and RoutesReady were all true; Ready transitioned
  at `2026-09-10T16:00:24.074225Z`.
- Previous ready revision: `amftms-api-00090-lqx`, image digest
  `sha256:cc9e1084ce7ceca87e4da09524b9c5fa44fb5dde5c6576eb3e2412de34e8ec4b`.
- Existing tagged revision routes were retained. No environment override file
  was supplied and no secret or authentication configuration was changed.

No migration was added. The latest migration remains
`20260909020907_StoreTruckFuelPlans`, previously applied in production as recorded
in the [September 9 release](fuel-eta-release-2026-09-09.md). No manual SQL repair
or migration command was run. Schema compatibility alone does not establish that
the previous API preserves newer manual fuel-plan behavior during a rollback.

## Validation

- Local full release gate with `AMFTMS_RELEASE_UI=1` and installed Chrome passed:
  1,306 Server tests, 596 Client C# tests and 376 JavaScript tests, with no skipped
  tests; 2,278 total. Strict Release builds passed.
- All 44 offline UI cases passed. These use deterministic fixtures and are not
  production map-provider or authenticated browser acceptance.
- The local gate verified 249 published assets and seven JavaScript dependency
  graphs in `artifacts/release.J9TJCk/publish/wwwroot`; this artifact has not been
  deployed to Hosting.
- Cloud Build independently passed the same 2,278 tests and strict builds, then
  verified 249 assets and seven dependency graphs before building the API image.
- Post-rollout `GET /api/health/live` returned HTTP 200 with `Healthy` at
  `https://amftms.web.app`, `https://tms.amfcarrier.com` and
  `https://amftms-api-ddgxhwho3a-uk.a.run.app`.
- A bounded initial error query for only the new revision, after the successful
  build, returned no entries. This is not a long-running production soak.

No isolated PostgreSQL fixture was available; PostgreSQL execution checks were
not run. Authenticated production UI acceptance and exact new Hosting asset hash
checks remain pending with Client publication. Production performance was not
measured. The local API and Client were left running unchanged.

## Resume Client publication

After Firebase reauthentication, run the official `deploy-client.sh` with the
offline UI gate enabled. It creates a fresh verified artifact and deploys that
exact directory. Check public asset hashes, SPA fallback and liveness on both
Hosting domains, then record the resulting artifact and verification here. Do
not rebuild or redeploy the already successful server without another change.

Ignored local evidence: `artifacts/cycle-deploy-server.log`,
`artifacts/cycle-release-gate.log` and `Client/test-results/ui-smoke/report.json`.
