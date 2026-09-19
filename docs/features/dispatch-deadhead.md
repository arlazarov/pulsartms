# Planned empty mileage

`DispatchDeadheads` stores one planned connection per destination dispatch,
with the previous dispatch ID, input signature, road miles, geometry and
calculation time. This is planned mileage, not actual mileage measured from
telemetry.

The same destination record's `RouteJson` already persists road travel
duration (`Seconds` and per-leg `Seconds`) as well as geometry. Consumers can
reuse this delivery-to-pickup travel time after validating the predecessor and
connection signature through `DeadheadConnection.ReadRoute`. It is driving
time, not an ETA: driver rest, appointment waiting and service time are
applied separately. It must not be treated as zero when timing is missing, or
reused for a replaced predecessor. Reading the saved duration requires no new
routing request or duplicate timing table.

Application selects the preceding non-cancelled load on the same truck using
scheduled pickup order. Tied or missing pickup order, missing delivery dates
and mixed-truck stops produce unavailable mileage rather than a guessed zero.
Overlapping delivery/pickup appointments do not invalidate an otherwise
unambiguous connection: the road distance still exists, and ETA separately
carries service, travel and appointment lateness forward. Schedule ordering
currently uses the imported local pickup dates and times; a missing delivery
time does not reverse that order or make the connection zero.

Inserting C between A and B replaces B's A-to-B signature with C-to-B. The
worker also prepares A-to-C. Cancellation, reassignment, endpoint edits and
vehicle routing-profile changes invalidate the affected connection. Dispatch
reads validate signatures before exposing saved miles, so stale values are not
shown while replacement calculations are pending. Historical lookup and
completed native-leg hydration share one database snapshot and return
immutable facts. The worker validates that history inside the transaction that
saves its result. The persistence reader accepts and returns immutable route
facts; saved fuel-history replay uses those values directly. DeadheadConnection
retains immutable current/predecessor work and endpoints, so caller edits cannot
change a captured geometry signature. Capture again to observe changed work.

Successful calculations do not expire merely with age. Price changes, page
refreshes and truck GPS updates do not request new routes. A failed unchanged
input uses the provider's retry time: permanent input failures require changed
inputs, transient routing failures wait five minutes, and the daily budget
waits until UTC midnight. The sanitized reason is stored with the destination
record. A five-minute claim covers interrupted calculations. Changed inputs
are eligible immediately. A persisted retry timestamp and optimistic
concurrency protect existing rows against simultaneous worker claims. The
unique destination index prevents duplicate inserts. Fuel horizons reuse
matching saved connection geometry.

The retry reservation and initial financial refresh share a protected
transaction. Provider/geocoding work runs after that transaction closes. Final
publication rereads historical inputs inside a second protected transaction
and writes road mileage/geometry with its financial values atomically. Changed
history rejects publication; failed writes or commit retain the prior
financial mileage and the committed retry reservation. Completed native
references and unknown-start guards remain part of historical capture. Its
content signature is separate from road geometry reuse and is not a stored
accounting revision.

The existing background batch prepares active assigned/in-transit loads. Each
base/deadhead operation has its own scope and timeout, so a deadhead failure
does not prevent base-route preparation. Completion time depends on the batch
backlog and provider response; opening a card performs no billable routing
calls.

- Loaded RPM = rate / loaded miles.
- Total RPM = rate / (loaded miles + planned empty miles).
- Unknown empty mileage leaves Total RPM unavailable; known zero is valid.

`DispatchRates` persists both RPM values to six decimal places, together with
the source price, currency, loaded/empty miles, connection signature and
calculation time. Active-load background preparation refreshes these snapshots
even when the road cache is unchanged; price changes do not request routing.
An unassigned load without a truck stores loaded RPM only. Consumers must
validate the snapshot with `DispatchRates.Matches` against current inputs and
the validated connection before using it. This is a latest-value snapshot, not
an immutable accounting history. Financial persistence accepts only explicit
DispatchRateInputs (load identity, price, loaded miles and currency), plus the
validated connection mileage/signature. It does not receive a route entity.

Apply `AddDispatchDeadheads` to the existing database before starting the
updated API. The migration adds only the deadhead table and its unique
destination index. It does not create a separate database or modify existing
load data. Deploy the matching Client DTO and load-card changes with the API.
