# Managed artifact retention — September 11, 2026

Added the shared local runner/retention policy in `scripts/artifacts.mjs` and
connected it to `test.sh`, default release verification, Client deployment and
all ten browser output producers. Explicit output-directory overrides retain
caller ownership and are not registered for deletion. The project rules now
require the managed runner for temporary builds/diagnostics.

The user reduced the proposed target from three GiB to **one GiB**. The policy
keeps the two latest runs per fixed kind, the last successful result, active/open
outputs, `.keep` pins and the last hour of work. Other registered runs expire
after seven days or are removed oldest-first above the target. Protected outputs
can exceed that soft target. Legacy directories and Trash are untouched.

Automatic deletion is limited to registered generated outputs inside
`artifacts/managed`; it does not fill Trash again. Missing open-file inventory,
invalid markers, unexpected paths and linked trees fail closed. A filesystem
regression exposed macOS `/var` versus `/private/var` path aliasing; canonical
paths now protect the actual open file before applying any removal.

Verification:

- `bash test.sh all`: 1,429 Server, 635 Client C# and 409 JavaScript tests passed
  (2,473 total; none skipped), including ten retention regressions and unchanged
  release gate checks.
- The native-inspector probe passed all eight cases using the new default managed
  output directory; its lease was marked complete/successful.
- The command runner completed the full test command, preserved its status and
  marked its diagnostic output successful.
- Shell and Node syntax checks passed. A final dry-run cleanup found no expired
  managed output; the existing localhost listeners on 5067/5086 remained running.

No deployment, database migration, credential modification, live provider request
or production load measurement was performed. No fresh Client build was required:
changes were limited to development/test/release tooling and documentation, not
Client production C#, Razor, styles or contracts. The staged inspector test used
the already-published isolated artifact from the preceding cleanup audit.

See [maintained policy](../../development/artifact-retention.md) for current usage.
