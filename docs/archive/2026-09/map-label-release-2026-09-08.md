# Map label readability release — 2026-09-08

## Scope

Client-only rendering changes: larger map labels and symbols, modestly wider
routes, 13px truck numbers with compensating padding, and per-font bitmap
atlases sized to display density. Density changes refresh typography without
rebuilding route geometry; the scene removes its resize listener on disposal.
API code, database schema and production server configuration are unchanged.

## Verification

`AMFTMS_RELEASE_UI=1 UI_TEST_BROWSER_CHANNEL=chrome bash deploy-client.sh`
passed all 101 Node, 111 Client C# and 514 Server tests (726 total), the strict
Release build with zero warnings/errors, 222 published asset checks and six
JavaScript dependency graphs. All 40 offline UI smoke cases passed. These UI
fixtures substitute the map provider; they do not establish production GPU
performance or hardware-specific text quality. Local map visual checks were
performed separately before this deployment attempt.

Verified artifact: `artifacts/release.IxlOQ1/publish/wwwroot`.

## Deployment blocked

Firebase rejected expired CLI credentials before publishing. The script exited
with code 2; this attempt did not deploy the verified artifact. The user must
complete `firebase login --reauth` before publication can resume. Do not request
credentials or authorization codes in chat. The staged artifact is retained.
Production asset verification remains pending a successful deployment.

## Successful retry

After the user completed Firebase reauthentication, the retained artifact passed
the 222-asset/six-entry integrity check again and was deployed directly with
`firebase deploy --only hosting --public` using that exact staged directory.
Firebase reported release completion. At 2026-09-08 14:35 UTC, all 69 checked
index/JS/WASM/CSS resources on `https://tms.amfcarrier.com` returned HTTP 200
and matched the staged SHA-256 bytes; non-HTML resources did not return SPA HTML.
Authenticated production map interaction was not repeated in this retry.
No API deployment or database change was performed.
