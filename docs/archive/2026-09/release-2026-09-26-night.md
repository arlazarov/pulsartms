# Release of September 26, night (UTC)

The owner authorized it ("Делай") for the tracking route fix and asked
why trucks 11007, 54777 and 11005 had no ETA on the map.

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
timing (fixed in `b8abde40`) and a timeout reaching the shared
development PostgreSQL used by `MessagingPostgresTests`. The third run
passed (Server 3,713, Client 1,222, JavaScript, UI smoke); artifact
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
- WhatsApp inbound to AMF waits for the owner's Meta callback change
  (see `whatsapp-routing-2026-09-25.md`).
- The ETA diagnostics stay: one line per change, no per-poll noise.
