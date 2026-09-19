# Follow camera stability through truck disclosure

Local implementation only; no deployment, API or database changes.

## Cause and change

The camera viewport correctly stopped notifying camera ownership for inspector
size changes. However, the next Follow playback frame still called its live
`center` calculation, consuming the changed free-map region and shifting the
truck's screen anchor. A stationary truck could conceal the issue until moving.

The truck layer now captures the viewport's screen offset when Follow starts.
Each movement frame uses that offset with the current projection and displayed
truck position. Details/Hide and delayed content update future focus insets,
without changing the active anchor. Explicit Follow activation and actual map
resizing capture a new offset. Follow cancellation and disposal release it.
No DOM reads, timers, requests or allocations of offset snapshots were added to
the playback loop. Current route and telemetry data are unchanged.

## Verification

- The browser regression failed against the previous implementation when the
  first movement frame followed an inspector height change.
- `bash test.sh map` passed: 253 Client C# checks, 125 server C# checks,
  333 map JavaScript checks and 47 JavaScript architecture checks.
- Typed JavaScript checking and JavaScript asset generation passed.
- Strict Client build passed with zero warnings and errors; localhost restarted.
- `mapStartupSmoke.mjs` passed at 1440px and 390px: disclosure, delayed content,
  subsequent movement, continued Follow, explicit reactivation and actual map
  resize. The resize probe waits for camera ownership to receive the browser's
  asynchronous resize notification before checking the updated anchor.

Browser evidence: managed run `browser-map-startup-wEfwjn`.
The browser uses production host, truck and viewport code with synthetic camera
and position inputs. This is not a live Google Maps or authenticated API test.
The full suite, production performance and PostgreSQL checks were not run.
