# Direct stop driver assignments — September 18, 2026

Driver/co-driver corrections now persist on accepted stops with an explicit
nullable override. Exact-stop, onward and range corrections retain execution
leg identities, stop order, actuals and transfer confirmations. Driver-only
corrections do not require a Switch. Equipment scope restrictions remain.

The workspace exposes consecutive driver sections. Native route/current-driver
reads retain overrides, planned mileage uses the departing stop's driver, and
existing recorded mileage is not relabeled. Mixed future drivers retain the
conservative ETA unavailability boundary rather than borrowing another driver's
HOS. This is not driver settlement or payroll implementation.

Migration `20260918041329_StopDriverAssignments` was applied only to the isolated
`pulsr_development` database. Existing rows inherit their leg defaults. Downgrade
rejects persisted overrides instead of silently discarding them. Production was
not migrated or deployed.

## Verification

- Full suite: 2,737 Server, 1,016 Client C# and 561 JavaScript tests passed.
- Strict Debug solution build: zero warnings and errors.
- New regression scenarios cover exact-stop, range and onward changes, explicit
  driver removal, retained neighboring drivers, stable topology and history.
- Manual development PostgreSQL/API checks changed stops 3–4 independently,
  cleared and restored stop 2, retained stop/leg identities and created no Switch.
  These used editable synthetic development data, not a disposable test reset.
- Browser verification saved Driver C on AMF1 stop 2 with `This stop only`.
  The saved stop list showed drivers B, C, A, A: all neighbors were retained.
- The broader PostgreSQL migration probe was not rerun for this change. Existing
  production performance and complete visual correctness are not established.

Evidence: managed runs `diagnostic-b18FC7` (full tests), `diagnostic-N3Acq6`
(manual API checks), `diagnostic-4dWbc4` (development migration), and
`scratch-L1HeiZ` (running localhost build). No production data was changed.

## Multiple driver drafts on one page

Driver-only drafts now survive stop selection and share the page Save/Discard
controls. Unsaved names appear in the stop list. The existing correction endpoint
processes drafts in last-edit order; successful responses provide the next opened
revision. Failed writes retain remaining drafts and stable retry identities.
This sequence is not an atomic transaction across multiple corrections.

Regression coverage includes navigation without writes, restoring a selected
stop's draft, discarding all drafts, overlapping onward/exact-stop scopes with
explicit driver removal, and retrying a later failure without resending earlier
confirmed changes. The Dispatch dependency group passed: 1,240 Server tests,
633 Client C# tests, and 64 JavaScript checks, including architecture checks.
The full suite was not rerun for this Client-only follow-up. Strict Client build
passed with zero warnings/errors. No migration or deployment was needed.

Browser verification on localhost edited AMF1 stops 2 and 3, returned to stop 2
without saving, then saved both with one click. Both values appeared in the saved
stop list. The existing AMF2 tab was refreshed to the new Client build.
Evidence: `diagnostic-FUDC6A/tests.log`; running build: `scratch-3IgGLI`.

## Stop selection resource reads

The page now retains one resource-options read across stop editor instances.
Selection reuses the loaded truck, trailer and driver lists instead of issuing
three new fleet reads. Pending reads belong to the page identity, so disposing
an old stop editor cannot cancel the next stop's shared request. Failed reads
remain retryable. New load identities receive a new options owner; server-side
assignment validation remains authoritative.

The page regression asserts exactly three fleet reads across navigation both
before editing and with driver drafts. Dispatch checks passed again: 1,240 Server,
633 Client C#, 64 JavaScript. Strict Client build passed. Browser checks navigated
AMF2 from delivery to Drop and Hook without editing, with enabled assignment
fields on each selected stop. No production latency benchmark was performed.
Evidence: `diagnostic-pbaRdP/tests.log`; local build: `scratch-YKP8EY`.

## Selection-only map publication

Stop selection previously included every saved road coordinate in the serialized
map fingerprint and JavaScript publication. Draft mode omitted roads, explaining
a plausible difference between clean and dirty page interaction cost. Selection
now publishes only the selected identity through `selectStopMap`; route identity
and compact marker data independently govern geometry publication. JavaScript
retains pending selection across delayed provider initialization.

A 20,000-point component fixture verifies selection adds no geometry publication
or route read. JavaScript checks retain roads and apply the latest selection after
provider initialization. Dispatch checks passed: 1,240 Server, 633 Client C#,
64 JavaScript. Strict Client build passed. AMF2 was refreshed and selection was
checked before editing. These checks establish eliminated work, not a measured
production latency improvement. Full suite was not rerun; no deployment occurred.
Evidence: `diagnostic-rjdb1n/tests.log`; running build: `scratch-Vi14dv`.

## Pickup details independent of structural locks

Ordinary stop CanEdit no longer inherits starting-stop, arrival/completion or
mileage structural locks. CanMove/CanRemove and segment boundaries retain their
existing restrictions. Updating an editable load also publishes corrected details
on its completed single-load legs; actuals and assignment identities are retained.
ExecutionAcceptance still rejects path changes protected by recorded mileage.
Cancelled/completed load workspaces, shared legs, transfers and dedicated
operations keep their restrictions. Noneditable ordinary stops expose full
read-only fields and the reason, instead of replacing them with a short summary.

Regression checks cover starting pickup address correction without removal,
recorded pickup instructions without changing actual completion time, stale-draft
rejection and visible disabled fields. Browser verification saved a synthetic
contact on the completed AMF2 pickup and retained its Completed state.
Strict solution build passed with zero warnings/errors. Dispatch checks passed;
no migration or deployment was required. Running localhost: `scratch-Bn4aeA`.
Full suite passed: 2,738 Server, 1,020 Client C#, 561 JavaScript tests.
Evidence: `diagnostic-pep6qS/tests.log`. PostgreSQL fixture checks were not run;
the browser save used the separate synthetic development workspace.
