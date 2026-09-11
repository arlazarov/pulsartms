# Load number display

Settings has an independent **Load numbers** card. Admins can save a fleet-wide
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
accepts `{ loadNumberPrefix, revision }`; both use the standard response wrapper
with `{ loadNumberPrefix, revision, updatedAt }`. Optimistic concurrency rejects
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
