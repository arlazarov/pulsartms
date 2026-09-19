# Verified stop addresses

Application's `StopAddressService` owns correction persistence. Infrastructure's
`IAddressGeocoder` implementation returns structured address components and a
precise point; it does not write dispatch data. API and Client require no new
endpoint or financial/address business logic.

Accepted execution uses `ExecutionStopAddressService` before native base-road
preparation. Both owners reuse `StopAddressResolution`; provider calls happen
outside the acceptance transaction. The native owner compares the captured leg
revision, applies the verified address through `ExecutionAcceptance`, records an
immutable revision and queues planning together. A late result cannot overwrite
a changed assignment. Recorded mileage prevents automatic location replacement;
explicit transfer sites retain their selected coordinates.

The original imported address remains separate from its verified location.
Replaying that original source preserves the accepted verified address and may
still supply valid appointment/actual facts. A different source address remains
under review. Source stop rows are not rewritten by native address verification.

`DispatchStops.SourceAddressJson` preserves the imported address components.
The normal address columns contain the verified components and therefore flow
through existing dispatch projections and map planning responses. Verification
replaces `Latitude`/`Longitude` with the verified point. The source JSON preserves
address components, not a separate original coordinate pair. Unchanged imports
must preserve both the verified text and its coordinates, even when the provider
continues sending a city-level point.

Synchronization compares the incoming source components with the saved source.
Unchanged input preserves the verified address. Changed input restores the new
imported components, clears verification/retry state, and changes routing input
signatures. Source JSON is an EF concurrency token: an in-flight correction cannot
overwrite a concurrently changed import.

Background preparation verifies eligible load stops before base/deadhead work.
Successful stops do not repeat verification each minute. Expected failures keep
the original address and a bounded retry time. Expiration restores original
components after 29 days, before the 30-day retention boundary, and clears the
verification state. The existing background worker must remain enabled for
expiration. No separate database is used.

The retry timestamp participates in the preparation fingerprint. An explicitly
cleared or changed retry is observed by the next repair scan instead of waiting
for the old in-memory deadline. Unchanged failed inputs still honor their retry
time; normal polling does not start repeated provider requests.

Google Geocoding must return a unique matching street-level candidate. A rejected
single candidate may be checked using Address Validation. Acceptance requires
confirmed house, street, city and region, an ACCEPT verdict and premise-level
validation. A warehouse-unit ambiguity cannot authorize a city-center point.
Text components come from the matching geocoding candidate, not a potentially
reordered formatted warehouse-unit string from Address Validation.

Street abbreviation matching includes Way/Wy without changing the house number.
State/province matching uses explicit locality tokens, never street abbreviations
or country aliases, in both geocoding and Address Validation.
US nine-digit ZIP codes are formatted as ZIP+4 before lookup and cache identity;
this changes only locality components, never the street/building number. Postal
matching still requires the same five-digit ZIP and does not waive street-level
precision or Address Validation confirmation requirements.

Provider storage and attribution requirements must be reviewed when changing
providers, retention or display surfaces:
https://cloud.google.com/maps-platform/terms/maps-service-terms

Regression checks cover persistence, existing dispatch projections, unchanged
imports, changed imports, expiration, failure retries and strict Google matching.
This feature does not impose a global Google geocoding request budget or certify
the correctness of every imported address.
