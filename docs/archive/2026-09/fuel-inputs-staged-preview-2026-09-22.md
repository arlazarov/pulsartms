# Three previews with the new stages — 2026-09-22

Taken by Codex after `dbce8b3` was deployed, on revision
`amftms-api-b-76d896cf-…` at 100% traffic, 1 GiB, GC unchanged. Three previews
of load 11006, all successful, the saved fuel plan untouched. Evidence:
`artifacts/managed/diagnostic-HWygsl/`.

## What the three runs read

| | run 0 | run 1 | run 2 |
|---|---|---|---|
| `EditFuelPlanCommand` | 689.57 | 372.21 | 408.78 |
| `fuel-edit/inputs` | 208.69 | 212.22 | 191.13 |
| `fuel-edit/horizon` | 110.04 | 70.90 | 86.31 |

**The 966.8 ms reading did not recur.** These three repeats are fast, and that
is all they establish. Three runs are not a stable speedup and none is claimed.

## Only the third run can be broken down

`itinerary-read/read-total` has a count of 4, 2 and 1 across the runs, and in
run 1 the child counts disagree with it — the snapshot window caught background
itinerary reads, so subtracting inside those two runs would be arithmetic over
different work. Run 2 is the one where every row is `n=1`:

| stage | ms |
|---|---|
| `itinerary-read/read-total` | 170.80 |
| `itinerary-read/work-batch` | 128.84 |
| `itinerary-read/evidence` | 41.69 |
| `itinerary-read/assemble` | 0.25 |
| `itinerary-read/legacy` | 0.01 |
| **unmeasured remainder** | **0.01** |

The parts account for the whole: the reader has no unnamed stretch left. Its
cost is the work batch and the evidence read, and `legacy` at 0.01 ms is the
guard from `3f58d40` declining to ask.

Matching counts are not proof that these rows belong to the measured request —
the counters are per process and a quiet window is not an isolated one. They
are consistent, which is the most this method gives.

## The scope counters cannot be attributed

`execution-scope/open` counted 7, 4 and 6 and `/commit` 7, 4 and 5, against one
preview each. Twelve callers share that scope, so those rows carry background
work and must not be charged to the preview. Within them, `open` totals 0.24,
0.14 and 0.24 ms across all those calls, so acquiring a connection and
beginning the transaction is not where time goes — the one thing these rows do
say.

## Where this leaves it

Current repeats are fast; the cause of the rare spike is **not established**,
in either direction. The per-request breakdown stops at the process-wide
boundary: isolating one request would need per-request stage capture, which
does not exist and is not proposed here.
