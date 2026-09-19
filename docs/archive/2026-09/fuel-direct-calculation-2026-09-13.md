# Direct automatic fuel calculation

Calculate automatically now invokes the existing reset request on one click.
The confirmation state, markup and unused styles were removed. Opening the editor
still does not calculate. The opened version token, request ownership, busy guard
and failure handling remain in place; failed requests retain the draft.

## Checks

- Regression tests failed before the change for both unchanged and edited plans.
- `bash test.sh fuel map styles`: 463 Client and 1,109 Server C# tests passed,
  plus 335 map, 117 style and 47 architecture JavaScript tests.
- Strict Client build: zero warnings/errors; styles compiled successfully.
- A local Release artifact was built for offline browser verification.
- The staged fuel editor passed at 1440x1000 and 390x844 in both themes,
  including one-click calculation, frozen version, Save, Cancel and Escape.
- Component tests hold a reset response pending to check duplicate suppression,
  busy state and draft retention after a failed calculation.

The complete browser matrix did not pass. At 390x667 light, before the calculation
scenario, the route pane had only 90.3125 pixels of height and no complete visible
timeline rows. The runner stopped there; subsequent short-screen cases were not
run. This change does not address short-screen timeline sizing.

Browser evidence: `artifacts/managed/browser-fuel-editor-SHIZGW/report.json`.
The probe uses synthetic API responses, not live fuel calculations or writes.
No database checks, migrations, full release gate or deployment were performed.
The localhost Client was rebuilt and restarted.
