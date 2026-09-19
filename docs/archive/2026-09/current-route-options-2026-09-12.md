# Current-position route options

## Scope

Route options for the authoritative current load use a fresh truck GPS origin
and unfinished stops. Future loads retain their complete truck itinerary.
The preview accepts one to three provider options and removes exact duplicate
geometry; it does not synthesize alternatives or identify nearly identical roads.

Saving a current-position choice preserves the full historical baseline and
updates the live remaining road in the same transaction. Preview ownership,
input/profile revisions, plan identity/version, remaining stop IDs and GPS
freshness/movement are validated before saving. Saved via points proved behind
the truck are omitted when reopening the editor.

## Verification

- `bash test.sh routing fleet`: 775 Server and 384 Client tests passed, with the
  required JavaScript and architecture checks.
- `bash verify-release.sh`: 456 Node, 1,606 Server and 731 Client tests passed;
  strict build completed without warnings or errors. Client publish validation
  checked 264 assets and seven JavaScript dependency graphs.
- Browser route editor probe: eight cases at 1440, 768, 390 and 320 pixels in
  light/dark themes completed without reported errors or horizontal overflow.
  The intercepted fixtures exercised a two-option GPS preview, a one-option
  manual preview, via editing and explicit save. Desktop and mobile options/edit
  screenshots were visually inspected.

Local evidence: `artifacts/managed/browser-route-editor-e49uLl/report.json`.
Verified publish: `artifacts/managed/release-7Frh1s/publish/wwwroot`.
Managed artifact retention may later remove these directories.

No live routing-provider/GPS exercise, production write, PostgreSQL integration
check or production performance measurement was performed. Browser APIs and the
map provider were intercepted; this does not verify real Google map interaction.

## Release status

Local only. This change adds no migration. API and Client must be released
together; older API code does not understand the saved remaining-road payload.
The separately reported fuel-marker color transition and Dispatch loading
performance are not fixed by this change.
