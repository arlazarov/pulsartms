# ETA, cycle and current-duty release — September 8, 2026

Deployed at approximately 21:48 America/Toronto (September 9, 01:48 UTC).

## Deployment

- Firebase Hosting: `https://amftms.web.app`, verified Client artifact
  `artifacts/release.ULcd2P/publish/wwwroot`.
- Cloud Build: `df57e07f-2070-461d-aaf0-2ecce2cab255`.
- API image digest: `sha256:20ce9afe1e794b4859f0d050e3a5006f30dc19c98564dab364e9eb5b7a598c02`.
- Cloud Run revision: `amftms-api-00079-xkx`, Ready and serving 100% of traffic.
- Previous ready revision: `amftms-api-00078-hh4`.

Includes the preceding quiet ETA/cycle display, sleeper-service planning and
country-specific reset countdown work. Current duty status is now beside the
map's HOS clocks instead of the destination forecast, with explicit elapsed-time
wording such as `Current status: Driving for 35 min`.

## Verification

- Full release gate: 626 Server, 362 Client and 169 JavaScript tests passed.
  Strict solution build completed with zero warnings and errors.
- Verified 222 published assets and six JavaScript entry-point dependency graphs.
- Offline UI smoke: all 44 cases passed. The exact published artifact also passed
  four viewport/theme ETA and duty-status scenarios, including pending retention.
- Published index and stylesheet bytes match the verified local artifact.
- Production Fleet Map page and API liveness returned HTTP 200, both through
  Hosting and directly to the API. Cloud Run readiness and traffic were checked.

Authenticated production workflows and the Admin-only database readiness endpoint
were not exercised. Real PostgreSQL fixture checks and a new authenticated map
provider lifecycle test were not run. Fixture/browser checks do not prove production
performance or actual ELD compliance. No migration was created in this release task.
