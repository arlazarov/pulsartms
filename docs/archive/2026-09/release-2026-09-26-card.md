# Frontend release of September 26: the truck card head

The owner authorized it after asking for the head of the truck card to be
drawn evenly: "Cycle short" on the ETA's line and every row aligned.

**Status:** complete at about 01:45 UTC. Frontend only; the API is
unchanged (`d7a80320`, from the night release).

- **Code:** `eeab5a34`, from a clean worktree with the local Client
  settings copied in:
  - `b8644000`: the head's layout;
  - `eeab5a34`: `ArrivalEstimate` reads the held forecast once per render.
- **First gate (`f0c3c483`):** failed, nothing published.
  - `DispatchPlanningRetentionTests` threw in `ArrivalEstimate`: one
    render read the clock-dependent forecast three times, and a tick
    between reads left the third with nothing. This was a real race,
    fixed in `eeab5a34` with a regression that reproduces it.
  - `AdvisoryLockTests` timed out connecting to the isolated Postgres
    fixture, on the Neon compute it shares with production.
- **Second gate (`eeab5a34`):** passed. Server 3,713, Client 1,223,
  JavaScript 655, UI smoke; artifact `release-6BXZEV`. Firebase uploaded
  285 files.
- **Served:** `index.html`, `css/main.css` (`?v=d30e4d513a79c9c0`) and
  `appsettings.json` are byte-identical to the artifact. The settings
  parse as JSON with the Maps key. `/api/health/live` is 200.

The layout was checked against the approved design with the card's real
markup at 896, 720, 390 and 320px, at 100% and 200% text. It was not
checked on the live map with production data.
