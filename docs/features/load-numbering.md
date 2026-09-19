# Load number display

Settings has an independent **Load numbering** card. Admins can save a fleet-wide
prefix of at most 16 characters. Surrounding whitespace is trimmed; empty is a
valid choice and does not become `AMF` or `#`. Control characters and null write
values are rejected. The initial default is `AMF` only when no settings row exists.

The prefix is presentation only. Current and future Dispatch Cards, Table, Papers,
load details, route summaries and current/future map references use the same
formatter. Dispatch identifiers, imported integer load numbers, order numbers,
URLs, search and copied numbers are unchanged. The number is formatted without
locale-dependent grouping; the Client never invents a prefix while settings are
unavailable.

`GET /api/settings/dispatch` is available to authenticated users. Admin-only `PUT`
accepts `{ loadNumberPrefix, revision, temperatureUnit, distanceUnit }`; both use the standard response wrapper
with `{ loadNumberPrefix, revision, updatedAt, temperatureUnit, distanceUnit }`. Optimistic concurrency rejects
stale or competing saves with HTTP 409. The UI retains the draft on failure and
requires an explicit reload to adopt another session's changes.

Application Dispatch handlers own this setting through `IAppDbContext`.
Infrastructure maps the singleton `DispatchSettings` entity to a dedicated table;
migration `20260908223826_AddDispatchSettings` creates only that table. These values
are not fuel planning preferences and do not participate in route, ETA, fuel or
financial signatures. Saving the prefix does not enqueue or invalidate calculations.

The authenticated layout reads the small setting once and cascades it to labels,
without one request per card or marker. The Settings form reads its own latest
revision before editing. Successful saves update the current layout immediately;
late reads cannot overwrite a newer revision. Other open browser sessions read a
new value when their layout is recreated or the page is reloaded. Map updates send
only the formatted load reference, preserving route geometry and truck selection.

Temperature accepts `fahrenheit`, `celsius` or `both`; distance accepts `miles`,
`kilometers` or `both`. These now belong to each account under Personal settings
in the account menu beside Logout. The authenticated appearance endpoint stores
them on Users; omitted unit fields preserve the caller's saved choices. New
accounts default to Both. `20260913142839_AddUserDisplayUnits` seeds existing
accounts from the former shared preferences without changing route/fuel data.
The old DispatchSettings columns and API fields remain for compatibility, but
company forms no longer edit units and the Client does not use those fields.

The Client converts normalized Celsius and saved miles only while formatting.
Fleet inspectors, Dispatch distances, load details, route choices and station/stop
distance labels share those preferences. Dual Remaining stays in three rows:
label, miles, kilometers. Single-unit mode omits the secondary row. Outside shows
Fahrenheit first when both are selected. Missing sensor values remain unavailable.
Speed stays in mph; MPG, rate per mile and provider quote units keep their labeled
bases. No financial formula or planning input is converted. The JavaScript map owns
its formatter per mount; changing units refreshes open stop/fuel text without
rebuilding roads, changing selection, fetching providers or recalculating purchases.
