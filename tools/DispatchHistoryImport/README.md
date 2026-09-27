# Authorized historical dispatch import

One-off AMF operation; preview is the default. An explicit `--apply` requires
an existing backup path and its matching SHA-256. Date ranges are inclusive,
limited to one year and cannot extend beyond today. Credentials come from the
existing local application configuration and company-scoped credential owner;
never print or save them in reports.

Run from the repository root. Build through the managed scratch runner, then
run the compiled tool through the diagnostic runner. Supply `--from=YYYY-MM-DD`
and `--to=YYYY-MM-DD`. Apply also requires `--backup=PATH` and
`--backup-sha256=HASH`. Publication and import authorization are separate.

The existing Torque adapter reads complete paginated 30-day windows. The tool
skips already linked provider identities; it does not refresh existing loads.
The existing SyncDispatchesCommand owns new source links, number allocation,
resource resolution, stop mapping, accepted execution and serializable writes.
A concurrently inserted source identity is still protected by that owner;
conflicts stop the tool instead of blindly retrying. Previously committed
windows remain committed and a later run skips their links.

Only AMF is selected. The tool starts no host, worker, migration or outbound
message operation. It preserves normalized source status and actual facts; it does not
invent delivery confirmations for historical loads. After each committed
window it checks that every source identity has a company-scoped link.

Preview and apply print counts/status distributions only. Source payloads and
personal data are not retained. No runtime auditor changes are needed: the
existing import owner and persistence guards are reused without modification.
Identity verification does not prove completeness of data missing at Torque,
financial readiness, or that every record qualifies for the Completed view.

Operational limitation: this process does not invalidate another process's
in-memory display caches directly. Their existing freshness/reconciliation
policies remain in effect. Do not claim an immediate UI refresh or run forced
production route preparation. Stop on an import conflict and inspect it.

## Invoice-status reconciliation

Torque's `sent` is an invoice-sent status for a completed load. Its adapter
normalizes this to `completed`; no arrival, departure or delivery timestamp is
created. Use `--reconcile-invoiced` to preview existing linked records still
stored as `sent` whose current provider result normalizes to `completed`.
Combined with `--apply` and the verified backup arguments, only these records
are refreshed through the ordinary synchronization owner. Missing records and
other source statuses are excluded. Replays select no already-repaired rows.
The operation checks stored completion after each committed window.

`--create-backup=ABSOLUTE_PATH` creates a new custom-format PostgreSQL dump
using the existing connection configuration. It is separate from `--apply`;
credentials remain in the child environment and are never printed. Verify its
inventory with `pg_restore --list` before supplying its hash to apply.

Runtime auditor coverage: a stale provider vocabulary cannot be inferred from
canonical application state alone. The bounded source-to-persisted preview is
its explicit detection and recovery path. Adapter and existing-row replay
regressions cover normalization and preservation of identities/actual facts.
The normal import owner retains its serializable transaction and conflict
handling; this tool adds no competing writer or retry policy. Deploy the
adapter correction before relying on ordinary synchronization to preserve the
new interpretation for records in its recent refresh window.

Use `--historical-only` with reconciliation while an older application adapter
is still deployed. It limits writes to deliveries before the recent seven-day
refresh window or complete source order windows outside the configured import
lookback. An old scheduled worker therefore does not refresh these records.
Missing delivery dates inside the order lookback remain excluded. Publish the adapter fix and then run
the remaining reconciliation explicitly; do not silently classify deferred
records as repaired. `--verify-history` performs bounded read-only checks of
retired-truck status counts and the reported examples through the board owner,
with telemetry, financial enrichment and ETA disabled.
