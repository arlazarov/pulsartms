# Release of September 26, night (UTC)

The owner authorized it ("go ahead", in Russian) for the tracking route
fix and asked why trucks 11007, 54777 and 11005 had no ETA on the map.

**Status:** complete. API `257aeaf1` at 00:19 UTC, frontend at 00:27.
No migration; no message sent; no reset or forced replan.

## Package

Since the evening release (`401387c9`):
- `326fcd55`: tracking no longer keeps a remainder or display reference
  that leaves the country (AMF1414 kept its Ontario leg);
- `f1f211b8`: ferry routes only when no road-only route exists, never
  taken automatically;
- `98ce04d5`: the approved Fleet truck card;
- `a93d953d`, `00d1729d`, `b8abde40`: one-line ETA diagnostics on change;
- `257aeaf1`: the map ETA fix (below).

API builds on the way: `c824791c` (a93d953d), `c09efb50` (00d1729d),
`8868e21e` (b8abde40), then `d7a80320` (257aeaf1), revision
`amftms-api-b-d7a80320-1ef1-43f1-8182-5713a04e8441`, image
`sha256:c7592ef5…b948759d`, generation 256, 100% traffic. Each was
verified: Ready, digest, health 200, no error or 5xx.

The frontend gate failed twice before publishing anything: a card test's
timing (fixed in `b8abde40`) and a connection timeout in
`MessagingPostgresTests`. That fixture is isolated and disposable, not
the application database:
- it uses its own database, `pulsr_core_fixture_…`, and its own role,
  `pulsr_test_runner`, which owns nothing else;
- each run makes a schema of its own (`t_<time>_<guid>`) and drops it;
- read on September 26: that role has no access to any of the 104 tables
  of the application database `neondb` and cannot create there, and it
  is not a superuser or a member of the owner role.

"Shared" means only that the fixture database, and the Neon endpoint,
also serve other runs and databases: it shares one Neon compute with
production (`neondb` through the pooler), so a timeout there can be
contention, not a test defect. The third run passed (Server 3,713,
Client 1,222, JavaScript, UI smoke); artifact
`release-Ef4f1C`, whose `index.html`, `css/main.css` and
`appsettings.json` Hosting serves byte for byte.

Backup: `local-backups/pulsartms-release-backup.DWuHnu/before-2026-09-25-night-release.dump`,
28,196,973 bytes, SHA-256 `fc51ed4b…565054160`, 737 entries, 104 table
data; in the inventory. No restore rehearsed.

Protected data before and after: migrations 73, users, conversations,
messages, outbound queue, broadcasts, fuel sends, stored credentials and
automatic fuel sending unchanged. The active-leg hash changed through
ordinary work (11007's assignment revision at 00:23).

## Map ETA: cause and fix

The diagnostics showed, every half minute, "ETA memory dropped the
forecast for scope <leg>: the leg could not be loaded" for the legs of
11005, 11007 and 54777. The ETA queue does not know whose load it holds,
so each refresh is offered to every carrier in turn. amfcarrier's pass
made and saved the forecast; the `meta-review-demo` pass could not see
the leg and dropped it as gone. The map's summary therefore never found
one. It began with the second carrier (September 24–25).

`257aeaf1`: a carrier that does not have the load leaves the forecast
alone. From 00:20 UTC the summaries of all three trucks carry an ETA
("shown") and keep it.

## Results

- AMF1414's plan: recalculated at 00:22:57, 0.7 + 1,229.8 mi, inside the
  US (was 104.5 + 986.5 through Ontario).
- Map ETA: shown for 11005, 11007 and 54777.

## Open

- Truck `bfb0bd83`'s forecast (scope `2f2cc129`) is often "not saved and
  taken back": its ETA shows only between those. Not investigated yet.
- **WhatsApp inbound to AMF: unresolved.** The earlier note that it
  waited for the owner to change the app's callback is out of date. The
  coordinator verified in the App Dashboard that the app-level callback
  is already the AMF URL, with `messages` subscribed. Delivery still goes
  to the demo:
  - 00:33:47 UTC, September 26: Meta's POST reached
    `/api/webhooks/whatsapp/meta-review-demo` and got 200. The message
    was stored as an inbound text of `meta-review-demo`, not a status;
  - no request has reached the `amfcarrier` path since 00:10:34 UTC,
    September 25, the last AMF inbound;
  - both tenants hold the same business number. Signatures pass, and
    both paths arrive through the same Hosting proxies, so neither
    forwarding nor the signature is the failing stage.

  Cause, from our side of the wire: a WABA-level `override_callback_uri`
  on WABA `1393526992996419`. `tools/ReviewerAccess` posts one to
  `subscribed_apps` with the demo URL. Meta verified such a callback with
  a GET to the demo path at 00:35:42 UTC, September 25, and the first
  demo POST followed at 00:36:31. A WABA override outranks the app
  callback, which is why the dashboard and the deliveries disagree.
  Not yet read directly: `GET /v26.0/1393526992996419/subscribed_apps`
  (and the number's `webhook_configuration`) needs the stored access
  token, which was not used. The fix is to clear or replace that
  override, not to change the app callback; while the number is shared,
  only one tenant can receive. AMF's outbound statuses go the same way
  and stay "accepted". The 11 inbound texts stored under the demo since
  September 25 were not moved.
- The ETA diagnostics stay: one line per change, no per-poll noise.
