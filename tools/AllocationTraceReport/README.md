# Allocation trace report

This diagnostic reads GC allocation ticks from an EventPipe trace and groups
sampled allocation amounts by type and call stack. Counts estimate cumulative
allocation traffic, not live heap size or retained objects. Inlined frames can
be absent; verify candidate hot paths in source and with regression tests.

Build through `scripts/artifacts.mjs` and pass one `.nettrace` file to the
compiled executable. Conversion creates an adjacent `.etlx` file, so traces
must also live in a managed diagnostic directory. Keep raw traces private;
publish only reviewed aggregate results. Use synthetic fixture traffic for
profiling, with a bounded EventPipe buffer. A profiler changes process load;
measure uninstrumented container memory separately.

An optional second argument skips the trace's first seconds. The report
also rolls sampled bytes up by the nearest application frame (what
allocated) and the farthest (the operation it ran under); asynchronous
continuations can cut the stack short, so the farthest frame is the
outermost one sampled, not always the originating request or worker.

`--cpu` counts the EventPipe sample profiler's samples of managed code
running (about one per millisecond per running thread) instead of
allocation ticks, rolled up the same way. Samples of waiting threads are
left out, and so is CPU spent outside managed code. The profiler suspends
the runtime at each sample, so a sampled process runs slower. `--events`
lists the providers and events a trace holds.
