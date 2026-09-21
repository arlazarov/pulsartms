# Dispatch and fuel release — 2026-09-21

The user explicitly requested commits and production publication. Application
source `7df6546` on `main` includes `db238cd`, `4d2ad93` and `7df6546`, all pushed
to origin. Cloud Build received a clean Git archive; the workstation's untracked
Claude launch configuration was excluded. No migration or data reset was needed.

## Verification and publication

The local `verify-release.sh` gate passed 3,103 server, 1,052 Client C# and
623 JavaScript tests without skips or failures. It verified 297 published assets
and 11 JavaScript dependency graphs. The Client artifact is
`artifacts/managed/release-TGNsvc/publish/wwwroot`; gate evidence is
`artifacts/managed/diagnostic-Vf0DEh/release.log`.

Cloud Build `255ec64d-cf28-4bbc-9b22-49999967a324` succeeded. Its server run
reported 3,081 passes and 18 skips without failures; PostgreSQL fixture tests
were unavailable in Cloud Build but ran locally. Published image digest:
`sha256:6c1c4267cebf8617a10b39fdc7e929cf6daf9e941bec08e27ee1c55c539f1edc`.

Cloud Run revision `amftms-api-b-255ec64d-cf28-4bbc-9b22-49999967a324` was created
without traffic. The wrapper's first readiness check failed before traffic
changed. A fresh read showed Ready=True and the expected image digest; the same
revision verifier then passed. Traffic was explicitly switched to this revision,
and the unchanged traffic verifier confirmed the requested and observed 100%
allocation. Evidence: `artifacts/managed/diagnostic-78X2Ym`.

Firebase Hosting published the verified 297-file Client artifact to
<https://amftms.web.app> using the existing Google Cloud identity. Public index
and CSS bytes matched the artifact and returned `Cache-Control: no-cache`.

## Production checks

- Public liveness returned `Healthy`.
- Authenticated readiness returned HTTP 200.
- The active Dispatch board for truck 11005 no longer included AMF1370, AMF1379
  or AMF1383. Each load was returned as completed by the Completed query.
- The first ERROR-level log query for the new revision returned no entries.

The authenticated checks used read endpoints only after login and retained no
access token on disk. Supporting managed runs are `diagnostic-XFlp25`,
`diagnostic-1GorKL` and `diagnostic-FuoOun`. Browser workflows, sustained worker
health and production performance were not measured. No local API was started.
