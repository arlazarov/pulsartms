# Customs data preparation

Status: ordinary shipments and independent crossing drafts are implemented
locally. See [current behavior](../features/border-preparation.md) for the
implemented boundaries and limitations. Reusable fleet/carrier profiles,
special procedures and external filing below remain future design.
BorderConnect is the intended first adapter; no external submission or deployment
is authorized by this specification.

## Ownership

Retain the modular monolith and the boundaries in
[architecture](../ARCHITECTURE.md).
Shipments owns ordinary cargo. Customs owns border declarations, crossings
and filing evidence. Execution remains
the owner of accepted assignments and custody. The existing Trip is an optional
operational grouping; it must not become a second assignment or customs owner.

| Record | Responsibility |
| --- | --- |
| Load | Commercial obligation, appointments and operational references |
| Shipment | Load-linked cargo, parties and commodity lines |
| Border shipment | Customs references and selected shipment revision |
| Shipment commodity | Description, external package quantity and weight |
| Border crossing | Direction, port, expected arrival and selected resources |
| Crossing shipment | Exact shipment revision carried through that crossing |
| Fleet customs profile | Reusable driver or equipment identification |
| Carrier customs profile | Legal identity and carrier identification codes |
| Filing revision | Immutable reviewed snapshot of one proposed submission |
| Integration receipt | Provider correlation and processing evidence |

A load can contain several shipments. A crossing can carry shipments from
several loads. A shipment can participate in more than one crossing, including a return
journey. Give these records stable IDs and explicit relationships; do not use
load numbers, stop positions, trailer numbers or provider IDs as primary keys.
Shipment creation may be linked to a load, but a crossing is an independent
aggregate. Do not put its drivers, equipment or filing state in load metadata.

## Data inventory

The following is the native data inventory. Provider-required fields and allowed
values vary by jurisdiction and shipment procedure; a blank draft remains valid
to save. A saved draft is not a statement of customs readiness.

### Carrier

Store legal name, structured business address, contact details, US SCAC and
Canadian carrier code as distinct fields. Preserve leading zeroes in all codes.
Carrier identity belongs to the organization, not the logged-in user's profile.
Keep BorderConnect credentials and account identifiers in the existing
integration boundary, never in carrier records or browser responses.

### Parties and locations

Use structured party snapshots with name, address lines, city, state/province,
country and postal code. Support shipper and consignee independently, plus
importer and customs broker where applicable. Broker contact/reference details
must not reuse the load's freight broker or billing terms automatically.

A pickup facility is not necessarily the legal shipper, and a delivery facility
is not necessarily the consignee. Provide explicit copy-from-stop actions that
create editable snapshots. Subsequent stop or directory edits must not silently
change those snapshots. Retain optional directory IDs as provenance only.

### Shipments

Ordinary shipments store load linkage, bill of lading, parties and commodities.
Border shipment selections separately store procedure, customs control numbers,
loading city/region/country, release office and importer/customs broker details.
PARS and PAPS are distinct jurisdiction-specific references; neither replaces
order number, PU/DEL reference nor appointment reference.

Support multiple commodity lines and an explicit consolidated-freight flag.
Reference-only shipments must retain their external reference and cannot be
silently treated as fully described shipments. Empty conveyance is a crossing
condition, not a shipment with zero packages.

Conditional procedures need typed extensions and their own validation before
being offered as supported workflows: in-bond/transit, bonded warehouse,
hazardous goods, personal effects, instruments of international traffic and
other release exceptions. Do not use an unrestricted JSON field as a substitute
for those rules, or silently export unsupported procedures as PARS/PAPS.

### Commodities

Store description, positive integer package quantity, package type, weight and
weight unit separately. Use a native package identifier with explicit ACE and
ACI mappings; the provider's package code is not the domain identity. A package
catalog must expose only verified mappings for the selected destination.
Store optional marks/numbers, classification, origin and dangerous-goods details
where applicable. Their applicability must follow the selected procedure.

Quantity describes the lowest external packaging. For example, 100 articles
inside five boxes on one pallet are five boxes. Pallet count remains a separate
operational handling field. Bulk cargo follows the applicable packaging rule;
do not infer its declaration from the weight.

The existing decimal Pieces value cannot be relabeled as an integer package
quantity without review. Do not default unknown imported packaging to Pieces,
Boxes or Pallets. Preserve existing operational cargo values, and let an
explicit copy action create a customs draft with unresolved packaging highlighted.

Weight uses an explicit unit and decimal precision. Provider conversions and
rounding occur server-side in the adapter with tests. ACE and ACI use different
weight codes; shared UI labels must not expose those wire-level differences.
Temperature and pallet count remain operational facts, not substitutes for
customs commodity fields.

### Drivers and passengers

Extend the existing Driver identity through a separate customs profile with
structured legal names, birth date, citizenship, applicable sex/gender code,
FAST identification and typed travel documents. A document includes type,
number, issuing jurisdiction and applicable validity dates. Support multiple
documents rather than one overloaded passport/license string.

Do not split the existing display name to invent legal first/last names.
Passengers are separate people, not co-drivers. Crossing crew roles explicitly
distinguish driver and co-driver. Do not create duplicate fleet Driver rows for
manifest use.

Sensitive profile fields need dedicated authorization and controlled reads.
They must not enter Dispatch board payloads, general workspace revision JSON,
browser storage, logs or diagnostics. Retain filing evidence under the same
restricted access. Establish retention and deletion behavior before collecting
real document data.

### Trucks, trailers and equipment

Reference existing fleet identities and extend them with registration plates,
issuing country/region and equipment type. Keep unit number and VIN distinct.
Support the applicable container, seal and conveyance identifiers separately.
A crossing may have several trailers; do not reduce it to the single current
trailer field on a truck. Commodity/equipment relationships must identify the
actual carried goods rather than assuming every shipment uses the first trailer.

### Crossings

Store destination jurisdiction, port of entry, expected arrival with an explicit
time zone, carrier, crossing reference and shipment links. Keep planned arrival
separate from actual crossing evidence. Do not infer actual crossing from GPS
or a completed pickup. An ETA recalculation may propose a new planned arrival;
it cannot rewrite a reviewed filing.

Select the accepted execution occurrence relevant to the crossing and record
its leg/stop identities and revisions as provenance. Capture driver, co-driver,
truck and trailers for that crossing. Resource changes elsewhere in the route
cannot rewrite it. If a relevant accepted revision changes, flag the draft for
review instead of silently substituting the truck's current driver.

## Application and persistence boundaries

Use a Customs feature with dedicated commands, queries, validation and typed
storage. Do not expand DispatchWorkspaceMetadata into a manifest container.
Keep load editing and customs preparation independently saveable. Existing
workspace optimistic revision and actor-bound retry behavior are precedents,
not permission to reuse its unrestricted payloads for identity documents.

Fleet profiles reuse existing resource IDs. Shipments link to loads; crossing
shipment rows link explicit shipment revisions to a crossing. Equipment and
crew snapshots retain stable source identities alongside the reviewed values.
Use database foreign keys, unique receipt constraints and concurrency tokens.
Serialize only immutable evidence or bounded value objects, not the mutable
relationship graph in one opaque JSON column.

Draft save checks shape, bounds, identities and permissions. A separate server
readiness assessment checks jurisdiction, procedure, catalogs and completeness
and returns field-addressable issues. Incomplete drafts can be saved;
unsupported procedures can never be reported ready. Readiness is not customs acceptance.

Application defines the filing adapter interface. Infrastructure owns
BorderConnect wire contracts, code mappings, credentials, HTTP and response
translation. Native domain and Client contracts must not contain companyKey,
sendId, autoSend or raw provider messages. Disabling the adapter leaves editing,
validation and native history available.

Future submission requires an explicit action against a reviewed immutable
revision. Use durable outbox delivery, stable correlation, deduplicated receipts
and monotonic processing of out-of-order responses. Separate provider import
success, customs transmission, acceptance, release and physical crossing.
A timeout is an unknown outcome, not permission to create another filing.
Corrections create amendments; they do not replace prior evidence.

## UI

Keep Location, Appointment and References ahead of operational Cargo and Contact
in the existing stop editor. The load Shipments section contains ordinary cargo;
the independent Border editor selects shipments from one or several loads. Show
procedure-specific fields only when applicable. Use existing controls and tokens.

Cargo uses Quantity plus Package type and Weight plus Unit. Package selection
is searchable and jurisdiction-aware. Never force customs completeness merely
to save an operational load or appointment.

Fleet customs profiles belong alongside the existing resource configuration,
with restricted identity-document editing. Carrier codes belong in company
configuration. Preserve all drafts across tabs; no autosave on selection, source
refresh, map interaction or resource lookup. Readiness links to the exact field
needing attention rather than presenting a generic missing-data message.

## Extension sequence and acceptance

1. Native shipment/commodity/party storage is implemented separately from Border.
   Existing cargo values remain unchanged.
2. Add carrier and fleet customs profiles, permissions and structured catalogs.
   Verify sensitive fields cannot leak through general fleet or Dispatch APIs.
3. Independent crossings, crew/equipment snapshots and multi-load shipment
   links are implemented. Continue extending coverage of crossing scenarios.
4. Extend basic draft checks to verified ACE/ACI procedure-specific readiness.
   Unsupported procedures must remain visibly unsupported.
5. Add the optional BorderConnect adapter only when integration is requested,
   followed by sandbox contract checks and a separately approved release.

Each slice needs its own migration and reviewable local result. Do not deploy
or modify production for this work. Test PostgreSQL only against the existing
isolated fixture, never the application database or a local SQL container.
Run the full suite for contracts, persistence and authorization changes; build
Client and inspect the actual editor after UI changes.

Required regression scenarios include partial draft save/reload, stale revision,
uncertain retry, two shipments on one load, several loads on one crossing,
two crossings for one shipment, multi-trailer cargo, empty conveyance,
independent shipper/PU addresses, independent appointment/customs references,
missing packaging, changed crew before review, unchanged submitted snapshots,
restricted document reads, disabled integration and duplicate provider callbacks.

## Source references

Verify the current official schema and catalogs when implementing each adapter
mapping. The API examples demonstrate different ACE/ACI weight codes, separate
trip and shipment data, multiple commodities and crew/equipment information.

- [BorderConnect developer API][api]
- [ACE message reference][ace]
- [ACI message reference][aci]
- [Driver profile guide][drivers]

[api]: https://www.borderconnect.com/emanifest-api/index.htm
[ace]: https://borderconnect.com/emanifest-api/manual/ace-emanifest-json-reference.pdf
[aci]: https://borderconnect.com/emanifest-api/manual/aci-emanifest-json-reference.pdf
[drivers]: https://wiki.borderconnect.com/index.php/Creating_and_Maintaining_Driver_Profiles_%28ACE_and_ACI_eManifest%29
