# Transfer confirmation without exact event times

The operator confirmed a completed trailer exchange near 07:00 Eastern on
September 14. The approximate time is not stored as an exact event:

- AMF1377: 11005 to 54777, trailer 9P1175.
- AMF1383: 54777 to 11005, trailer 44120.

Imported mixed-truck itineraries still had no native execution legs. The legacy
whole-load route therefore rejected the absence of one unique truck. Native
confirmation separates completed outgoing work from the active incoming route.
It does not rewrite imported trailer discrepancies or original source actuals.

## Implementation and release

Release/receipt actor fields now establish confirmation independently of optional
exact timestamps. Completed native visits project an explicit completion flag;
unknown actual times do not become mileage/odometer evidence. Trailer custody
uses receipt confirmation for its unique open interval constraint.

The Switch editor accepts an explicit completed-import reconciliation, and normal
independent Drop/Hook actions accept blank actual date/time fields. Uncertain
requests retain idempotency and revision guards. Existing native histories are
not eligible for completed-import bootstrap.

Migration `20260914123323_OptionalTransferEventTimes` was applied. The reviewed
SQL permits nullable custody release time and changes the open-custody index to
`ReceivedBy IS NULL`. Downgrade cannot invent missing timestamps and will fail
until such rows are explicitly reconciled. A private scoped custody/history
backup was retained alongside the earlier full pre-execution backups.

API Cloud Build: `4d67d9f9-ffb6-4928-91ec-609dba74fc40`.
Image:
`sha256:0e3e8f53b4c4b7d4c569090c2b53504f4154d7ce93f3adae0e5ca0de56f54e2f`.
Cloud Run: `amftms-api-00114-j98`, 100 percent traffic.
Client: `artifacts/managed/transfer-20260914/publish/wwwroot`, Firebase released.

Client and API builds completed. Regression sources were added but no test suite,
release gate or broad browser checks were run, as requested. No performance
improvement is claimed. Incident confirmation is performed through the operator's
authenticated UI, not a fabricated account token or direct assignment SQL.

## Incident result

Switch `f6e90ace-60ed-42cc-b520-2838b987fd83` was saved through the operator's
authenticated localhost UI against the shared production database. Both Drop and
Hook confirmations show completed, with no invented exact times. Four native legs
retain the two outgoing histories and the two active incoming assignments.

The production store contains native route version 1 for each incoming leg,
owned by the confirmed truck, ending at its actual final delivery and with
`InputsChanged=false`. Fleet Map displayed AMF1377 / 9P1175 on 54777, with
Target DC #3802 as destination and about 662 miles remaining. The 11005 inspector
displayed trailer 44120 and the Costco Port St. Lucie destination, about 701 miles
remaining. Distances are observation-time readings, not fixed expectations.

A transient local Neon DNS resolution failure interrupted supplemental reads
after the save. Both database hostnames subsequently resolved; no ERROR entries
were returned for the production revision in the focused ten-minute log read.
This is limited incident evidence, not a full production or browser test pass.
After reloading, 11005 also displayed AMF1383, its order, current remaining
distance (637 miles), ETA and cycle forecast without the connection warning.
The saved incoming routes contain 10,925 and 8,635 geometry points respectively.

The old AMFTMS workspace path was restored as a symbolic link to pulsartms,
allowing the existing in-app browser tooling to start after the project rename.
