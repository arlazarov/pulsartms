# GPS recovery, mobile HOS and style audit — September 13, 2026

## Scope and cause

The user approved fixes, optimization/style review, and publication of the full
update. Existing unrelated working-copy changes were preserved. No integration
cursor or business records were manually reset.

A bounded read-only Samsara diagnostic used the saved production feed cursor
without logging its value, credentials, URLs or provider payloads:

| Feed request | HTTP |
| --- | ---: |
| Original types, saved cursor, temperature decoration | 400 |
| Original types, no cursor, temperature decoration | 200 |
| Original types, saved cursor, no decoration | 200 |
| Original types, no cursor, no decoration | 200 |

The added decoration was incompatible with the existing cursor's parameter set.
Removing it restores continuation without losing the checkpoint. Google Weather
already supplies the visible temperature. Legacy sensor fields remain readable.

Location streaming now has its own job/backoff, independent of blocked or failed
feed reads. Network waits stay outside the publication gate. Stream positions
enter checkpoint state before publication, including the first observation of a
truck. Older feed GPS cannot replace them; sensor timestamps remain independent.
Failures log once with a status code, without verbose provider request URLs.
Routine EF command logging is Warning-level to avoid serializing normal SQL into
production logs. Error/warning visibility is retained.

## Cycle and weather

At an already observed active stop, live clocks cannot reconstruct the cycle at
the earlier arrival. The additive `CurrentCycleMinutes` carries the known current
balance separately. Both Blazor and map JavaScript use it for Cycle remaining
when the historical arrival balance is absent. They never substitute departure
hours or another stop's forecast. Current cycle does not grant missing recap
credits, and unverified projections remain unverified.

A cold weather placeholder retries after fifteen seconds, bounded to five prompt
attempts, then once a minute. Success restores the ten-minute cadence. The
server's bounded, coalesced per-truck weather cache is unchanged. Temperature
units and independent GPS/HOS freshness checks are unchanged. Google's required
attribution moved from the truck card to the map key; it was not removed from
the product.

Mobile HOS fills four equal slots beside telemetry. Dials grow to the shared
58px token and shrink to the available field. At enlarged text, labels can wrap
inside their slots rather than enlarging the child grid and overflowing.

## Performance inspection

These are local, read-only diagnostic observations against the configured remote
database, with four trucks and four board loads. They are not production latency
benchmarks. Cache warmth and network latency differ across samples.

| Read | SQL commands | Observed elapsed |
| --- | ---: | ---: |
| Core board, cold application caches | 3 | 297 ms |
| Core board, warm | 1 | 69 ms |
| Full board, cold | 18 | 13,960 ms |
| Full board, warm samples | 11 | 5,154–7,275 ms |
| Financial enrichment | 6 | 889 ms |
| ETA enrichment | 5 | 712 ms |
| Identity-only warm board | 0 | 0.85 ms |
| Selected route preview, cold | 8 | 2,356 ms |
| Selected route preview, warm | 2 | 287–768 ms |
| HOS provider read | 1 credential read | 784 ms |

Actual route response inspection found 468–1,152 KB uncompressed initial
previews and 13–15 KB version-matched planning reads. All four matched reads
omitted geometry and retained fuel metadata. Local Fastest gzip samples were
118–284 KB initially and 5.1–5.6 KB subsequently. These are sample encoded body
sizes, not measured browser wire totals. ETA was absent in this diagnostic's
planning snapshots, so this is not an upper bound for every enriched response.

Keep initial Dispatch core/enrichment separation, per-selection previews,
versioned geometry, shared HOS and weather snapshots, cancellation, bounded
caches and hidden-page polling controls. Further optimization should target
repeated financial/ETA database reads and cold route JSON transfer, with
transaction/invalidation tests before adding broader caches. Do not replace
freshness checks with longer stale-data lifetimes. No new long-running heap soak
was performed; this audit does not establish the absence of all memory leaks.

## Style and verification

Pinned Prettier 3.6.2 now covers maintained Client JS/TS, SCSS, build scripts and
fixtures with an 80-column target, two spaces and single quotes. It is build-only,
not downloaded by the app. The release gate checks formatting. CSharpier checked
991 C# files; one migration's indentation was corrected without schema changes.
Long indivisible literals/regexes retain their contents. Existing architecture
and style assertions were retained; whitespace-sensitive assertions now accept
formatter line breaks and the new gate step is checked explicitly.

- Full local release gate: 828 Client, 1,706 Server and 521 Node tests passed.
- Strict solution and Debug Client/API builds: zero warnings/errors.
- JS type check, Prettier, CSharpier and `git diff --check`: passed.
- Offline primary UI: 52 page cases across 12 size/theme/text configurations.
- Fleet inspector: 12 configurations, including 320–767px and 200% text probes;
  no failures, browser errors or unexpected fixture requests.
- New regressions cover blocked-feed GPS publication, first stream observation,
  current-stop cycle in C#/JS and prompt weather recovery.
- One full run failed the existing allocation threshold (6,320 versus 4,096
  bytes). The isolated test and subsequent full gate passed with no threshold or
  test changes. No leak or runtime-cause claim follows from that intermittent
  result.
- Real Google Weather lookup succeeded. Migration inventory: 33 applied, none
  pending. No new migration was added.
- No isolated PostgreSQL execution fixture or authenticated live browser review
  was available. The desktop browser-control runtime failed to initialize.
  Offline fixtures are not a substitute for actual provider/GPU acceptance.

Staged Client: `artifacts/managed/release-qNktch/publish/wwwroot`.
UI evidence: `browser-ui-8gyzWN` and `browser-hours-forecast-NNk2A1` under the
managed artifacts directory. Cloud Build
`b40a9553-9c34-4c29-9028-4729030ea6d1` passed with the high-CPU build machine.
API digest: `sha256:824751704210da812e72272fa00563fb96a5380729e8fa0898e7bcf62fe7a691`.

## Published result

API revision `amftms-api-00110-ph9` is ready and serves 100% of traffic. Firebase
version `03528e01428ab522`, release `1789329983363000`, was published at
`20:06:23 UTC`. Previous API revision `amftms-api-00109-zw4` remains the rollback
identity. No environment values changed; the before/after environment checksum
matched, including the configured weather key.

Both public hosts returned matching SHA-256 bytes for twenty sampled deployed
assets, four SPA routes, correct cache headers and HTTP 200 API liveness. A fresh
Chrome session booted the actual production Client to Login with no JS errors.
CSS version is `fdf5420156c22295`; Client assembly is `Client.tighma0l9g.wasm`.

The new revision acquired the synchronization lease at `20:07:25 UTC`. The
`20:09:26 UTC` checkpoint confirmed recovery: moving trucks 11005/11006/11007
had GPS samples around `20:08:55–59 UTC`; parked, engine-off 54777 had a
`20:04:08 UTC` sample. Both telemetry and independent location jobs reported
successful runs and zero consecutive failures. This replaces the previous
`15:06 UTC` stuck feed, without manually changing cursor or freshness timestamps.

Fresh persisted forecasts were present for all four trucks. Truck 54777's
`20:09:54 UTC` forecast had a verified current-stop cycle of 3,837 minutes
(63h 57m) and a separate later arrival forecast of 2,418 minutes. Its historical
arrival balance remained null; the current value is no longer lost in the UI.

No new-revision synchronization/ETA failure appeared in the scoped log check.
The startup HTTPS redirect-port warning remained; it is not a GPS failure.
Direct Weather provider validation returned 35.7 Celsius and MOSTLY_CLEAR.
Authenticated production card interaction remains unverified; do not equate
fixture screenshots or provider checks with a signed-in end-to-end browser run.
