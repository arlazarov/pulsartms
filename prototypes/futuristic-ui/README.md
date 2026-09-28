# PulsR futuristic interface concept

A localhost design reference for Fleet Map and Dispatch that reads real data
through the existing API. It is not a deployable app and is never published.

## Run

```bash
node prototypes/futuristic-ui/server.mjs
```

Open <http://localhost:5179>. The host binds to 127.0.0.1 only.

- **Sign in · live data** uses the normal PulsR sign-in. The default upstream
  is the production API (`https://amftms.web.app`); set `PULSR_API_ORIGIN`
  to use another, for example a local API on `http://localhost:5086`.
- **Open labelled demo fixtures** uses synthetic data for layout checks. It is
  labelled on every screen and never mixed with live data.
- `?fixtures=1` opens the demo directly; `?theme=light|dark` forces a theme.

## Safety

- `read-routes.mjs` is the complete list of forwarded routes: the reads the
  maintained Fleet Map and Dispatch pages make (three are server queries sent
  as POST) and the sign-in, refresh and sign-out routes. Everything else,
  including camera, sync, messaging, fuel sending and every PUT, gets 403.
- Tokens stay in the browser tab's sessionStorage. Nothing is written to disk.
- No map provider key is used; boundaries come from us-atlas/world-atlas.

## Data and refresh

| Feed | Endpoint | Cadence |
| --- | --- | --- |
| Positions | `GET api/fleet/locations` | 10 s |
| HOS | `GET api/fleet/hos` | 15 s |
| Board | `GET api/dispatch/board` (12 per page, all pages) | 60 s |
| Planning summaries | `POST api/dispatch/board/planning` | 10 s until every row has a plan, then 60 s |
| Selected truck plan | `POST api/fleet/trucks/{id}/planning` (known plan version) | 10 s |
| Next routes | `GET api/dispatch/truck/{id}/next-routes` (revision echo) | 30 s |

Polling pauses while the tab is hidden and every poll stops when the page
changes. The current trip is the dispatch the server's planning result is for;
the display rules in `public/js/rules.js` name their Client sources.

## Checks

```bash
node scripts/artifacts.mjs run scratch -- \
  node prototypes/futuristic-ui/check.mjs '{artifacts}/profile'
node scripts/artifacts.mjs run scratch -- \
  node prototypes/futuristic-ui/shoot.mjs '{artifacts}'
```

`check.mjs` drives headless Chrome over the demo fixtures: selection
agreement, chain and search selection, Follow across position updates and its
end conditions, filters, tabs, panels, theme, Dispatch selection sharing and
page overflow at 390 and 360 px. `shoot.mjs` saves the eight screenshots
with true device emulation (headless windows cannot go below ~500 px).

See `PARITY.md` for the comparison with the current pages.
