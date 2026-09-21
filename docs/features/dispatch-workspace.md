# Dispatch load workspace

`/dispatch/{id}` is the full-page editor for an existing commercial load.
Cards, Table, Papers and Fleet Map links open this same page. An optional
`stopId` query selects the exact visit. Opening a load does not create or
complete work.

Operators can also start at `/dispatch/new` through **New load**. This creates a
PulsR-owned load without importing it first, preserves uncertain-save retry
identity, and opens the saved workspace for assignments. All active Dispatch views
include planned/unassigned loads. See [optional imports](dispatch-import.md).

Source-assignment review keeps unfinished overdue work visible in the active
board. It does not bypass completion or cancellation checks. Review notices
remain on the load without returning finished work to the truck's queue.
Conflicting actual visit order remains visible for review rather than proving
completion. The Completed list includes recorded final deliveries even when an
import retains a non-completed header status, unless the delivery was reopened.

## Shipment preparation

The load header links to its ordinary Shipments editor. Cargo and party drafts
are independent of load/stop edits and contain no border requirements.
[Border preparation](border-preparation.md) separately selects shipment versions
from one or several loads and owns PARS/PAPS and crossing details.

## Page and save boundaries

The tabs are Broker & billing, Overview and History; Overview opens by default.
Overview places the ordered route and selected stop editor beside Notes,
a document drop zone and load instructions. Native assignment, transfer,
and actual-stop controls remain below the editor in Overview. Recorded mileage
has its own sidebar section. Add stop acts on the selected visit and offers
Pickup, Delivery, Drop / Hook trailer and Switch truck / driver. Transfers open
the existing native planner in context; there is no permanent Manage Switch
panel. Ordinary additions cannot fall back to another editable stop when the
selected stop is locked. A contextual transfer retains an exact split visit
and still requires an explicit location and confirmation.
Broker & billing groups broker contacts, customer, load rate, currency and
saved financial summaries in its own section. Switching sections must not
discard drafts, remount editors or trigger provider calls.

Selecting a stop updates one editor below the entire ordered list. It never
inserts details between rows or collapses the selected editor. A stable page
scrollbar gutter prevents width changes as the editor changes. Assignment and
repeat-visit context stay visible beside the stop summary. Each row names the
truck, trailer and driver; missing resources show a dash rather than an inferred
assignment. Long names wrap without losing the resource identity.
Recorded and transfer stops show compact read-only facts instead of disabled
edit forms. Address and
contact editors use native disclosure; appointment windows, references and
instructions stay visible. New visits open their location fields for entry.

One **Save changes** commits load metadata and future-stop edits.
**Discard** and **Undo** restore the opened snapshot. A conflict retains the
draft and offers an explicit reload; uncertain responses retry the same
payload and request identity. Unsaved load, note and file drafts guard
navigation. Client cancellation and identity checks reject late responses
after leaving or changing loads.

Post-save reads enrich financial and ETA projections without replacing the
saved stop editor or its time drafts. A changed revision requires an
explicit reload; a late read must not replace new input.

**Add note**, automatic file uploads and assignment confirmations are
independent writes with their own statuses and retry identities. Load Save
never sends a note,
uploads a file, completes a stop or confirms a Switch. Instructions and
internal notes are not automatically delivered to a driver or broker. Copy
load link copies a URL, not a public access grant; normal sign-in and
authorization still apply.

## Future stops and assignment boundaries

Future ordinary visits can be edited, added, removed and reordered within
their eligible segment. Drag handles and native move buttons perform the
same draft operation. Stable stop IDs, not array indexes, identify
selections and writes. Completed or arrived visits, transfer boundaries, and
recorded mileage decisions are protected. A stop cannot move across an
assignment or historical boundary. Existing pickup/delivery semantics cannot
be changed through general editing.

Appointment mode is At, Window or Not scheduled. Windows retain independent
start and end dates and times. Times display in 12-hour format. A selected
time zone is stored with the appointment; invalid daylight-saving gap times
are rejected. Missing source time-zone information is not silently guessed
by the browser. PU/DEL reference, appointment reference and order number
stay separate.

Verify address is an explicit provider-backed read outside the save
transaction. Changing address fields clears old coordinates. Saving requires
valid coordinates and normal server revision checks. No geocoding occurs
during rendering or typing.

Truck, trailer and driver snapshots describe each stop's execution context.
Changing resources opens the existing native Switch workflow. It does not
replace historical resource IDs by editing display labels. Planned changes,
independent Drop and Hook events, and actual confirmations retain their
existing semantics. Execution writes are unavailable while the load editor
has unsaved changes.

## Native ownership and synchronization

### Corrections to recorded stops

Ordinary stop detail editing is independent of removal and reordering permission.
On an editable load, a starting or recorded pickup may correct its details while
retaining its protected position and identity. Single-load accepted legs retain
actual events, driver overrides and transfer confirmations when details change.
Recorded mileage still protects changes to the accepted path. Shared operations,
transfer boundaries, cancelled/completed load workspaces and dedicated operations
retain their existing editing restrictions. Ordinary stop fields stay visible in
full, disabled when editing is unavailable, with the restriction explained.


The selected ordinary stop directly exposes status, truck, trailer, driver and
co-driver, including on completed loads. The page header owns Save and Discard;
resource changes reveal the assignment scope and affected stop numbers.
Status and driver drafts belong to the page and survive selecting other stops.
The stop list previews unsaved drivers and completion. Save processes the drafts in their last-edit order;
later overlapping scopes take precedence. Each confirmed correction advances the
opened revision for the next request. A failed request stops the sequence, retains
remaining drafts and retries an unconfirmed write with the same identity. Earlier
confirmed corrections remain saved; this is not an atomic multi-stop transaction.
Discard removes remaining drafts and reloads if a write was unconfirmed. Equipment
and structural edits remain separate from pending status and driver drafts.
Each correction is an independent confirmed write with automatic actor and
before/after history, without a required reason,
the opened workspace/source version and an actor-bound retry identity. It stores
before/after workspace snapshots in the change history. Cancelled loads and shared
legs retain their coordinated workflows. Drop/Hook resource fields are directly
editable on the selected stop. Truck and driver corrections affect that side;
a trailer correction updates both linked legs and their custody record in one
transaction, preserving confirmations, actors and actual times. Connected further
transfers and overlapping trailer custody are rejected rather than partially
rewritten. These resource writes do not reopen or confirm actual transfer events.

Status can be set to Completed or Not completed without entering a time. The API
retains its optional actual-time field for compatibility; the compact editor does
not invent an actual timestamp. A nullable native completion override takes precedence over source actuals
without erasing them. Source imports cannot silently restore a corrected Completed
flag. Reopening cargo work does not undo a confirmed transfer or reopen its released
leg. Reopening the final completed leg checks for an active resource conflict.

Resource corrections replace exact master IDs, not display names. A native load
supports this stop, current assignment, all assignments, onward and an explicit
stop range. Driver and co-driver corrections apply to exactly those stops,
including completed stops and transfer visits. They persist as accepted stop
assignments; truck/trailer execution, stop identity, actual times and custody
remain unchanged. Consecutive stops with the same drivers form a displayed
assignment section. No Switch or additional transfer confirmation is required.
The ordinary editor defaults to this stop; use onward or range to edit a section.
Truck/trailer corrections still require whole existing execution legs or the
coordinated transfer workflow. Shared-load execution requires coordinated edits.

Accepted stop overrides retain an explicit marker so clearing a driver does not
fall back to the leg's default. Source replay preserves those assignments.
Current work uses the next incomplete stop's driver; planned mileage uses the
driver departing its origin stop. Previously recorded mileage remains evidence
and is flagged for review rather than relabeled. Mixed remaining drivers keep
the existing conservative ETA availability rules; this change does not implement
future driver settlements or invent another driver's HOS.

Only resource fields explicitly changed by the editor propagate; older clients
without a field mask retain the original all-resource behavior. A truck change
also confirms its planning truck. Changing resources alone retains completion;
completing a stop also completes earlier unfinished ordinary visits in the
workspace order, across accepted legs, in that same transaction. Existing actual
facts remain unchanged; inferred earlier completions have no actual time. An
unconfirmed transfer or a shared assignment blocks the correction and requires
its dedicated workflow. Reopening affects only the selected stop. Existing trailer
custody cannot be changed through a single-leg correction. Recorded mileage
evidence retains its original equipment and requires review after assignment
correction; planned movement identities are superseded. Pay is not recalculated.

These fields require the additive `AddStopCorrections` migration before the new
API is started. UI preview fixtures do not apply migrations or change live data.

The workspace is additive to the existing Dispatch identity. Metadata and
audit records are native, not additions to a TorqueAI payload. Explicit
commercial overrides survive import. The first operational stop edit retains
a source baseline and establishes local ownership of the itinerary. Later
imports cannot silently add, remove, reorder or overwrite those local
visits.

Exact, unchanged source identities can supply missing compatible actuals.
Changed source visits, assignments or contradictory actuals produce a scoped
review notice. They do not get attached to a different local visit. Native
execution snapshots, revisions, planning invalidation and planned-mileage
supersession are updated transactionally when eligible future work changes.

For existing execution legs, source reconciliation, explicit source acceptance
and the editor share ExecutionAcceptance. It checks revisions and protected
movement paths before replacing accepted stops. A changed interval retires its
automatic planned mileage; an unchanged adjacent interval is retained. Arrival
or appointment updates alone do not change the physical path. Missing movement
anchors cannot establish that recorded evidence belongs to an unaffected path.
The reviewed source fingerprint includes header assignments and unresolved
resource names, so those changes invalidate an already opened source review.

The save checks opened workspace, source and execution revisions, uses a
database transaction and actor-bound idempotency, and retains audit
snapshots. Financial values continue to use server-owned calculations.
Displayed saved forecasts must not be presented as forecasts for unsaved
reordered or edited stops.

Concurrent editors do not use last-write-wins. A later save against an old
revision returns a conflict and retains the browser draft. The database
concurrency token, unique revision/receipt indexes and serializable transaction
protect separate server instances as well as the same-process import gate.
Note and document writes have independent identities and do not replace a load
draft. A refresh or navigation must not discard unsaved input implicitly.

TorqueAI remains a scheduled source through the existing adapter; opening the
workspace does not call it. The `Imported` timestamp describes the last applied
or reconciled import, not a provider modification time or proof of the latest
worker check. Synchronization health remains in the existing status endpoint.
Commercial fields that have no local override continue following the source;
rate and currency move together. Matched-stop instructions, reference, cargo
and temperature groups also follow the source unless locally overridden.
Appointment dates, times, window and time zone form one override group. An
unrelated local edit does not freeze an imported appointment. Their
baseline advances after reconciliation so repeated imports remain stable.

Local itinerary ownership still protects stop order, assignment and location.
Active/planned native sections refresh appointments of the same ordinary source
visit during synchronization, using the locally protected effective schedule.
The section revision and existing planning outbox invalidate dependent forecasts.
Native Drop/Hook visits and completed sections retain their recorded snapshots.
Conflicting structural updates require explicit review; this release
does not add automatic reconciliation of a locally reordered itinerary.
Contradictory recorded actuals are retained and reported, never replaced.

An explicit native transfer appears as separate **Trailer drop** and **Trailer
hook** stops with outgoing/incoming resources. Resource-only handoffs remain
distinct. Confirmation comes from recorded events even when exact time is
unknown. Equal addresses, adjacent Pick Up labels and similar appointment times
do not establish or complete a transfer.

Workspace and load projections use the same ordered native visit identities.
Recorded transfer actuals enrich only the disconnected read response. An event
confirmed without a time remains completed with no invented timestamp; reads do
not change source rows, editable baselines or the source fingerprint.

## Broker directory and adjustments

Broker contacts and terms are always visible in Broker & billing. The directory
is also available from Customers & brokers in main navigation at `/customers`,
with explicit company search, creation and shared-profile editing. Its forms use
the same profile endpoint and billing fields as the load editor. Failed saves
retain the draft; navigation is guarded until save or discard. The directory
extends the existing Customer master rather than creating another provider-owned
identity. Search and creation reuse CustomerMatcher's letter/digit normalization:
CH Robinson and C.H. Robinson match. Similar legal names are not fuzzy-merged.
Renaming a master to a different normalized identity is rejected.

Search is explicit and bounded to 20 results. Selecting a profile copies contacts
and payment terms into the load draft; Save changes persists that snapshot.
Save as broker defaults is a separate shared-profile write with an opened
revision and database concurrency token. It never rewrites saved loads.
The profile stores regular and Quick Pay emails, payment days and cadence,
the date from which terms run, Quick Pay days, percentage, fee basis,
fixed fee/currency and free-text terms. These settings do not send bills,
deduct Quick Pay fees or initiate payment.

Load adjustments have stable IDs, broker/driver recipients, addition/deduction,
positive two-decimal amounts and reasons. Driver records retain an exact driver
ID and server-derived name snapshot, but do not calculate or pay wages.
Broker additions and deductions change the server-calculated saved invoice
amount, not the base rate or existing RPM definition. New adjustments capture
the load currency. A later source-currency change preserves recorded currencies
and makes a mixed-currency broker total unavailable until reviewed; it never
converts or relabels the amounts. Unknown rate remains unknown, not zero.
Workspace revisions,
actor-bound receipts and audit snapshots cover these records.

Migration `20260914204235_AddBrokerProfiles` adds Customer profile JSON and
revision columns. Apply it before starting the new API. Coordinate the API and
Client rollout: an older API does not support the new metadata fields.

## Notes

The editor presents one load-wide note field and Add note. It has no note-type,
driver or stop selector and no new-issue checkbox. New records use kind `note`
with no stop or driver link. Existing notes retain their recorded associations;
previously open issues remain visible and can be resolved without deleting text.
Author and recorded time are server-derived, never supplied by the browser.

Notes are distinct from driver instructions and automatic edit history. Saving
does not change operational status or create external notifications. Reads
are bounded and user-driven; the panel does not poll. Reload appears only for
errors/conflicts or returning from older notes, not as a permanent heading
action.
Raw notes are not logged.

## Documents

Authorized dispatchers can attach RC, BOL, POD and other documents,
including after load completion. Route editability does not control POD
upload permission. PDF, PNG and JPEG signatures are checked, each file is
limited to 5 MiB, and each load is limited to 50 attachments. Upload retry
identity includes the actor, load and content. Names are normalized for
download. Listing projects metadata only; file bytes are read only on an
explicit authenticated download.

Choosing or dropping files into Overview starts uploading immediately. A native
file input keeps mouse, touch and keyboard access. A batch reads and uploads one
file at a time; failures retain the current payload and identity for Retry and
pause the remaining queue. Discarding that queue does not delete files already
uploaded. In-progress and unconfirmed files continue guarding navigation.

Files are stored durably in the database in this initial implementation.
They are not cached in the browser or loaded with the route. Downloads use
an opaque attachment Blob rather than injecting document content into the
application. Signature checks are not malware scanning. External sharing,
email delivery, OCR, virus scanning, file deletion and a public document
portal are not included.

## Scope and rollout

This implements editing existing loads. New commercial-load creation,
completed history corrections, return-to-import reconciliation, customer
invoicing and driver payroll are separate workflows, not implied by the
Broker & billing section.

Migration `20260914153020_AddDispatchWorkspace` adds workspace/audit,
activity and document storage plus the appointment time-zone column. Source
implementation does not mean the migration is applied or a release is
deployed. Rollout needs explicit authorization and the normal full
verification gate. Tests must use isolated fixtures; application and
production databases are not test databases.

## Imported resource proposals

The Assignments tab compares accepted execution resources with the latest
normalized import proposal. The importer stores header and ordered visit
resources before applying local ownership rules. Its raw names and resolved
catalog identities remain available even when local stop or resource edits are
retained. Unmatched names are explicitly unresolved; the screen does not infer
that they identify an accepted resource.

This read-only comparison does not accept assignments or confirm transfer events.
The existing stop correction editor owns explicit assignment changes, and Switch
owns boundaries with separate release/receipt confirmations. Source observations
remain outside accepted execution history until an owning action accepts them.

## Historical document authors

Documents retain the uploader ID and name captured at upload. Listing and retry
responses do not depend on the current uploader account. Renaming, disabling or
deleting that account cannot hide its file or rewrite its historical label.
Current caller authorization and load-scoped downloads remain required. The
ActorName column is part of the pending clean core-storage migration.
