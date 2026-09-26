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
message operation. It preserves source status and actual facts; it does not
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
