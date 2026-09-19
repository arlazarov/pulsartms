# Compact Fleet Map release — 2026-09-12

## Scope

- Start truck inspection with driver, fuel, actions, remaining miles, next location
  and ETA; Details expands the existing mounted content on desktop and mobile.
- Fit the selected truck's available road once without refitting on polling.
- Preserve saved geometry when progress is unavailable; retain a previously
  trimmed road through a later telemetry gap. This does not fabricate measured
  mileage, fuel, ETA or completion state.
- Cluster nearby unselected trucks at overview zoom. Mouse/touch expands groups;
  selection stays individual. Group badges avoid stop, fuel and truck labels.
- Use thinner dashed future routes until selected, retaining geometry caches.

## Verification and publication

Final artifact: `artifacts/managed/release-5EBSYX/publish/wwwroot`.

- Full release gate: 1,589 Server tests, 723 Client tests, 449 Node tests passed.
- Strict Release build: zero warnings and errors; 264 assets and seven generated
  JavaScript entry-point dependency graphs verified.
- Fleet inspector browser probe: 10 width/theme cases, no failures or unexpected
  requests (`browser-hours-forecast-Er2mjK`).
- Fuel editor browser probe: eight cases passed, including mobile pointer drags
  and server-prepared quantity redistribution (`browser-fuel-editor-lymN1M`).
- Production GPU source probe: two device-pixel-ratio cases passed, including
  cluster clicks and unknown-progress road rendering (`browser-map-markers-FetZAv`).
- `git diff --check` passed.
- Published that exact artifact to Firebase Hosting project `amftms` at
  `https://amftms.web.app`. No API deployment or database migration was required.

Browser probes use deterministic API/provider substitutes. PostgreSQL checks
were not run: no safe isolated fixture was available. Live Google Maps/Samsara
behavior, the current production state of truck 54777, production performance
and complete visual correctness were not established by these probes.
