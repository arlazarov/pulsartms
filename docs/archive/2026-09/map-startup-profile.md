# Local map startup profiling

These measurements used a temporary ten-second localhost-only startup probe.
The probe, query flag and loopback reporting have since been removed from the
application. This document retains the findings, not an active profiling interface.

September 7, 2026, authenticated Codex browser, selected truck:

- Before removing fleet-wide preview preload: 2713, 594 and 539 ms long tasks.
- Without preview preload: 1346 and 556 ms long tasks; truck delivery stage moved
  from 3519 ms to 319 ms in those runs.
- Generated JSON metadata did not remove the remaining long tasks.
- A command-line Jiterpreter-disabled build did not remove them either.
- Reading successful API envelopes directly from the response stream removed the
  preliminary string allocation and JsonDocument parse. Two subsequent runs
  recorded 744 + 576 ms and 748 + 584 ms long tasks, respectively, versus roughly
  1340 + 580 ms before this change. Error responses retain Problem Details handling.

These are individual local runs, not statistically controlled benchmarks.
Long-animation-frame attribution points to the .NET native runtime timer callback.
Subsequent isolation identified synchronous JSON reading and JS interop payload
serialization as major contributors. Successful responses now yield between
16 KiB input chunks. Map payload serialization yields on async buffer flushes
and transfers UTF-8 bytes, avoiding a second synchronous managed serialization.
The map projection omits duplicate flattened geometry and unused plan fields.
Two authenticated selected-truck startups recorded no tasks longer than 50 ms
and one 52 ms task, respectively, after these changes. Route delivery latency
remains separate from input blocking. Manual marker switching is not covered by
these page-load measurements.
