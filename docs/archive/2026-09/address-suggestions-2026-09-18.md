# Address suggestions: September 18, 2026

Local implementation adds one shared address combobox to Dispatch stops and
Shipment/Border party editors. It waits 300 ms after three characters, cancels
superseded reads and requires explicit selection. Arrow keys, Enter and Escape
support keyboard selection. Choosing an address never saves the owning form.

Application queries existing stop and shipment-party addresses with bounded
projections and case-insensitive token matching. Matching saved addresses precede
provider predictions and duplicates among saved addresses are collapsed. Admin
queries additionally include Border parties; ordinary Dispatch queries exclude
those restricted records. No separate history table or migration was introduced.
History reflects saved records, not every partial string typed into a form.

Infrastructure owns Google Autocomplete (New) and Place Details requests through
an Application interface. Predictions are restricted to US/Canada, use a session
token, have a bounded timeout and are not cached or logged. The key stays on the
server. Missing provider results preserve manual editing and saved suggestions.
Google Maps and previously used results have separate labels. Dispatch selections
still run existing coordinate verification before changing the operational stop.

## Checks

- Full suite: 2,751 Server, 1,024 Client and 561 JavaScript tests passed
  (4,336 total), including architecture checks.
- Strict solution/Client build: zero warnings and zero errors.
- Styles rebuilt successfully.
- Browser: typing `100 Demo` returned the saved demo pickup first, followed
  by live Google predictions. Selecting a Google address filled street, city,
  region, country and postal code without saving the draft. Verification edits
  were discarded. Desktop layout was inspected; no full responsive matrix.
- Regression checks cover saved-first ordering, duplicate saved addresses,
  restricted Border history, provider failure, provider session/field mapping,
  debounce, late replies, keyboard selection and party identity preservation.

The automated relational checks use SQLite; a separate PostgreSQL fixture run
was not performed for this change. No production deployment or migration was
performed. Production search latency and large-history performance are unmeasured.

## Provider references

- [Autocomplete request contract][autocomplete]
- [Place Details][details]
- [Attribution and address-selection policy][policy]

[autocomplete]: https://developers.google.com/maps/documentation/places/web-service/reference/rest/v1/places/autocomplete
[details]: https://developers.google.com/maps/documentation/places/web-service/place-details
[policy]: https://developers.google.com/maps/documentation/places/web-service/policies

## Dropdown follow-up

Suggestions now overlay the form instead of participating in document flow.
Loading and messages use the same bounded overlay. Available ZIP/postal codes
appear on each result; Google Maps attribution appears once in the footer.
Google predictions do not include postal components, so up to five parallel
Place Details reads enrich the displayed predictions. Preview reads omit the
session token so they do not terminate the selection session; they incur
separate provider requests. A failed preview leaves the suggestion selectable
without an invented postal code. No preview result is persisted or cached.

Follow-up verification: all 4,337 tests passed (2,751 Server, 1,025 Client,
561 JavaScript). Strict solution and final Client builds passed without warnings.
One earlier full run timed out in the unrelated Dispatch batch-refresh component
test while a build ran concurrently; the complete repeat passed. Browser checks
confirmed live ZIP/ZIP+4 results and identical 52 px spacing from the search input
to the underlying address row with the dropdown open and closed. Absolute page
coordinates were not used because input focus scrolls the containing pane.
