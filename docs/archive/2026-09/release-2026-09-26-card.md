# Frontend release of September 26: the truck card head

The owner authorized it after asking for the head of the truck card to be
drawn evenly: "Cycle short" on the ETA's line and every row aligned.

**Status:** complete; Firebase released it at 01:26:24 UTC. Frontend only;
the API is unchanged (`d7a80320`, from the night release).

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
  `appsettings.json` were byte-identical to the artifact when compared
  right after the release. The settings parse as JSON with the Maps key.
  `/api/health/live` is 200.

## Evidence for independent verification

The artifact directory `release-6BXZEV` is gone: it lived inside the
temporary release worktree, which was removed after the release without
pinning it. What remains checkable:
- Hosting version `sites/amftms/versions/77a0ab003a21c5d9`, released
  2026-09-26T01:26:24.965Z, 287 files, 14,565,005 bytes. The Hosting API
  lists every file's hash; the SHA-256 of its sorted `path:hash` lines is
  `091cf9eb786b5e61f51f23316b44980a56d054535332d282c8b0a7e437b13219`.
- SHA-256 of the files as served (and, by the first 16 digits compared
  right after the release, of the artifact):
  - `index.html`
    `fd4507955c08c1de513f8664ef85619e2bff960e081e15100a20736e29205de0`;
  - `css/main.css`
    `d30e4d513a79c9c02c3e1646ae6c315d016cd49c007a55691707348a0cedea44`;
  - `appsettings.json`
    `ca2a6e6c74ce65676cdb76b87f91253e0c4879979d96b9a806fb507057b1380e`.
- The source is commit `eeab5a34`, plus the ignored local
  `Client/wwwroot/appsettings.json`, which is not in git.

The layout was checked against the approved design with the card's real
markup at 896, 720, 390 and 320px, at 100% and 200% text. It was not
checked on the live map with production data.
