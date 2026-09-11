# Map layer preparation measurement

Run: `node tools/map-performance-probe.mjs` from the repository root.

## Method and limits

This is a CPU-only cache ablation, **not a historical before/after application benchmark**.
The current scene is compared with an in-memory control that disables layer-group
memoization and rebuilds stop snapshots on every render. Production files are not
modified. Both use real deck.gl layer constructors from the browser bundle, but a
mock overlay: no attribute manager, GPU uploads/draws, picking pass, Google map,
network, DOM layout or actual display frames are measured. Stop DOM is mocked.

Fixture: 3 moving trucks, 1,000 stations, 2 stops, one fixed 2,000-point route.
Seven alternating samples of 30,000 synchronous updates, after warmup. The route,
station prices and distance labels stay fixed. This isolates truck-only updates;
it does not model continuously trimming route geometry or live price changes.

## Recorded result on the local Mac

| Metric, per 30,000 updates | Cache-disabled control | Current scene |
| --- | ---: | ---: |
| Median CPU duration | 179.66 ms | 81.81 ms |
| Min–max CPU duration | 169.32–182.28 ms | 80.13–103.83 ms |
| Layer object constructions | 180,000 | 60,000 |
| Layer data-reference changes | 150,000 | 60,000 |
| Mock stop DOM queries | 60,000 | 0 |

Median CPU reduction in this isolated task: 54.5%. Layer construction reduction:
66.7%; data-reference changes: 60%. Data-reference changes are **not measured GPU
buffer uploads**. Absolute median preparation time is approximately 0.0060 versus
0.0027 ms per update; these numbers must not be presented as full frame time.

The open in-app map has a 633×662 CSS-pixel map with 1266×1324 canvas backing
resolution, including the foreground fleet canvas. Retina resolution remains 2×
on both axes. This confirms no resolution reduction, not GPU load improvement.

## What remains unmeasured

GPU utilization/time, whole-page CPU, FPS, frame-time percentiles during continuous
zoom/pan, and total memory before/after. A historical or controlled browser A/B
trace with the same viewport, data, camera sequence and foreground state is
required before giving a whole-map percentage. Moving routes to the foreground
canvas also changes draw composition; removing one overlay does not mean halving
GPU work.
