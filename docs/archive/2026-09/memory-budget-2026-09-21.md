# Memory investigation, September 21

Cloud Run reported 514 MiB used against the 512 MiB limit at
19:08:37 UTC. Minute-level container metrics rose from approximately 263 MiB
at 18:48 to 499 MiB at 19:08. The 263 MiB observation is not an idle baseline.
After the restart, observations returned to approximately 460–470 MiB.
No heap profile was captured, so neither a leak nor its owner is established.

A read-only production query found 15 route plans containing 11,659,035 bytes
of JSON in total, with the largest containing 1,877,542 bytes. Serialized
storage size is not managed-heap size or container working set.

## Local corrections

- Truck history uses a dedicated, company-scoped cache with an 8 MiB estimated
  retention budget. Previously it used the shared cache without a size limit.
  Existing 26-hour expiry and incremental refresh remain. Entries exceeding the
  budget are not retained; evicted histories require fetching again.
- Page loading no longer sorts, deduplicates and copies the accumulated history
  after every provider page. It publishes once after all pages succeed.
  Existing cached history survives a provider error during refresh.
- The deployment wrapper retains 512 MiB. The proposed 1 GiB change was not
  deployed and is withdrawn pending measurement.

The budget estimates retained point objects, strings and reference arrays. It
does not impose a hard limit on temporary provider responses, the managed heap
or process memory. Route JSON deserialization, fleet preview reconstruction and
other caches still need allocation/retention profiling before further changes.
No measured production memory reduction is claimed. No deployment or migration
is part of these corrections.

Regression tests cover company separation, oversized entries, aggregate cache
admission and atomic publication across successful and failed pagination.

## Validation

The full suite passed with 3,107 server tests, 1,052 Client tests and 625
JavaScript tests. After adding the pagination and aggregate-budget regressions,
the Fleet dependency group passed with 495 server tests, 286 Client tests and
470 JavaScript checks across feature and architecture runs. No failures or
skips were reported. Evidence is retained in the managed
`diagnostic-BYYQeL` run. Browser behavior and production heap retention were not
measured. The corrections do not establish that 512 MiB is sufficient.
