# Truck weather

The selected Fleet Map truck reads Google Weather current conditions through
`GET /api/fleet/trucks/{truckId}/weather`. The authenticated controller delegates
to Application. Application uses the cached fleet position; it does not trigger
GPS, HOS or routing requests. Infrastructure owns the Google transport.

Set `GoogleWeather:ApiKey` in local user secrets or `GoogleWeather__ApiKey` in
server deployment secrets. Restrict the key to `weather.googleapis.com`; never
include it in browser configuration. The Settings integration editor does not
manage this key. Enable Weather API separately in the Google Cloud project.
Do not log outgoing weather URLs: they contain truck coordinates.

The bounded server read cache coalesces readings by truck for ten minutes within
one API instance, including while the truck moves. This is not a distributed
cache or an account-wide billing cap. Each replica can make its own request.
Only selected trucks request weather; closing or changing selection cancels the
browser read and its refresh delay. A cancelled viewer does not cancel
another viewer's shared provider read. Provider reads have an eight-second bound.

Weather represents the truck position at the last lookup, not a vehicle-mounted
sensor. Missing/stale GPS, absent credentials, failed reads and historical dates
show a dash. Readings more than one hour old are not shown. Celsius is the default;
the user's explicit Fahrenheit preference is preserved without refetching.
Condition icons distinguish clear day/night, cloud/fog, rain/storm and snow/sleet.
Cold/unavailable readings retry after fifteen seconds for at most five attempts,
then once a minute. A successful reading restores the ten-minute cadence. This
avoids leaving a cold GPS placeholder for ten minutes; the server still coalesces
paid provider reads. The required source attribution is visible in the map key,
outside the truck card, alongside other map explanations.

Weather reuses Fleet Map's visibility subscription. Hidden tabs do not start new
weather reads or wake a periodic retry timer. Becoming visible starts an overdue
read immediately, but retains a fresh reading's original deadline. The page's
lifetime token cancels weather work before its visibility observer is disposed.
This does not cancel another viewer's shared server lookup or change its cache
duration. An already-started bounded HTTP read may finish while the tab is hidden.

Provider references:

- [Current conditions](https://developers.google.com/maps/documentation/weather/current-conditions)
- [Attribution](https://developers.google.com/maps/documentation/weather/policies)
- [Service terms](https://cloud.google.com/maps-platform/terms/maps-service-terms)
- [Billing](https://developers.google.com/maps/billing-and-pricing/pricing)
