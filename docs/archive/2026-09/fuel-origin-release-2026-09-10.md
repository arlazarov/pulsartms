# Fuel origin release — September 10, 2026

The user explicitly requested deployment without further checks. This release
used a one-off Docker-build-only Cloud Build configuration; the maintained release
gate and deployment scripts were not changed. No tests or post-deployment
application probes were run during deployment.

Changes allow estimated initial route access within forty geographic miles and
retain a zero-mile mandatory-stop anchor when GPS matches the current leg end.
Saved following roads and actual stop-completion state remain unchanged.

Before the deployment request, `bash test.sh fuel routing` passed 1,214 server
tests, 189 Client C# tests and 19 JavaScript architecture tests. This was not a full
suite run. PostgreSQL execution tests were not run; no isolated fixture was
available. The production truck diagnostic used scoped read-only queries, not
production data as a test fixture.

- Cloud Build: `86eaa203-fb73-4647-800f-3b74e7b2773f` (`SUCCESS`).
- Image: `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:73bef4b73d8d43fcb9724d7921a68b19eec121a6621a99cfde877cf5569a6d45`.
- Cloud Run: `amftms-api-00096-c7z`, deployment command reported 100% traffic.
- Client was unchanged and was not redeployed.
- No saved fuel plans were explicitly recalculated or replaced by this release operation.
