# Truck inspector typography and independent groups

The truck inspector now caps both densities at 56rem. Identity and driver share
one header group, including on phones. Details uses the shared compact text button,
keeps its label in both states and retains keyboard focus. Action icons use the
shared desktop/touch control metrics.

Labels and supporting facts use small type; operational values use body/600;
truck identity, load number and headline readings use lead/600. Metric fuel follows
the same reading hierarchy. HOS keeps its 44px component-owned dials in both states.
GPS puts the observation time alongside its label on wide maps.

Distances and the next visit now own independent vertical stacks. Longer cycle
forecasts and expanded total mileage cannot stretch or shift these groups. The
next visit and ETA receive equal flexible width. Narrow cycle rows wrap instead
of clipping enlarged text. Business calculations and map-selection logic are unchanged.

`bash test.sh fleet styles` passed: 290 Server, 215 Client, 305 map JavaScript,
95 style and 46 JavaScript architecture checks. Strict Debug Client builds,
SCSS compilation and an isolated Release Client publish passed.

Staged artifact: `artifacts/managed/scratch-KXVwox/publish/wwwroot`.
Browser evidence: `artifacts/managed/browser-hours-forecast-WBbnD9/report.json`.
All ten width/theme cases passed, including 200% text, stable disclosure geometry,
typography, keyboard focus and independent groups under longer expanded facts.
Earlier iterations exposed mobile cycle clipping at 200% (fixed) and one
forecast-polling wait timeout; the final run completed without failures or browser errors.
Desktop light/dark and mobile screenshots were inspected. The Debug Client was
rebuilt and restarted on localhost:5067; the Fleet Map URL returned HTTP 200.
These managed temporary outputs are subject to retention. Browser checks use
synthetic APIs and map callbacks, not live authentication, providers or a database.
The full release gate and PostgreSQL checks were not run for this Client-only change.
No migrations or production deployment were performed.

## Follow-up: appointment and ETA rows

The next appointment now sits above ETA in a dedicated timing group. Both rows
keep labels and values inline at desktop width, including a full appointment
window. Remaining places its label beside miles, with kilometers beneath that value.
Narrow layouts retain wrapping. The expanded-state typography and HOS remain unchanged.

`bash test.sh fleet styles` passed again with the same counts above. Strict Debug
build, isolated Release publish and all ten width/theme browser cases passed,
including the long-window and enlarged-text checks. Browser evidence:
`artifacts/managed/browser-hours-forecast-mKgcgW/report.json`; staged artifact:
`artifacts/managed/scratch-6jiu1H/publish/wwwroot`. Desktop screenshots were inspected.
Localhost was rebuilt and restarted; no full release gate, database checks,
migration or production deployment was performed for this follow-up.
