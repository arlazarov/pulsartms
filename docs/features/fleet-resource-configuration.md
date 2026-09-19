# Fleet resource configuration

One Admin-only Fleet navigation entry opens `/settings/fleet` (Trucks by default).
Trucks, Trailers and Drivers remain tabs inside the existing
`/settings/fleet/{kind}` pages. Fleet Map stays separate. Settings retains its shortcuts.
The Fleet tab bar contains only the three resource tabs; Settings is available
through main navigation, not repeated beside those tabs.
They use existing fleet identities and do not create replacement IDs.
Pay, settlement contracts and payment operations are not part of this feature.

## Ownership and editing

The lists are searchable, paginated in groups of 30 and loaded on demand.
Opening Edit loads one resource. Save and Cancel are explicit. Fuel-card values
are absent from list responses and available only in the Admin detail editor;
the Client clears retained edit values on cancel, save and disposal.

Supported edits are:

- Trucks and trailers: VIN and active/inactive state.
- Drivers: display name, fuel card and active/inactive state.

Imported unit numbers and provider/ELD identifiers are read-only. Renaming an
imported unit without alias-aware assignment reconciliation could break number
matching. Native resource creation and mapping reconciliation are not enabled.
Truck planning continues to use its existing profile and effective fleet
defaults; this section does not add a second planning formula or profile editor.

A saved configuration owns its supported fields as one local bundle. Import
updates a separate source snapshot but preserves that effective local bundle.
Return to import requires explicit confirmation and restores the latest source
snapshot. This also returns future field ownership to the importer.

Configuration revisions are optimistic concurrency tokens. Changed source facts
advance the revision; identical imports do not. Conflicting saves require a
reload. An import that read before a local save cannot commit over the newer
configuration. Display-name changes do not alter ELD identity: Torque driver
matching uses the retained imported name.

## Assignment boundaries

Configuration displays current fleet links separately from unfinished dispatch
references. Those references may include future work and do not imply switch
completion. Profile changes do not modify assignments, visits, fuel inventory,
driver clocks or execution history.

Making an active resource inactive is rejected while current fleet links,
unfinished dispatch references or active/planned execution legs exist. This
feature does not silently unassign a resource or delete referenced records.
Configuration writes use a serializable transaction. Native assignment
operations must also validate active resources under serializable isolation;
the configuration revision alone cannot protect independent assignment writes.

## API and persistence

Admin endpoints use `api/settings/fleet/{kind}`, where kind is trucks, trailers
or drivers. GET supports search and page. GET/PUT on the stable resource ID own
the detail/edit contract. Both endpoint policy and Application handlers require
an active Admin account. Resource fields are not recorded in audit logs; the
administrative audit records only identity, kind, revision and restore intent.

Additive fields on the existing resource tables retain imported configuration,
local ownership, configuration revision and editor provenance. Deploying the
feature requires the combined additive migration; this guide does not imply
that a migration has been applied. There is no automatic production backfill
or operational data edit from this page.

Selected verification categories are Fleet, Synchronization, Dispatch and
Architecture; persistence/shared contract changes require the full suite when
authorized. Regression sources cover local/import ownership, unchanged imports
and source-name matching. Tests and browser verification were not run during
implementation, as requested. Build success is not visual or database evidence.
