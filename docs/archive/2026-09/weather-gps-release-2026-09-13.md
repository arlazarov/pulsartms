# Weather and GPS release — September 13, 2026

The user explicitly approved publishing all latest Client and API changes.

## Gates

The local full release gate passed: 824 Client, 1,705 Server and 520 Node tests,
strict solution build without warnings, and 267 staged asset integrity checks.
The offline UI gate checked 52 page cases across 12 configurations, with no
failures, browser errors or unexpected requests. The separate Fleet matrix
passed 12 configurations against the same staged artifact.

Cloud Build `2799a413-9dc7-4512-af6e-7a2d6d7578e3` failed on two five-second
Client test waits. Server tests passed. Nothing from that build was deployed.
The unchanged source and assertions passed on an `E2_HIGHCPU_8` builder in build
`8dc8182c-411c-4148-90fb-3b5a1a268266`: 824 Client and 1,705 Server tests.
Node passed 514 tests; six existing artifact-retention checks skipped because
open-file inventory was unavailable in the container. All 520 passed locally.
Cloud's independently published Client passed 249 asset integrity checks.

## Deployment identity

- Cloud Run revision: `amftms-api-00109-zw4`, Ready with 100% traffic.
- API image: `us-east4-docker.pkg.dev/amftms/amftms/api`.
- Image digest:
  `sha256:dae74ef05141c413501e56029f9f507e28d5374481438e289e62b2aba1f0f3ca`.
- Firebase version: `b109e902d6aa8666`.
- Firebase release: `1789325217809000`, at `18:46:57.809 UTC`.
- Client artifact: `artifacts/managed/release-EpzbIb/publish/wwwroot`.
- Stylesheet version: `3ac6c502b3adcb73`.
- Client assembly: `Client.us3x83h66v.wasm`.

The approved Weather-only key was added as `GoogleWeather__ApiKey` using the
existing Cloud Run environment configuration mechanism. Every previously
configured environment entry was verified unchanged. No key value is stored in
this report or the Client artifact. Secret Manager was not enabled.

Rollback identities retained: API `amftms-api-00108-vmn` and Firebase version
`3dd5a649959f594a`. No cleanup or retention policy was changed.

## Post-deployment checks

Twenty fetched asset responses across `tms.amfcarrier.com` and `amftms.web.app`
matched the staged SHA-256 values. Entry HTML, styles and unhashed modules
revalidate; fingerprinted framework files remain immutable. Four SPA route
responses matched the staged index. API liveness returned HTTP 200 on both hosts.
A fresh anonymous Chrome session booted the real deployment to Login without
JavaScript errors. Authenticated production UI interaction was not performed.

The initial new-revision log check returned no ERROR-or-higher entries. The new
revision acquired the synchronization lease at `18:47:24 UTC`. The pre-release
checkpoint still contained GPS timestamps around `15:06 UTC` despite updates at
`18:40 UTC`; follow-up GPS checkpoint verification is recorded below.

No migration was added by this update. The read-only migration inventory reported
no pending migrations. Isolated PostgreSQL tests and real-device orientation
checks were not run. No production performance or billed-usage claim is made.

Pinned evidence:

- `artifacts/managed/browser-ui-wbrP9s`
- `artifacts/managed/browser-hours-forecast-UKyNFj`
- `artifacts/managed/release-EpzbIb/live-verification.json`
- `artifacts/managed/release-EpzbIb/production-login.png`

## Remaining production issue

The `18:53:24 UTC` checkpoint still retained the old GPS points. A scoped
read-only job-state inspection showed telemetry last succeeded at `15:06:17 UTC`,
with 19 consecutive `HttpRequestException` failures and a retry scheduled for
`19:07:54 UTC`. Other synchronization jobs continued succeeding.

The new revision attempted `/fleet/vehicles/stats/feed` at `18:52:54 UTC` and
received HTTP 400. Its telemetry job exits before reaching the independent
high-frequency stream read. Therefore the checkpoint-merging correction alone
does not resolve production stale GPS/Remaining while this feed error persists.
The exact invalid request parameter or cursor condition was not established.
No cursor, job state, freshness timestamp or business data was manually reset.

This is not a completed production GPS fix. A further synchronization correction
and separately approved rollout are required. The updated read-only diagnostic
prints job timing/error types as well as bounded truck timestamps; that tool-only
extension was compiled locally after deployment and is not deployed application
code. Post-deployment migration inventory: 33 applied, none pending.
