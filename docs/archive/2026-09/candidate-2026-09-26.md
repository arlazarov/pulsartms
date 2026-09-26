# Release candidate of September 26

**Status:** prepared, not published. It waits for Root's final clearance
and the owner's go, per [release.md](../../operations/release.md).

- **Candidate:** `3630bbdb` (code). Commits after it change documentation
  only; a release is published from `3630bbdb` exactly.
- **Deployed now:** API `257aeaf1` (revision `d7a80320`), frontend
  `eeab5a34` (Hosting version `77a0ab003a21c5d9`).
- **Excluded:** `2e8d63e2`, the first-stop fuel reserve policy, which the
  owner has not decided. `54c186af` reverts it on `main`; branch
  `fuel/first-stop-reserve-policy` (`2fd002c1`) keeps it. Net change to
  the fuel optimizer, its tests and the fuel rules: none.
- **Scope since the deployed API:** 50 files in `Server` and `Client`,
  details in [overnight-2026-09-26.md](overnight-2026-09-26.md):
  - messaging signals by long poll (`6ae6bd0e`, `b070e218`, `52038a20`,
    `18232d06`);
  - ETA: reason logged, publish after commit, retire only on a
    demonstrated change (`d7a8aeea`, `8aa1723e`, `f9cfdd92`);
  - tenant isolation and background passes (`b4a332ba`, `327252b9`,
    `b044021e`, `50795128`, `c732e490`);
  - accounts only for an existing, active carrier (`c3ab25ea`,
    `d8f5aab9`);
  - truck card speed bands (`325c6cc2`);
  - tests only (`ec5662c5`, `418aadf4`, `3630bbdb`). `3630bbdb` is
    `c8234a92` from an unmerged branch, a test fix for a flake that
    failed the first gate on `e2d959b2`.

## Final gate

`PULSARTMS_RELEASE_UI=1 bash verify-release.sh` on a clean worktree of
`3630bbdb` with the local Client settings copied in: passed in 366 s.
JavaScript 656, Client 1,225, Server 3,743 (all unfiltered), Release
build with warnings as errors, published-asset verification, offline UI
smoke and the messaging tabs smoke, no errors.

- **Artifact kept:** `artifacts/managed/candidate-3630bbdb-release-h5qEVM`
  (`.keep`, `verify.log`, `manifest.sha256`): 285 files; manifest SHA-256
  `c61b7da95ac71d7232df06bba2ecc7660d596646cd599a7548855a0963e899d5`;
  `index.html` `16bfee49…`, `css/main.css` `920af594…`,
  `appsettings.json` `ca2a6e6c…`.
- **Earlier attempt:** the gate on `e2d959b2` failed one test,
  `AnImportedLoadsUnknownTrailerIsCataloguedAndPutOnItsTruck`, the known
  `ProcessGates.Fleet` flake whose fix `c8234a92` had never reached
  `main`. It was not rerun until green: the fix was cherry-picked
  (`3630bbdb`), its held-gate regression reproduces the failure
  deterministically, and the gate ran once on the new candidate. Its log
  is kept as `artifacts/managed/verify-e2d959b2.log`.
- **Not rerun:** the API's Cloud Build gate runs again at API deploy, as
  it always does.

## Migration and backup

- **Migrations:** none since `257aeaf1`; the schema does not change.
  Rollback is the previous API revision and Hosting version.
- **Backup:** not required by the schema. Following practice, take a
  read-only `pg_dump` into `local-backups/` before the API deploy.

## Release order and what changes for users

1. **API first**, then the frontend. Until the frontend lands, the old
   client's `/api/messaging/events` answers 404 and it falls back to its
   30-second poll; open tabs keep the old client until reloaded.
2. **Sign-in:** an account now counts only for an existing, active
   carrier. Read-only check: all 3 production users' carriers exist and
   are active.
3. **Fuel station sweep** resumes after 5 days of checking nothing. It is
   billed Places requests: at once only stations in live plans (hourly)
   and one never checked; the weekly re-check of about 700 falls due
   around September 28, 15 per pass.
4. **Messaging:** at most 4 mailboxes per account and 40 waiting requests
   per instance; beyond that 503 and the browser polls.
5. **Logs:** "ETA forecast for scope … was not saved and not published:
   {reason}; the shown one stays|was retired", once per failed refresh.

## Before and after publishing

- **Blocker:** the local `gcloud` credentials expired; the owner must
  sign in again before the API can be read or deployed.
- **After release, verify:** revision and traffic, health, no 5xx; the
  11006 unsaved reasons; one owner-sent WhatsApp test with measured
  latency (the fix is local until then); the speed colours on the card.
