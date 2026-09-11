# Fleet assignments

`POST /api/fleet/sync` requires an authenticated TMS user. Samsara requests use
`Samsara:ApiToken`; the token needs Read Assignments in addition to fleet read access.

- Driver → truck: `/fleet/driver-vehicle-assignments`, `filterBy=vehicles`,
  `assignmentType=HOS`, queried at the sync snapshot time. Explicit HOS assignments
  include assignments outside trip time ranges. Passengers and ended assignments
  are excluded. Other assignment sources (static, RFID, etc.) are not used.
- Driver → trailer: `/driver-trailer-assignments`, queried for the returned driver IDs.
  Trailer IDs are matched to Samsara IDs, not free-text names from daily HOS logs.
- All result pages are loaded before modifying the database. Failed requests abort
  the sync; missing/ambiguous active assignments clear the old local link.
- The model supports one driver and one trailer per truck. The latest active vehicle
  assignment wins; equal-time conflicts and multiple trailers are left unassigned.
- Newly imported entities participate immediately. Clearing and reassigning unique
  links are saved within one transaction, then the fleet metadata cache is invalidated.

API references:
- https://developers.samsara.com/reference/getdrivervehicleassignments
- https://developers.samsara.com/reference/getdrivertrailerassignments
