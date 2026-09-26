# Hours forecast probe migration

Scope: `Client/tests/browser/hoursForecastSmoke.mjs`, and the two shared
helpers it calls, `mobileTruckScrolling.mjs` and `truckReadingsLayout.mjs`.
Base `63f61ea4`. No production source was changed here; the product fixes
the revived probe found were made and reviewed in the root integration
branch (`5ca504fd`, `c98a0b5d` and its later local CSS).

## What changed in the probe

- Read-only fixtures for the shell's own reads (`/api/driver-groups`,
  `/api/messaging/unread`, `/api/messaging/changes`). They are excluded
  from the duplicate-work count `apiReads` but reported per case and
  endpoint as `shellReads`; every fleet, dispatch and planning read is
  still counted.
- Obsolete markup migrated to the approved Fleet card of September 26:
  the route's distances, delivery and timing groups became visit (next,
  where) and facts; the appointment and arrival read in the head; the
  HOS label is the clocks' accessible name; readings lead with icons;
  the weather is a `truck-readings__reading--outside` cell; the next
  load's stop card drops the recap row; the disclosure says Details or
  Hide details; a card under 40rem is open whole.
- Relocated rather than kept: the old run total and timing column
  checks (fuel on arrival is the stop card's, covered by stopCardsSmoke
  and fleetDesignSmoke; the route's column split is pinned by
  truckCardLayout.test.js and drawn by fleetDesignSmoke).
- Loading geometry keeps "nothing moves while data loads", measured by
  zones. The head's first row, the duty row and a stacked next stop may
  grow only by content arriving for the first time (the stop headed for,
  the booked hour, the cycle badge, rest countdowns, street and town),
  bounded by that content's measured height and row gap. The one
  shrinking allowed is the Remaining load metric removed when the final
  stop becomes known (node retained). Everything below a zone must move
  by exactly the zone's growth.
- Layout alternatives follow the measured container: badges beside the
  hour when the value column has room, under it otherwise; clocks beside
  the Dispatch vehicle line only on a truck card of 1050px or more; the
  Fleet toolbar on one line where its controls fit, otherwise the heading
  centred on the wrapped toolbar with non-overlapping rows.
- Positions compared across interactions are measured against the card's
  scrolled content; the scroll itself is reported (`interactionScroll`).
- mobileTruckScrolling: the obsolete 70% ceiling is replaced by the
  card's stylesheet cap and an independent half-of-the-map contract.
- truckReadingsLayout: repaired after the TruckReadings class rename in
  `242ab81a` (the weather became a reading cell), opens a wide card's
  Details before reading its lower rows.

## Findings fixed by root during the migration

- Load column recentred 9px when the forecast arrived: hgzrSD, and the
  same on the untouched release (ozSL9t). Fixed with align-self start.
- Enlarged clocks capped by a 640px group at 2344: CRIaso. Fixed with a
  scoped clocks panel max-width.
- Enlarged clocks squeezed in a 670px column at 1920: GC6lBY. Fixed with
  the 54rem stacked vehicle and clocks row.
- Visit grid 11rem wider than a 200% phone card (64px sideways scroll):
  fGQdrD. Fixed with minmax(min(100%, 11rem), 1fr).
- Load placeholder 2.8px shorter than the load link on a stacked head:
  1brMwf. Fixed by setting the placeholder at the link's lead size.

## Final runs

Artifact `scratch-sibwn1` (root production `f6184f83` CSS), main.css
sha1 575b876238e238507e2cf6b7cfcb24c0e7433695, identical before and
after the matrix.

- v1g25L, full matrix of 12 cases: 10 clean; 1200 light and dark failed
  the old rule that the heading centres on the search row.
- After v1g25L only that toolbar assertion changed (heading centred on a
  wrapped toolbar, rows inside it and non-overlapping, one line where
  the controls fit). tIZVA2 and Qt1x6D, 1200 light and dark on the same
  artifact, pass; they supersede those two failures. The other ten cases
  and the lifecycle were not repeated.
- jjqGYN, lifecycle mode (16 transitions): pass.

No unexpected requests or browser errors in any final run. Pinned runs
carry `.keep`. Not run: PostgreSQL, the .NET suite (browser scripts
only), production traffic.
