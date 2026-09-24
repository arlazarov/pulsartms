# Driver groups

A dispatcher makes their own named groups of drivers ("West", "Local"),
fills and renames them, and removes them, in Personal settings. One
choice, a group or All drivers, holds for them on every page that lists
drivers' work, on every device, until they change it. The names are
examples; nothing is created for anyone. State on 2026-09-24: local only,
migration `AddDriverGroups` not applied anywhere.

## What a group is and is not

- A view, not a permission: it narrows only what the dispatcher may
  already see and never widens it. All drivers is everything their role
  shows, as before groups existed.
- The dispatcher's own: groups belong to one user in one company. Another
  dispatcher neither lists, changes, removes nor chooses them; names are
  unique per owner only. Groups may share drivers.
- Removing a group removes its membership rows only: no driver, message,
  load or history. Whoever had it chosen is back on All drivers (the
  choice is set to null by the database).
- Unread is not a group matter: each dispatcher's read markers, unread
  counts and notices stay their own whichever group they look at, and the
  navigation's unread count still counts every conversation.

## Where it applies

One owner, `Application/Features/DriverGroups`, keeps the groups and the
choice (`User.SelectedDriverGroupId`). Pages do not send the choice: the
server reads it once per request through `IDriverScope`, a neutral
contract in `Application/Interfaces` that the owner implements
(`DriverScopeReader`: two reads when a group is chosen, one when not). A
background job has no user and reads All.

Only a page's own list is narrowed, and only when its endpoint asks: the
query's `InChosenGroup`, set by the controller. The same query sent by
another owner inside the request is unscoped - a truck's position read
for its planning, the fleet preview kept once for everyone, a truck's
weather - so a personal filter never shapes shared tracking state or a
shared cache. Queries default to unscoped for that reason.

- Messages: conversations linked to one of the group's drivers. An
  unlinked conversation is under All.
- Dispatch board: trucks the group's drivers are on (the truck each is
  assigned to, and the trucks of their planned and active legs as driver
  or co-driver) and loads one of them drives. Applied after the shared
  board index, so the index stays one for everyone; enrichment asks the
  board with the page's query and follows it.
- Loads (active and completed): loads whose recorded driver, or a stop's
  driver or co-driver, is in the group, history included.
- Fleet Map: the same trucks, narrowed on a copy of the shared telemetry
  snapshot; the fleet's hours read likewise.

Pages whose lists are not drivers' work (configuration, users, customers)
are not narrowed. A driver moved to another truck is followed through
the truck and leg data at the next read; a conversation follows its
linked driver.

## Screens

`Shared/DriverGroups/DriverGroupPicker` is the switch on Messages,
Dispatch and Fleet Map: All drivers or one of the dispatcher's groups with
its size, and a link to manage them. Changing it saves the choice and the
page reads its list again (`Services/ChosenDriverGroup` announces it).
The picker reads the choice from the server each time a page shows it, so
a choice made in another tab shows on the next page. Personal settings
list the groups with Edit and Remove (confirmed); the editor names a
group and checks its drivers, and a refused save keeps the draft.

## Audit coverage

The rules are held structurally, not detected after the fact: every
group read and write is filtered by its owner; the choice can be set only
to one's own group and is nulled when the group goes; membership rows go
with their group or their driver. There is no stored state a runtime
check could find wrong that these constraints allow, so no auditor rule
was added.

## Tests

`Server.Tests/Fleet/DriverGroupTests` (groups and choice are their
owner's, trucks through assignment and live legs, removal keeps drivers
and falls back to All, an edit at an old revision changes nothing,
naming and company drivers only), `Server.Tests/Messaging/
DriverGroupInboxTests` (the list narrows, the notice, an unflagged read
and each dispatcher's reads do not), `DispatchBoardIndexTests` (the page narrows, not the
index), `CompletedDispatchReadTests` (a group's loads, history included; an
unflagged read lists every load),
`FleetTelemetryTests` (a copy is narrowed for the page, the snapshot
and an internal read are not), `TruckRoutePreviewTests` (a dispatcher's
group never narrows the shared fleet preview),
`Client.Tests/Fleet/DriverGroupComponentTests` (choosing saves and the
page reads again, making a group, a refusal keeps the draft, removal
after confirming).
