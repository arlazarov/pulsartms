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

## Second publication: dark only (12:31 UTC)

The owner asked to publish the follow-up and then to disable light-theme
selection before publication ("Ok, publish"). Firebase Hosting,
12:31:08-12:31:14 UTC, 288 files; no API deploy, no data written.

### Package

`453c4e95` on `claude/release-2026-09-28b`: the first publication's
record `fc373bf5`, then

- `23f03483` (designer): the phone truck card always open (no Details
  toggle), one stable card box per breakpoint, Appointment copies itself,
  the truck panel parks instead of hiding (no replayed arrival), each
  card opens from its head (scroll reset on mode change), the booking
  time wraps at enlarged text;
- `baa8ddbb` (designer): one head for every map card, translucent light
  glass, no decorative glow in light;
- `35bd620e` (designer): dark is the only effective theme, from the
  first paint (`<html data-theme="dark">`, no localStorage bootstrap),
  no theme toggle or selector; the account's saved theme is kept and a
  units change sends it unchanged (an unchosen `""` sends `dark`, the
  only value besides `light` the live server accepts). The candidate
  before this fix could not load preferences for an unchosen account;
  production has 3 users and none unchosen (read-only count);
- probes only (`40b472d4`, `34fb63af`): truck panel probes, the arrival
  animation read after it finishes and counted from a mark with a
  positive control, the phone card open, the stable box, dark only.

### Gates

- `adf89cab` failed once in bUnit (`release-J2yaX1`): no product race;
  the Dispatch board's first render costs about 2.5-3 s of one core
  cold and a CPU burner reproduces the failure (`diagnostic-cv05Ug`).
  Contention during that gate is consistent, not proven.
- `9e1c6074` passed (`release-yp2smA`), superseded by dark only.
- `453c4e95`, with other sessions' heavy work paused, exit 0:
  JavaScript 669, Client 1,341, Server 4,041, uiSmoke
  `browser-ui-Y2lX9t`, messaging `browser-messaging-tabs-nJmRNs`;
  artifact `release-5vB9hT`, pinned. The probe owner's dark-only runs
  on `35bd620e`: uiSmoke 12/12, messaging, appearanceSmoke, hours and
  fuel pass their dark-only checks.

### Verification

`tms.amfcarrier.com` serves, SHA-256 equal to the artifact:
`index.html` (`<html lang="en" data-theme="dark">`),
`css/main.css?v=f9b9d956ae239488`, `appsettings.json`, and the
fingerprinted `blazor.webassembly`, `dotnet`, `dotnet.native` and
`Client` files; entry HTML `no-cache`; `/api/health/live` 200. Before:
`main.css?v=f74168069f28f65e`.

### Incident 11006

Root saved AMF1385's and AMF1395's deliveries through the load page
after the first publication (about 12:07 and 12:12). A read-only
readback: both loads and their legs completed, every native stop
complete (1395's pickup by the owner's rule), source trailers and actual
times unchanged, no transfer, planning changes processed in 1-6 s with
none pending; the truck's current work is load 1412. Left with owners:
1395's source review banner (Root); the truck's fuel plan (04:11)
predates 1412's route plan (12:08) (fuel); 1412 has no saved ETA
forecast (ETA).

### Known gaps

The phone fuel editor at 390x667; the Dispatch workspace ETA rows 4 px
too wide at 1200; about 20 ungated probes still loop a light theme;
workspaceSmoke stale since September 27. The unified card head
(`claude/card-header-unify`) is included; nothing newer is.
