# Fuel stop economy release — September 10, 2026

The user explicitly requested deployment without further checks. The one-off
Docker-build-only Cloud Build configuration was reused; maintained release scripts
and gates were not modified. No tests or post-deployment application probes ran
during this deployment.

Automatic selection version 27 requires at least $20 savings per additional fuel
stop between feasible alternatives with the same schedule rank. This threshold
does not increase financial totals. Single-stop omissions retain intermediate
stop-count alternatives, with quantities re-optimized across the full horizon.
Schedule replay pruning uses the same preference. Manual plans are unchanged.

Before the deployment request, `bash test.sh fuel routing` passed 1,228 server,
189 Client C# and 19 JavaScript architecture tests. This was not a full suite run.
The captured 11006 regression selects a full second fill and omits the third
purchase while preserving final fuel. PostgreSQL execution tests were not run:
no suitable isolated fixture was available.

- Cloud Build: `e870b693-a5d2-48a6-83ff-bb7bc0c28637` (`SUCCESS`).
- Image: `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:c064201a2235952aca66b0a70d3f2599b8336f423c60c1ed856a3cb56430dcd3`.
- Cloud Run: `amftms-api-00097-67d`, deployment reported 100% traffic.
- No Client changes or Firebase deployment.
- This deployment did not explicitly recalculate or replace saved truck fuel plans.
