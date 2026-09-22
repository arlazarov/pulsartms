# Route chunk release — September 21, 2026

The user explicitly authorized the working-database migration, publication and
subsequent memory measurements. Application source is the working tree based on
`80216a8`, including the chunk implementation recorded in the
[local measurements](route-chunks-measurements-2026-09-21.md).

## Database and publication

The existing deployment target was verified against the workstation migration
connection without printing credentials. Private pre-migration and quiesced
custom-format PostgreSQL backups were saved under the workstation backup
directory `route-chunks-20260921T212502Z`. Archive inventories were readable;
restore rehearsal was not performed.

Cloud Run was stopped through service manual scaling at zero. Shutdown completed
and HTTP returned 503 before migration. No localhost API was started. The
reviewed EF SQL applied these migrations together in one transaction with bounded
lock and statement timeouts:

- `20260921203755_StoreRouteChunks`
- `20260921204626_RecordRouteMovement`

The complete Identity user-row fingerprint was unchanged. No operational reset
or historical-data deletion was executed. Old inline plans remain readable and
convert on their next successful write; this release does not eagerly rewrite
every historical plan.

A post-release read-only check confirmed both migration receipts, 15 saved
plans with three already converted, 293 geometry chunks and eight closed
movement chunks. This verifies actual background publication in the new format.
Evidence: `diagnostic-7tfY3b/verification.txt`.

Published API build: `862d8158-9127-4dbd-8a2e-d534f7fd913e`.
Image digest:
`sha256:90212c3c61029ed83def8458371d914ef2eac051dda7b35b85070c319923f054`.
Serving revision: `amftms-api-b-862d8158-9127-4dbd-8a2e-d534f7fd913e`.
The deployment wrapper's image/readiness and traffic guards verified 100%
traffic. Original automatic service scaling was restored; the revision still
has a one-instance maximum and 512 MiB memory limit. Old binaries must not be
restored against converted route plans.

Firebase Hosting published the verified 297-file Client artifact from
`artifacts/managed/release-pRbK5R/publish/wwwroot`. Publication used the existing
Google Cloud identity with the project explicitly selected for quota accounting;
temporary credentials were removed. No account or global CLI configuration was
changed. Live HTML and CSS bytes matched the verified files, both with
`Cache-Control: no-cache`. API liveness through both Cloud Run and Hosting
returned `200 Healthy`.

## Verification

The local release gate passed 3,138 Server, 1,052 Client C# and 625 JavaScript
tests, without failures or skips, and verified 297 assets and 11 JavaScript
entry-point dependency graphs. Evidence: `diagnostic-hd9YcH/release.log`.

The first cloud build exposed a pre-existing Border component test race between
finding a button and dispatching its event. The test now performs both actions
on the renderer dispatcher; all behavioral assertions remain. That failed build
was cancelled. The new cloud build passed 3,114 Server tests with 20 fixture
skips, 1,052 Client tests and 625 JavaScript tests. PostgreSQL tests ran locally
against the isolated fixture, not the working database.

Deployment evidence: `diagnostic-7i1xGd/deploy.log`.
Hosting evidence: `diagnostic-uPCV75/hosting.log`.
Live artifact checks: `diagnostic-cMlElb/verification.json`.
These managed diagnostic runs are pinned locally with `.keep`.
Browser interaction and a controlled 100-truck load test were not performed.

## Per-truck route memory

The read-only [probe](../../../tools/RouteChunkMemoryProbe/README.md) loaded real
saved routes after release without starting providers or background workers.
It measures retained managed data in a local Release process, not Cloud Run RSS.
Five samples each held eight independent copies; the following values use the
median per-copy retained graph plus the separately counted source-string bytes.
The scenario retains all saved plans for the truck, their calculation indexes
and display snapshots, including a second index owned by each snapshot.

| Truck | Saved plans | Converted | Coordinates in current roads | Route data MiB |
| --- | ---: | ---: | ---: | ---: |
| 11005 | 4 | 1 | 29,578 | 7.42 |
| 11006 | 5 | 1 | 55,941 | 14.04 |
| 11007 | 4 | 1 | 43,227 | 16.14 |
| 54777 | 2 | 0 | 23,592 | 9.61 |

This includes completed/historical saved plans, not just current work. It is a
conservative coexistence scenario, not a measured permanent cache allocation.
Source-string counts exclude object headers. Database access/runtime state,
telemetry and provider responses are outside the result. The reported route
graph therefore cannot be multiplied by fleet size to predict container memory.
One cold construction of each truck's complete route set allocated approximately
11.6, 24.0, 29.1 and 17.2 MiB respectively, including temporary objects.

Evidence: `artifacts/managed/diagnostic-3bw4qZ/memory.jsonl`.
Earlier single-copy GC samples varied and are superseded by this compacting,
eight-copy measurement. The tool is diagnostic-only and is not in the API image.

## Container memory

Cloud Monitoring memory utilization is multiplied by the unchanged 512 MiB
container limit. This measures the whole active server, not one truck or an
idle runtime baseline. Before release, observed minute samples were roughly
483–506 MiB. New-revision minute samples were:

| UTC time | Memory MiB |
| --- | ---: |
| 21:36 | 438.93 |
| 21:37 | 456.46 |
| 21:38 | 472.23 |
| 21:39 | 461.18 |
| 21:40 | 453.91 |
| 21:41 | 461.36 |
| 21:42 | 469.04 |

Evidence: `artifacts/managed/diagnostic-jVBHJt/memory.json`, collected through
21:43:20 UTC. The latest observation is approximately 469 MiB, leaving about
43 MiB below the unchanged limit. No ERROR-severity revision logs were found
in the final check; API liveness remained healthy. This is a short observation
window after startup, not a sustained load result.

Restart, cache warm-up, background work and differing workload prevent attributing
the whole difference to the route implementation. These observations do not
establish steady-state consumption, absence of leaks or 100-truck capacity.

Later follow-up: Cloud Run recorded a 530 MiB limit violation and restarted
this revision at 21:47 UTC, after the observation window above. See the
[runtime investigation](runtime-memory-investigation-2026-09-21.md).
