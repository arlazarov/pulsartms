# Shipment and Border preparation: September 18, 2026

Implemented locally in the existing working copy. This record does not authorize
or describe a production release. See the [current guide][guide] for behavior.

## Scope

Ordinary load-linked shipments own parties, pickup/delivery references, BOL and
commodity lines. Independent Border drafts select shipment revisions from one
or several loads and own PARS/PAPS, crossing details and crew/equipment snapshots.
Domestic shipment editing has no customs requirements. Selecting different
brokers' loads for Border does not merge their commercial or billing records.

Application commands enforce permissions, revisions, shape bounds and
actor-bound retry receipts. Infrastructure provides encryption and relational
storage. Source edits do not silently replace saved crossing snapshots.

## Verification

- Full suite: 2,747 Server, 1,023 Client and 561 JavaScript tests passed
  (4,331 total), including architecture checks.
- Strict solution build, including Client: zero warnings and zero errors.
- Dedicated PostgreSQL fixture: migrations from an empty schema, shipment and
  crossing saves, fresh-context reads, protected person data, idempotent retry
  and stale-revision conflict passed. The fixture schema was cleaned afterward.
- Migration `20260918120545_AddShipmentAndBorderDrafts` applied only to the
  isolated development database through the guarded probe.
- Browser: a synthetic crossing saved with Canada and Windsor Ambassador Bridge,
  survived a full reload and reopened with the selected port and reference.
  Crossing-to-Shipments tab navigation retained the draft before save.
  A synthetic ordinary shipment was saved without customs fields, found from
  Border and selected with its own PARS reference.

The PostgreSQL fixture used its dedicated database and test role. No local SQL
container was started. Production migration and deployment were not performed.
Production performance and a complete responsive visual matrix were not measured.

## Limitations

This is preparation, not external filing. No BorderConnect request, customs
acceptance or release is implemented. Basic missing-field checks do not establish
ACE/ACI readiness. Special procedures and dangerous-goods rules, reusable carrier
and fleet customs profiles, dedicated customs staff permissions, and retention
management remain future work. Only synthetic identity data was used in checks.
The US port catalog also includes non-highway ports; eligibility needs a future
validated adapter catalog. Existing operational Pieces/Pallets are unchanged.

[guide]: ../../features/border-preparation.md
