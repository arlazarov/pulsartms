# Company isolation release — 2026-09-21

The user explicitly authorized migration, server publication and commits after
the [local corrections](company-boundary-fixes-2026-09-21.md).
Application source commit: `a9836eb` on `main`, pushed to `origin`.

## Database transition

Cloud Run was already stopped with manual scaling at zero, and neither local
API nor Client was listening. Old test revision tags were removed before the
transition. A private custom-format PostgreSQL backup and the prior service
configuration were saved under the workstation's PulsR backup directory,
`company-isolation-20260921T175739Z`. The backup archive inventory was readable;
a restore rehearsal was not performed for this backup.

The working database had all migrations through
`20260921152824_FreeTheServerCheckpointsFromCarriers`.
The EF-generated SQL for
`20260921174619_IsolateCarrierIntegrationCredentials` was reviewed and applied
in one transaction with bounded lock and statement timeouts. Credential
ciphertext, revisions and timestamps matched before and after. The Identity
user count was unchanged, and existing credentials belong to the original
carrier. No operational reset was executed.

## Published artifacts

- Cloud Build: `85dd8597-eeff-4698-ad8e-e70027427a2a`, successful.
- Source upload came from `git archive a9836eb`, excluding workstation files.
- Cloud Run revision:
  `amftms-api-b-85dd8597-eeff-4698-ad8e-e70027427a2a`.
- Image digest:
  `sha256:802cd2c067c10b9e7d54195cfe290e2b914cd6d063ab03cdbc16c97f2780b585`.
- Readiness, image identity and 100% traffic were verified by the deployment
  wrapper before service scaling was resumed through the Cloud Run v2 API.
- Automatic service scaling is active. The deployment wrapper sets a
  one-instance revision maximum and the background liveness probe.
- Firebase Hosting published the matching 297-file Client artifact to
  <https://amftms.web.app>. The existing Google Cloud identity was used because
  the separately cached Firebase login had expired.

## Verification

The local `verify-release.sh` gate passed with 3,091 server, 1,052 Client C# and
623 JavaScript tests, with no failures or skips. It verified 297 published
assets and 11 JavaScript dependency graphs. PostgreSQL fixture checks ran here.
Cloud Build repeated the release gate successfully; its server run reported
3,069 passes and 18 skips because no PostgreSQL fixture was configured there.

After publication, the API liveness endpoint and the Firebase API rewrite both
returned `200 Healthy`. Published index and CSS bytes matched the verified
artifact, and both revalidate with `Cache-Control: no-cache`. The protected
readiness endpoint returned 401 without an application Admin session, as
configured; this is not an authenticated readiness check. Initial log queries
for the new revision found no entries at ERROR severity or above.

Evidence is retained in managed diagnostic runs `diagnostic-lr0XkD` (local
release gate), `diagnostic-PfENXh` (server deployment), `diagnostic-qrGBRa`
(Hosting publication) and `diagnostic-Fu4b9r` (public artifact verification).
Each is pinned with `.keep`; the staged Client is under `release-PcyNdG`.

Browser workflows, authenticated production feature checks, sustained worker
health and production performance were not established by these probes.
No local API was started. Local secrets still target the working database;
restore isolated development configuration before the next local API launch.
