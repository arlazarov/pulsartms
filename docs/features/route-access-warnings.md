# Route access warnings

TomTom section warnings do not block mileage or rate-per-mile persistence.
Application's `IRouteSectionValidator` implementation retains provider distances,
durations and geometry unchanged, including terminal connectors. It records truck
restriction and unconfirmed-access warnings in `TruckRoute.Warnings`.

Current-route map details and Dispatch planning display these warnings. Accepting
a distance for financial planning does not certify legal truck access. Invalid
coordinates, incomplete route legs, provider errors and request budgets retain
their existing checks.

Warnings are serialized with the route in the provider cache, saved base routes
and deadhead geometry. Existing server-side financial formulas use the accepted
mileage; no client-side financial formula or schema migration is required.

When deploying this policy, expire only cached section-rejection failures and
their deadhead retry timestamps. Keep successful saved routes, audit rows and
daily request counts. Normal background preparation performs the recalculation.

Regression tests verify unchanged mileage/geometry, warning persistence and cache
reuse without another paid request, including explicit truck-restriction sections.
