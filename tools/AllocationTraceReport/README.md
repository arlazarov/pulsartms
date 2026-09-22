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
