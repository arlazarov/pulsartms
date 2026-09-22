# Heap retention report

Reads a local `.gcdump`, totals shallow object sizes by type and reports one
shortest root path for each of the largest objects. Paths prove reachability;
they are not exclusive ownership, retained sizes or dominator analysis. Type
names and root descriptions are emitted, never object string contents.

Build with `GcDumpAssembly` pointing to the installed `dotnet-gcdump.dll` from
the pinned diagnostic tool version 10.0.745401. The graph reader is supplied by
that tool. Run builds and report output through the managed diagnostic runner.
Pass one local `.gcdump` file to the compiled executable. Keep raw dumps private.
No production process or database is contacted by this reader.
