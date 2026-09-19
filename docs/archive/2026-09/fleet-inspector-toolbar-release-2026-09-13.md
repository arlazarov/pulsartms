# Fleet inspector and toolbar release — September 13, 2026

Published the user-approved Client update to Firebase Hosting `amftms` at
21:47:13 UTC. The API remains on `amftms-api-00110-ph9`; no server deployment,
environment change or migration was performed.

## Changes

- Desktop route/load facts precede the disclosed telemetry, HOS and GPS group.
- Phone Details reveals the entire card; Hide retains the title strip.
- Phone arrival facts sit beside the stop address, stacking at narrow widths.
- Fleet Map shares a row with an elastic search and icon-based layer controls.
- Stop addresses omit the pin and ellipsize long streets. Native tooltips and
  copying retain the complete address; locality information still wraps.

## Verification

The full release gate passed: 828 Client, 1,706 Server and 523 Node tests;
strict builds, JS type checking, formatting and 267 staged asset checks passed.
The offline UI matrix passed 52 page cases, and the Fleet matrix passed all
12 size/theme cases, including enlarged-text and mobile probes. Updated visual
contracts reflect the requested heading row and street-only truncation; route
containers remain unclipped. No fixture browser errors or unexpected requests.

Published version: `4972507d4bedc6fc`.
Release: `1789336033498000`.
Previous Firebase version: `03528e01428ab522`.
CSS: `e19f3119c0eef05e`; Client: `Client.e8hn8wzx44.wasm`.

Twenty sampled assets matched staged SHA-256 bytes across both public hosts.
Four SPA-route responses matched the staged index; cache headers and API liveness
were correct. Fresh production Chrome reached Login without JavaScript errors.
Authenticated production map/provider interaction was not tested. No isolated
PostgreSQL fixture or new performance/heap measurements were run.

Evidence under `artifacts/managed`: `release-7B8Xwa`, `browser-ui-BHI8sd` and
`browser-hours-forecast-BTa2JG`. The release directory contains the live asset
verification report and anonymous production Login screenshot.

## Loading animation and mobile alignment follow-up

Published the next user-approved Client update at 22:24:44 UTC. Mobile Remaining
and appointment columns now share their left alignment. The initial personal
preferences gate shows three blue animated dots instead of a visible sentence;
accessible loading status, reduced-motion support and preference gating remain.

The full gate passed 829 Client, 1,706 Server and 524 Node tests, strict builds,
formatting, JS type checking and 267 published asset checks. The staged offline
UI matrix passed 52 page cases; Fleet passed 12 cases without browser errors or
unexpected requests, including the new column-alignment regression. Local loader
checks covered light/dark rendering, accessible status and reduced motion.

Firebase version: `c9fa515324e72a9d`; release: `1789338284408000`.
Previous version: `4972507d4bedc6fc`.
CSS: `b7edc02a93b19fa7`; Client: `Client.z6sc4am1cy.wasm`.
The API remains `amftms-api-00110-ph9`; no migrations or server changes.

Twenty sampled production assets matched staged bytes on both public hosts;
four SPA responses, cache headers and API liveness passed. Anonymous production
Chrome reached Login without page errors. Authenticated production/provider
interaction and isolated PostgreSQL checks were not run. Performance was not
measured by this UI release.

Evidence: `artifacts/managed/release-HhweJS`, `browser-ui-21SUiv` and
`browser-hours-forecast-w2TXIC` under the same managed directory.

## Appointment and ETA wrapping follow-up

Published `artifacts/managed/release-5dr9VO/publish/wwwroot` to Firebase Hosting
`amftms` after the user explicitly requested deployment without checks.
The Client publish build and Firebase deployment completed successfully.
No tests, release gate, browser checks or post-deployment verification were run
for this publication. Do not treat earlier results as this release's approval.

Phone Pickup/Delivery and ETA now keep their label, date and time inline when
space permits, wrapping otherwise. Clock suffixes remain attached to their
time. Arrival status sits beneath ETA. No API deployment or migrations occurred.
