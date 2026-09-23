# Fuel hand-over, WhatsApp foundation and trailers release — September 23, 2026

The user authorized publication of the accumulated work with its
migrations. Source: `main` at `218dcb15` for the API; `1741f7f8` (ignore
rules only) for the Client. Nothing here sends WhatsApp messages: no
credentials were saved, automatic fuel sending stays off, and live sending
remains untested.

## Contents

Background ETA every two minutes and prepared running work (`8a8afadb`);
fuel hand-over horizon, sent records and Send plan (`2ef1e953`); driver
contacts (`b945baa0`); WhatsApp Cloud API adapter, signed webhooks and the
consistency contract (`3fc932f1`); trailer catalog separate from the
current assignment (`f83b07a9`) and its immediate targeted refresh
(`85e6dba8`); cancelled loads closing never-started work and planning
refusals reported instead of "updating" (`8b1a44a2`); UI fixes found with
an offline fixture probe (`75b390db`, `76115dbe`).

## Database

A private custom-format backup was taken before migration and listed with
`pg_restore --list` (584 entries); no restore was rehearsed. It is kept in
the ignored workstation directory `local-backups/`. The new revision
applied the four reviewed additive migrations at startup:

- `20260923115327_AddFuelVisitSendsAndAutomaticFuelSending`
- `20260923122137_AddDriverContacts`
- `20260923134223_AddDriverMessages`
- `20260923144550_SeparateTrailerCatalogFromAssignment`

`__EFMigrationsHistory` then held 59 rows. No company had a dispatch
settings row, so automatic fuel sending is off everywhere.

## Publication

- Cloud Build `1adb6247-04aa-4d77-b9e5-f63d2cf49ddb`, image
  `us-east4-docker.pkg.dev/amftms/amftms/api@sha256:6777c6be2f3b373210ae8ec080d3a763273097ae6be1eef4fee7779bf7b4eac4`.
- Revision `amftms-api-b-1adb6247-04aa-4d77-b9e5-f63d2cf49ddb` at 100%
  traffic, scaling mode automatic, 1 GiB, one instance. The previous
  revision stopped at 16:13:41 UTC. `/api/health/live` returned 200; the
  only warnings in the first minutes were expected 401s.
- Firebase Hosting (`https://tms.amfcarrier.com`, `https://amftms.web.app`)
  serves the build's `main.css?v=004e83ce2ba34b9b`; entry HTML and CSS are
  `no-cache`, fingerprinted framework files `immutable`; `/api` through
  Hosting reaches the new revision.
- The release gate passed on the final code (Server 3358, Client 1066,
  JS 629). The first Client attempt stopped at the gate on an
  ignore-file ordering test and published nothing; it was fixed and rerun.

## After the first synchronization (read-only, 16:25–16:27 UTC)

- Load 1407 (truck 11005): trailer 55904 catalogued from the load import,
  the import review cleared, an active execution leg accepted, a route and
  an ETA (16:25:53) prepared in the background.
- Cancelled loads 1401 and 1406: their never-started planned legs closed as
  cancelled. Cancelled 1399 has a recorded movement, so its leg stays
  planned and marked for review.
- Truck 11005 shows no trailer: telemetry reports trailer `055904`, whose
  catalog row is inactive as imported from Samsara (the connector marks a
  trailer active only when it is tagged "AMF Carrier" and not
  "Deactivated"); the load names `55904`. The two numbers are not merged
  by guessing, so the truck carries the conflict. The filter was not
  changed.
- Nine ETA forecasts were calculated in the ten minutes before the check,
  without anyone opening them.

## Remaining

Live WhatsApp sending, delivery and webhooks are untested and need Meta
credentials, the webhook registered at
`/api/webhooks/whatsapp/amfcarrier`, and a driver writing first (no
approved template). The trailer naming for 11005 and the Samsara tag are
configuration for the fleet owner.
