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

Trailers are catalogued automatically from the telemetry provider and from
imported loads; see [synchronization](synchronization.md#trailers-catalog-and-current-assignment).
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

## Driver contacts

A driver has a phone, an email and a WhatsApp number. Unlike the bundle
above, contacts are owned field by field and have their own revision:

- Phone and email follow the telematics source (Samsara `phone` and `email`)
  until a dispatcher sets or clears them. A later import refreshes only the
  source copy. A cleared field stays empty through imports. "Use source
  value" returns that one field to the source.
- The WhatsApp number has no source and is never derived from the phone.
  "Use the phone number" copies it only when a dispatcher presses it; the
  copy is checked and saved like any other entry.
- Phones are stored in E.164. The server completes a number only when its
  country is certain: ten digits, or eleven starting with 1, that fit the
  North American plan. Anything else needs its "+" and country code. A source
  phone that is not a complete number is shown as written and marked not
  usable; nothing is sent to it. An email must be one plain address.

`GET/PUT api/drivers/{id}/contact` use the Dispatch policy, and the handlers
again require an active Admin or Dispatch account. The company query filter
makes another carrier's driver not found. A stale revision is refused and
the editor keeps its draft. The administrative audit records the driver,
revision and which fields follow the source, never the values. The shared
editor appears in the Admin driver editor. Migration
`20260923122137_AddDriverContacts` adds the columns and is not applied by
this change.
