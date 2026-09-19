# Optional load imports

TorqueAI is a temporary Infrastructure adapter. Application owns native load
creation, validation, assignments, stop completion, workspace edits and history.
The core receives neutral `ExternalDispatch` facts through `IDispatchProvider`;
it does not reference Torque types, credentials or response formats.

## Configuration

`DispatchImport:Provider` selects the adapter. An empty or missing value
disables
load imports. The checked-in API configuration explicitly selects `torqueai` to
retain the existing integration until the operator chooses to disconnect it.
Set `DispatchImport__Provider` to an empty string and restart the API to disable
it. Unsupported nonempty providers fail during composition.

When disabled, Infrastructure registers neither the load provider nor its HTTP
client. Fleet synchronization omits the dispatch loop and hides that job's old
checkpoint status. Catalog, assignment, telemetry and planning schedules retain
their own settings. An explicit sync request returns a disabled response without
resolving an adapter. `Synchronization:Enabled` remains the overall scheduler
switch; it is not the control for disconnecting only TorqueAI.

Disabling imports does not delete existing loads, source links or credentials.
Source names and timestamps describe saved provenance, not a live connection.
Replacing an adapter requires implementing the Application contract and
selecting
its registration in Infrastructure. No financial or execution formula belongs in
an import adapter.

## Identity and ownership

`Dispatch.Id` is PulsR's permanent load identity. `DispatchSourceLinks` maps the
pair `(Provider, ExternalId)` to that identity and retains the source display
name. Each load currently has at most one authoritative import source. Another
provider cannot update that load merely by reporting the same external ID.
Multiple-source reconciliation is not inferred or implemented.

`LoadNumber` is a unique PulsR display number, not an import lookup key. New
imports may retain an available source display number. A collision receives a
new PulsR number; it never attaches to an existing native load. Later changes to
an external display number do not rename the existing load when the external ID
is stable. The current Torque response exposes only its load number as an
identity; conversion of that number into an external key stays in the adapter.
Renumbering that source identity requires explicit reconciliation, not a guess.

Native creation and imports share a transactional number counter. Serializable
transactions, a counter concurrency token and the unique load-number index
reject concurrent collisions. Clients retry the same creation identity after a
conflict or uncertain result. The server retains the original response in the
workspace revision receipt and rejects reuse with another payload or actor.

Missing or duplicate external IDs reject the batch before writes. Import replay
uses persisted source links, including after a process restart. Existing local
commercial/stop ownership, completion corrections and accepted execution guards
still apply to updates of linked imported loads.

## Native workflow

Dispatch's **New load** action opens `/dispatch/new`. Operators create pickup
and
delivery stops through the existing address and appointment editor, then assign
resources in the saved load workspace. Creation uses `POST /api/dispatch` and
has
no provider dependency. A draft address lookup uses the authenticated
`POST /api/dispatch/verify-address` endpoint and the existing geocoder contract.
Looking up an address does not create a load.

All active Dispatch views include planned/unassigned work, so newly created
loads
remain discoverable before assignment. Completion of a `Delivery` uses the same
active-work exclusion as `Drop Off`. Native creation, assignment and completion
are covered together without an import adapter.

The first explicit ordinary resource assignment now creates accepted
execution: a Trip, ExecutionLeg, ordered typed visits and a load link. The
same transaction records its actor, immutable revision and durable planning
request. Retrying an editor correction returns its saved response without
creating another assignment. Truck-start confirmation uses this owner when the
complete itinerary has one resolved resource set. Existing accepted execution
must be changed through the correction editor; clearing the old truck override
cannot remove it.

Different resources across visits, driver travel before truck pickup,
unresolved resource names and invalid actual times remain reviewable source
facts. They are not collapsed into an invented single assignment. Assignment
alone does not assert that driving started. Explicit completion advances
ordinary execution and can close delivery with an unknown actual time.
Completion and operation commands update accepted stops, history and planning
demand together. Transfer release and receipt remain separate actions.

## Automatic execution acceptance

An ordinary import with one resolved, active resource set across its visits uses
`InitialExecutionAssignment`, the same owner as explicit assignment. It commits
its Trip, leg, ordered visits, load link, immutable revision and planning request
in the source synchronization transaction. System acceptance has no invented user
or actual start time. Replay after a fresh process retains the same accepted work.

Assigned/planned work stays planned until actual visit evidence arrives. Matching
pickup/arrival facts activate ordinary work; completion requires every visit and
a final delivery. An incoming transfer still requires receipt, and cargo events
cannot release an outgoing transfer. Two proposed active assignments sharing a
truck, crew member or trailer both require review instead of using batch order to
choose a winner. Missing/inactive resources, driver travel, contradictory header
assignments and unsupported source status remain reviewable source facts.

`DispatchSourceLinks.AssignmentSignature` retains the observed resource proposal,
including raw names and resolved catalog identities. It is captured before local
workspace overrides. The accepted leg retains its own source assignment signature.
A changed, resolved ordinary proposal automatically replaces the accepted truck,
trailer and crew, including local resource overrides. Prior accepted revisions
remain immutable; the new revision invalidates open editors and calculations.
Both the previous and current truck receive durable planning invalidation, and
obsolete planned mileage is retired without changing recorded movement evidence.
Unchanged replay does not create another revision. Shared loads and transfer
boundaries are not collapsed into one assignment; unresolved, inactive or
contradictory resources remain visible data problems.

Unaccepted source work remains visible with its reason, and its itinerary segment
blocks authoritative planning. Base-road preparation does not establish resource
acceptance. Ambiguous multi-resource imports still need explicit boundaries; this
comparison does not infer transfers. The Assignments tab shows the latest source
header/visit proposal alongside accepted execution, including unmatched names.
DispatchSourceLinks.AssignmentProposalJson retains these normalized observations
before local ownership rules are applied. The resource fingerprint remains
independent of descriptive visit labels.

A review warning is an observation, not an accepted execution revision. Recording
or clearing only the warning retains the accepted revision and immutable history,
while requesting planning revalidation once. Actual accepted visit/resource changes
still create a new revision, history record and planning request together.

## Migration boundary

The source-link and counter tables are included in the pending clean-transition
migration `20260917055902_RebuildExecutionStorage`. Up refuses populated old
load
or execution storage before any schema changes. The existing approved
operational
reset must precede that migration; users and protected identity/configuration
remain retained. No source links are guessed from old load numbers.

Down refuses accepted execution, pending planning or new load/number/source-link
state. Use forward repair after new work begins. Local isolated PostgreSQL
verification is evidence of the fixture only, not a working-database migration.

This separation does not complete the broader
[core rebuild](../architecture/core-rebuild.md): explicit boundaries for ambiguous
assignments and calculation adapter removal remain exit conditions. Financial
modules and actual expense imports remain future work.

## Explicit resource boundaries

An operator can plan a switch for assigned or unassigned source work using exact
boundary visits and active catalog resources. Both accepted legs remain planned
until actual work or an explicit confirmation changes their state. An outgoing
source pickup can activate the first leg; only receipt can activate the incoming
leg. Resource availability is rechecked at activation. A future plan may reuse a
currently busy truck without making both assignments active.

Source work with a driver-only prefix cannot be silently treated as truck travel.
Confirm its truck start through the existing correction workflow first. Transfer
preview and acceptance share the same source eligibility rules. Repeating a
successful plan retains the same legs and accepted history.
