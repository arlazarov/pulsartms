# Frontend release — September 28, 2026 (UTC)

The owner authorized finishing the frontend with the designer after the
server release of the same day
([release-2026-09-28-server.md](release-2026-09-28-server.md)). The live
server `9af9a134` was not touched: `Server/` of the candidate equals it.

**Status:** Firebase Hosting, 11:43:20-11:43:28 UTC, 288 files (33 new).
No server deploy, no migration, no message sent, no data written.

## Package

`3cc30ee4581bdea11787a188e8c8ad989ccddd9b` on `claude/release-2026-09-28`:

- the server candidate `1ccd086f` and the designer's workspace, phone
  fixes and SCSS cleanup (`968bd9eb`, merged in `fbbd9aa5`);
- the uiSmoke locator repair `df633dd8` (merged in `58238a58`);
- `a3687c37`: Save no longer requires `Workspace.CanEdit` for a stop
  correction on a completed load; each drafted stop's `CanCorrect`
  decides, load fields still need `CanEdit` (loads 1385 and 1395);
- `34cf109e`: regression proving a delivery correction completes the
  native stops the source already marks done, with no transfer invented
  and source trailers and actual times unchanged (load 1395, no server
  change);
- the designer's batch `886c33b4`: location and next-stop wrapping at
  enlarged text, the tool bar's own row on phones, the compact opened
  fuel stop on phones, the Dispatch Table lane arrow, satellite imagery
  at natural brightness, HUD arrival (fade and one scan, labels written
  in, values whole, reduced motion a short fade), a wider stop list on
  the load page, the phone truck card at most half the stage, HOS clocks
  in one row on phones.

## Gate

- `PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on `58238a58` failed
  once in bUnit (`release-3AsMQD`). A controlled-signal diagnostic
  (`diagnostic-C7nsKB`) found no product ordering race: the HOS response
  had not completed within the test's 5 s wait. Why it was late is
  unconfirmed.
- The gate on `c4c7d006` passed (`release-glDFtz`) and was superseded by
  the user's later requests.
- The gate on `3cc30ee4`, exit 0: JavaScript 664, Client 1,330, Server
  4,041, uiSmoke `browser-ui-ybid1a`, two-tab messaging
  `browser-messaging-tabs-GPNE9j`; artifact `release-GrBDSv`, pinned.
- Ungated browser probes on the artifact (probe owner): fuelEditorSmoke
  390x844 and 1440 in both themes and mobileTruckScrolling pass;
  hoursForecastSmoke's three timing failures were stale probe reads of
  the new arrival animation (fixed for the next candidate).

## Verification after release

Served by `tms.amfcarrier.com`, SHA-256 against the artifact, all
matching: `index.html`, `css/main.css?v=f74168069f28f65e`,
`appsettings.json` (hosted settings verified), the fingerprinted
`blazor.webassembly`, `dotnet`, `dotnet.native` and `Client` files.
Entry HTML `no-cache`; `/api/health/live` 200. Before the release the
site served `main.css?v=7d647c66a2f55ccc` (`before-index.html`).

Not verified: signed-in pages (the owner's check), the phone shell on a
device, and production browsers.

## Known gaps, with owners

- On a phone, reopening Details replays the arrival animation, and at
  320 px with 200% text the next stop's booking time overflows by 9 px.
  Fixed on `claude/mobile-details-open` (`23f03483`) for the next
  release. Owner: the designer.
- The phone fuel editor at 390x667 cannot show two complete rows while
  the stage keeps half the screen. Owner: the owner.
- workspaceSmoke has been stale since `340ff42c` and `f1918bf1`
  (September 27); it is not gated. Owner: the probe owner.
- Loads 1385 and 1395 (incident 11006) are recovered by Root through the
  load page after this release; this release changes no data.
