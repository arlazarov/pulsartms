# Compact inspector load and HOS

The default Fleet Map truck inspector exposes the existing four shared HOS dials
and load/order references. A scheduled Delivery line uses the final delivery visit
in sequence, including an appointment window. It does not relabel the next pickup
or calculated arrival as the delivery appointment. Available load details take
precedence; a valid saved plan supplies a fallback while details are unavailable.
Missing delivery appointments remain a dash.

The compact inspector now uses the same bounded width token as expanded Details.
The unused compact-width token was removed. Narrow layouts stack telemetry, HOS,
actions and load summary, retaining the existing bounded internal scrolling and
unchanged map rectangle. Expanded mode retains its existing next-stop appointment.
No additional HTTP requests, business writes or provider calculations were added.

## Checks

- `bash test.sh fleet styles`: Server 290 and Client 212 passed, plus map, styles
  and architecture Node suites. Three added component cases cover delivery job
  names, missing delivery, order/load identity, date window and selection changes.
- Strict isolated Release Client publish passed at
  `artifacts/managed/scratch-Qexgqq/publish`; 264 asset integrity checks passed.
- Fleet-only browser matrix passed ten cases in both themes at five widths,
  with no errors or unexpected requests. Evidence:
  `artifacts/managed/browser-hours-forecast-2uvnTy`.
- Desktop and phone screenshots were inspected. The phone card scrolls internally
  for its lower route facts; it does not resize or cover the entire map.
- Full suite, authenticated provider and PostgreSQL checks were not run.
  No deployment or migration was performed.
