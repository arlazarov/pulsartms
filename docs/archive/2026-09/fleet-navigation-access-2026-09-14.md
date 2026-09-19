# Fleet navigation and authorization correction — September 14, 2026

## Changes

Main navigation now has one Admin-only Fleet entry. Its existing Trucks,
Trailers and Drivers pages remain internal tabs; Fleet Map remains separate.
The optional Fleet route defaults to Trucks and preserves one active sidebar
entry across all resource tabs.

Production reproduced a permission error on Trucks for the signed-in
administrator. `FleetConfigurationController` used standard role authorization,
but the application's current roles are resolved through its database-backed
Admin policy. Standard role claims are intentionally not the authority here.
The controller now uses that existing policy. The same mismatch on mileage-policy
saving was corrected. Handler-level active-account and current-role checks remain
unchanged; no roles, accounts, permissions or database records were modified.

Authorization integration tests now combine the actual controller and method
attributes with the registered policies. They cover list/detail/save for Fleet
and saving mileage policy: current Admin succeeds without standard role claims;
Dispatch, unauthenticated, forged and inactive principals remain rejected.

## Verification and publication

The full local release gate passed 549 Node, 1,005 Client and 1,919 Server tests
without failures or skips and verified 273 published assets and seven JavaScript
entry-point graphs. The staged artifact is
`artifacts/managed/release-VPb3ho/publish/wwwroot`.
The Cloud Build submitted for the matching API source is
`6e2a575c-3eeb-46bf-8782-68135b0c326c`.

The offline browser gate passed all 12 cases (52 page checks), reported in
`artifacts/managed/browser-ui-6iuS28/report.json`. Google Cloud reauthentication
was required during release preparation and completed through the user's browser.
Cloud Build succeeded, including 1,005 Client and 1,919 Server tests. Its Node
worker passed 543 tests and skipped six open-file-inventory retention tests;
the local gate passed all 549. The deployed image digest is
`sha256:7b014460f93780694a8e7cbb78c4443ae0bffcf91327a952e8a5748350b76bee`.
Cloud Run revision `amftms-api-00117-g9t` is ready and serves 100% of traffic;
the previous revision is retired/inactive. Firebase Hosting published the exact
staged artifact after the API transition. Public HTML, CSS and Client WASM hashes
match that artifact, with correct cache headers; public liveness is Healthy.

Authenticated production browser checks confirmed Trucks (including the default
Fleet route), Trailers and Drivers all load their real lists without the previous
permission message. Main navigation shows one Fleet entry. No production edit or
save was submitted solely for verification.

No database migration is required. No isolated PostgreSQL checks
were run; authorization integration uses isolated in-memory SQLite, not the
application database.
