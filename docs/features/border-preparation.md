# Shipments and Border preparation

This is a local draft workflow. It does not connect to BorderConnect, transmit a
manifest, obtain customs acceptance or record an actual border crossing.

## Ordinary shipments

Open **Shipments** from an existing load. A shipment contains its bill of lading,
pickup/delivery links, shipper and consignee, and multiple commodity lines.
Quantity and package type are separate from weight and weight unit. Unknown
packaging stays unknown; existing stop Pieces and Pallets are not reinterpreted.
Explicit copy actions copy pickup/delivery addresses into editable party values.
Later changes to a stop do not overwrite those parties.

**Find address** suggests matches after three characters and a short typing
pause. Matching saved stop and shipment addresses appear first, followed by
Google Maps predictions for the US and Canada. Admins may also reuse saved Border
party addresses; those addresses are excluded for ordinary Dispatch users.
Choosing a suggestion fills street, city, region, country and postal code,
clears the old unit line, and preserves the legal name and contact fields.
Review the new unit/address details before saving. Search and selection do not
save the form. Manual entry remains available if Google is unavailable.

Several shipments may use the same pickup on a load. Different freight brokers'
orders retain separate loads and their own billing, rates and documents. Choosing
them together in Border does not merge their commercial records or implement a
new shared physical-pickup operation.

The ordinary shipment editor contains no PARS/PAPS fields, carrier customs codes
or border requirements. Domestic carriers can use Shipments without Border.
Drafts may be incomplete. Save is explicit, revision checked and retry safe;
leaving the page requires saving or discarding its draft.

## Independent crossing drafts

**Border** is a separate navigation entry at `/border`. A crossing may select
shipments from several loads. Search uses load number, BOL or customer. Each
selected shipment retains its own version, PARS/PAPS references, procedure,
importer, customs broker and loaded equipment. **Use latest shipment version** is
an explicit replacement of its source snapshot; source edits do not do this.

Crossing fields include destination, port, planned arrival with time zone,
carrier name/address and separate US SCAC and Canadian carrier code. Country
selection filters the port catalog and clears the previous country's port.
The checked-in catalog contains government port identifiers obtained from the
public [US][us-ports] and [Canadian highway][ca-ports] reference datasets on
September 18, 2026. The US list also contains non-highway ports; catalog membership
alone does not establish that a port is suitable for the intended movement.
Availability, hours and provider-specific filing eligibility are not checked.

Crew and equipment belong to this crossing. Select existing fleet identities or
explicitly copy an accepted assignment from one of the selected loads. Accepted
stop driver overrides take precedence over leg defaults. A pickup driver who
leaves the trailer at a yard need not be the driver selected for the crossing.
Later execution changes produce a review issue; they never silently relabel the
saved crossing. Manual resource selection clears the execution-source reference.

Crew identity and travel-document fields are crossing snapshots. Driver names
are not split into legal names. Passengers are separate from fleet driver roles.
Equipment captures fleet identity, unit/VIN, registration jurisdiction, equipment
type, containers and seals. Several trailers can be recorded and shipments can
choose their loaded equipment.

Carrier, legal-person and registration values currently belong to each crossing.
Reusable company/fleet customs defaults are a future extension, not a second
fleet identity or functionality implied by these forms.

## Access, persistence and checks

Ordinary shipments require the existing Dispatch permission. Border currently
requires Admin at both HTTP and Application boundaries because it contains
identity-document data. No existing Dispatch or fleet permission is expanded.
Crew details and complete save receipts are protected using the application's
persistent Data Protection keys with a crossing-specific purpose. They are not
included in ordinary Dispatch, fleet lists or workspace audit snapshots.

Shipments, commodities, crossings, crossing-shipment links, crew and equipment
use dedicated tables. Shipment snapshots and protected personal details are
bounded value payloads inside their owning rows. Saves check opened revisions,
use serializable transactions and record actor-bound idempotency receipts.
A failed or uncertain save preserves the browser draft. An uncertain retry
retains the exact original request. No browser storage is used for these drafts.

**Check missing fields** reports basic preparation gaps and changed source
versions. It is not an ACE/ACI readiness certificate. Supported draft procedures
are PARS and PAPS; special procedures, dangerous-goods rules, electronic submission,
amendments, provider callbacks and customs acceptance remain unimplemented.
There is no Submit action or implicit provider call.

Crossing drafts and their receipts are retained; there is no deletion/retention
UI yet. Only synthetic personal data has been used in local verification. The
release design must establish the operational retention policy and dedicated
customs staff permissions before collecting real identity documents at scale.

Migration `20260918120545_AddShipmentAndBorderDrafts` is additive. It was applied
to the isolated development database and verified on the separate PostgreSQL
fixture. It has not been applied to production, and no deployment is implied.
See [the implementation record][record] for exact verification and limitations.

[us-ports]: https://borderconnect.com/data/us/ace/us-port-codes.json
[ca-ports]: https://borderconnect.com/data/ca/aci/highway-ports.json
[record]: ../archive/2026-09/shipment-border-preparation-2026-09-18.md
