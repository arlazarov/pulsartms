# ETA batch reads: implementation and paired measurement

Follow-up: [native history batching](eta-native-batch-comparison-2026-09-21.md)
reduces the remaining per-truck reads while retaining original eligibility.

## Change

Application selects each truck's ETA work before fetching future road
versions. Future versions are read together, then partitioned by dispatch and
execution-leg identity. Historical source candidate reads combine disjoint
dispatch groups inside the existing read snapshot. Groups sharing a dispatch
identity remain separate, retaining their supplied facts.

Native predecessor eligibility still runs for each original history batch.
This preserves completed-leg selection, original batch membership and later
publication replay. This change does not remove those remaining per-truck
reads, weaken freshness checks or parallelize a shared DbContext. Profiles,
work selection, input hashes, geometry hashes and publication rules are
unchanged. No persistence migration or production release is required for
this Application-only change.

## Paired results

Both builds ran sequentially against the same isolated PostgreSQL schema
containing 50 trucks. Linux ARM64, one CPU, 1 GiB and workstation GC were
unchanged. Each build restarted, then handled the same page-one ETA enrichment
request four times. The page contains 12 trucks; this is not a timed refresh
of all 50. Tests/builds were completed before measurements. The normal browser
remained open; CPU totals include its polling and ordinary background work.

| Repeated requests, median of three | Before | After |
| --- | ---: | ---: |
| SQL commands per request | 88 | 55 |
| Wall time, seconds | 4.551 | 3.887 |
| Sum of command durations, seconds | 3.798 | 3.045 |
| Container CPU time, seconds | 0.651 | 0.548 |

Query count fell by 37.5%, and median wall time by 14.6%. Repeated wall times
were 4.479–4.599 seconds before and 3.478–4.368 seconds after. The first
requests took 6.079 and 5.509 seconds respectively, with 108 and 76 commands.
Network variation and three repeated samples limit timing conclusions;
CPU totals are not isolated per-request profiles. No new memory saving is
claimed from this experiment.

The final parsed HTTP response was exactly equal across builds, including
stored forecast values. Forecast validity times were unchanged; the fixture
does not periodically regenerate ETA, so this is not proof of fresh forecasts
for every truck. Automated tests separately compare complete single-versus-
batch descriptions, normalizing only the observation timestamp.

## Validation

All 4,856 tests passed: 3,179 server, 1,052 Client C# and 625 JavaScript, plus
JavaScript type checks. New regressions cover two independent trucks with
future work and repeated dispatch identities with distinct captured inputs,
including empty batches. Existing native/legacy ETA, history and publication
tests ran in the full suite. Release probe publish and formatting checks passed.

The first new equality test failed solely on differing snapshot AsOf times;
the test now normalizes that timestamp while comparing all other serialized
description data. No production invariant or architectural check was relaxed.

Evidence under `artifacts/managed`:

- `diagnostic-U9z8E3`: paired SQL/CPU measurements and exact HTTP responses.
- `diagnostic-tHpj0q`: before executable.
- `diagnostic-UAfCxS`: after executable, currently running locally.
- `diagnostic-KFQwRZ`: complete test output.

The remaining 55 queries still warrant investigation. Native predecessor
hydration must be batched with explicit original-group eligibility before it
can safely replace the per-truck reads. This improvement is measurable but
does not establish that all ETA read inefficiency has been eliminated.
