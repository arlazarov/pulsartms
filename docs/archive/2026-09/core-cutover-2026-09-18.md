# Core production cutover — September 18, 2026

Times in this record are UTC. The operator approved publication after renewing
Google Cloud authentication. The earlier approval selected the working database
and permitted replacement of operational data while preserving users.

## Applied database transition

The old API was stopped with Cloud Run manual scaling at zero. Its idle pooled
connections were drained; the database reported no other client before backup
and again before the guarded transition. A fresh custom-format backup was saved
outside disposable artifacts with private directory/file permissions.

- Recovery file: `~/.local/share/pulsartms/backups/`
  `core-cutover-20260918/quiesced.dump`.
- Size: 231,569,028 bytes.
- SHA-256:
  `865f8145bd234d094bce8240c3248fd4d3ba6f3b8d5c456a230633f5a5dfd986`.
- Verification: archive inventory, all ten protected table-data entries and
  full archive decoding. A complete restore of this backup was not performed.

The reviewed operational reset and generated EF upgrade were combined into one
transaction without changing their statements. The exact combined SQL was
rehearsed on an isolated fixture on the existing PostgreSQL server; that fixture
was removed. No container database was started.

The transition committed at `2026-09-18T03:01:43Z`. All 49 operational tables
were reset. Protected row fingerprints matched before and after both operations;
two application users and two identity users remained. Credentials, roles, key
material and preferences were not exported to diagnostic evidence.

Migration `20260917055902_RebuildExecutionStorage` is applied: 40 migration
history rows, typed accepted stops, immutable execution revisions and all 17
enabled planning-input triggers. The old `ExecutionVisits` table is absent.
The combined SQL checksum is
`93edf2ac6d7e0adefc2f2e5ea3603f5eff2eaaa2fd569574b0fd62b124c78552`.

## Published release

Initial Cloud Build: `119010d4-bffb-46b8-a289-38d68183e076`.
API revision: `amftms-api-b-119010d4-bffb-46b8-a289-38d68183e076`.
Image digest:
`sha256:56c7b671a4ac2819ad9be22687e9a5162203734ea02aa256b5fe9ff0bd819822`.

The new revision was verified ready at the exact image digest before receiving
100% of traffic. Only then was automatic scaling restored to the prior service
minimum of one and maximum of twenty; revision maximum remains one. No old
traffic tag was enabled against the new schema.

Firebase Hosting version: `bb117d62c20a3fe8`. Publication used the exact verified
Client from `artifacts/managed/release-HBn24c/publish/wwwroot`. Public HTML, CSS
and the fingerprinted Client WASM matched the staged bytes. HTML/CSS revalidate;
fingerprinted WASM has immutable caching. Existing Hosting rewrites remain.

The saved Firebase CLI login had expired. Publication used the renewed Google
Cloud access token through the CLI's supported token environment and the same
project's quota header. No credentials or project permissions were changed.

## Live acceptance and follow-up

The existing browser session remained authenticated as an administrator. Both
users were visible. Dispatch, Fleet Map, current route geometry, telemetry and
an ETA loaded through the published Client. Initial source bootstrap produced
20 loads, four trucks, nine drivers and six accepted execution legs. These are
observations, not fixed expected fleet counts.

Live verification found native mileage requests retrying: base roads used
resolved street coordinates while accepted stops retained imported city-level
coordinates. A follow-up adds accepted-address verification through the common
execution acceptance/history owner and shares the existing address resolver.
Version conflicts and protected mileage reject late location changes. Original
source replay retains verified accepted coordinates; changed source locations
still require review.

That follow-up passed all 2,731 Server, 1,015 Client and 561 JavaScript tests
locally. Cloud Build `8e9a9ecf-0470-445b-8db0-f09963c9a082` completed and
published revision `amftms-api-b-8e9a9ecf-0470-445b-8db0-f09963c9a082` with
verified 100% traffic. Its image digest is
`sha256:611976b1aa5c11ea922f5a1d4eb48b6d25f7cd4a7f854f6fb1469af0ea4957c3`.
No additional schema migration or Client publication was needed.

The first read-only check after publication found valid signatures, geometry and
miles for all six roads; five matched accepted-stop anchors, while one planned
road still did not. Seven movement records now existed. This demonstrates partial
recovery, not complete queue acceptance. Evidence is in managed diagnostic runs
`diagnostic-SQK8zw`, `diagnostic-btkmgQ` and `diagnostic-NWPoQ1`.

The reported driver edit applies to a whole existing assignment. A yard handoff
requires separate outgoing/incoming assignments through the existing contextual
Drop/Hook or truck/driver Switch workflow. Ordinary Pickup/Delivery placeholders
do not create that boundary. No real assignments were changed during inspection.

## Evidence and limits

Pre-cutover gates, isolated PostgreSQL scenarios and offline browser matrices
are recorded in [the local readiness report][readiness]. Live checks do not
replace them. Provider-disabled create/assign/complete was exercised in the
isolated fixture, not by creating synthetic work in production. A fresh password
login and every personal-preference combination were not exercised live; the
protected data comparison and existing-session check are the available evidence.

No production throughput improvement or complete visual correctness is claimed.
Fuel stations/import sources were operational data in the approved reset; the
initial bootstrap had no saved fuel plans. Full fuel-price recovery remains to
be verified. The backup proved an existing application-owned Gmail registration
from September 9, with successful recovery immediately before cutover. Only that
registration checkpoint was recovered, with an expired old lease and no target
row; normal bounded catch-up was made due. No new mailbox ownership or external
renewal schedule was introduced. The first recovery attempt recorded
`OperationCanceledException`; fuel stations remained empty at the follow-up
check. Fuel recovery therefore remains open. Accounting, settlements, toll
pricing and actual-expense imports remain later product work.

[readiness]: immutable-consumers-and-cutover-readiness-2026-09-17.md
