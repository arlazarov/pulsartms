# Base routes

`DispatchBaseRoutes` stores the full ordered stop-to-stop route independently of
`DispatchRoutePlans`, which owns live progress, rerouting and fuel planning.
Base route signatures include stop locations, order and routing dimensions, but
exclude GPS, elapsed time, fuel prices and truck identity. Reassignment with the
same routing profile does not invalidate the base route.

The background operation checks batches of ten dispatches once per minute, with
only in-transit, assigned and unassigned loads. Sent and cancelled loads are not
background-prepared. Unassigned loads use fleet-default
dimensions. Routing uses the existing provider cache and daily request budget.
Unchanged routes have no age-based expiry. Invalid stops are skipped and unexpected
failures are logged by the worker. A failed replacement keeps the saved route.

A matching input signature alone is insufficient: geometry must reach each
confirmed facility within 0.5 mile, with adjacent road ends within 0.05 mile.
The base-route service repairs only contiguous invalid leg ranges and checks the
replacement before saving. Valid legs keep their provider mileage and time.
Fuel horizons also validate current/future routes and connecting roads before
searching fuel alternatives. Base and deadhead JSON stores the coordinates in
the legs only; legacy aggregate-coordinate rows remain readable.
Saved base-route reads are untracked. After a write, the operation detaches only
an entity it owns; pre-existing caller-tracked entities and pending changes remain
under their original owner. Sequential future-load reads therefore do not retain
every saved route JSON in the fuel request's tracker.

TomTom failures persist a sanitized application message alongside the request
hash. Permanent route/geometry failures block that exact request until its inputs
change; temporary HTTP/network failures wait five minutes, and authentication
failures wait one hour. The daily budget returns a retry time at the next UTC
midnight without sending an HTTP request. Cached successful routes remain usable.
Changing validation rules may require explicit invalidation of permanent failure
entries. Old failure rows do not contain recoverable reasons.

Automatic live reroutes reserve a persisted per-truck attempt before calling the
provider. All attempts, including failures and cache hits, count conservatively:
15-minute minimum interval, at least three miles from the previous attempt,
three attempts per rolling hour and twelve per rolling 24 hours. PostgreSQL
advisory locking serializes reservations across processes. The default deviation
threshold is two miles sustained for three minutes. Pending stops behind the
truck no longer bypass the sustained-deviation interval. Initial pickup
connections also consume this budget. A blocked update retains the saved route.

Fuel horizons reuse a future load's persisted base route when all its stops remain.
Only the connecting leg is separately requested, using the shared provider cache.
The current-position baseline reuses the remaining saved legs only when the stop
suffix matches and GPS is within 0.05 miles of the leg. Distance and time are
proportioned along the saved geometry; this does not refresh traffic. Off-route
baselines and candidate fuel detours still require road checks.
Future address resolution uses the same street-level validation as base routes.

Apply `PersistRoutingFailures`, `AddRouteRecalculationBudget` and
`StoreDeadheadGeometry` before running
the updated API. Deployment does not automatically reset historical failed
requests or replenish the daily budget.

The provider streams successful HTTP bodies with a 16 MiB limit and validates a
200,000-point ceiling before materializing coordinates. HTTP failures do not read
the response body. The configured timeout also covers streaming body reads. Cache
text has a 32 MiB pre-expansion limit for compatibility with duplicated legacy
coordinates. New private cache keys separate legs-only rows from older binaries;
the current reader still reuses valid legacy successes and cooldowns. Each call
detaches only its own audit entity, preserving unrelated tracked work and the
committed reservation/budget semantics.

`GET api/dispatch/{id}/planning/base` reads saved geometry without telemetry or
routing requests. `Next loads` uses this endpoint for the selected truck's following
assigned loads. Future stop markers require saved geometry; imported city coordinates
are not used as temporary destinations.

Migration `AddDispatchBaseRoutes` was applied to the shared database on September 7, 2026.
Street addresses take precedence over imported coordinates, which may be city
centroids. Coordinate-only stops remain supported. The location policy participates
in route signatures so older address-based routes are invalidated.

Google Geocoding resolves street addresses through `IAddressGeocoder`; TomTom remains
the truck-routing provider. The Google lookup uses the server-side `GooglePlaces:ApiKey`
and requires Geocoding API access. It rejects partial matches, city centers,
conflicting street numbers, mismatched cities/states and ambiguous results. Postal
code corrections are allowed when the street, city and state agree. Successful
lookups are cached in memory for twelve hours; provider payloads and keys are not logged.
The lookup identifies an address, not a verified truck entrance. It does not choose
business-search results or visitor entrances. Do not substitute city coordinates
when address resolution fails.

Address lookups omit `Attn` recipient instructions without changing the stored stop.
State-highway aliases such as `State Highway 5 S` and `NY-5S` are equivalent only
within the matching state, preserving the house number, highway number and suffix.

For a single-leg route, one terminal `other` travel-mode connector of at most ten
meters may be removed when it immediately follows a truck section. The stop keeps
its resolved location; road geometry and mileage end at the truck-accessible point.
Travel time remains conservative. Interior or longer unsupported sections and all
explicit truck restrictions still reject the route.
