# Imported execution acceptance — September 17, 2026

## Scope

Ordinary provider-neutral imports now share initial execution acceptance with
explicit assignment. TorqueAI remains an optional Infrastructure adapter. The
checked-in provider setting still enables it; this work does not disable the
operator's live integration or deploy the local changes.

The import transaction stores source identity and observations, accepts an
unambiguous resource set, and commits the Trip, leg, typed visits, load link,
immutable revision and planning demand together. System actors are nullable;
recording time never becomes an invented assignment start. Restart replay does
not add another leg, history revision or planning request.

Matching actual visits can advance planned ordinary work to active and then
completed. All accepted visits must be completed before final delivery closes
it. Transfer receipt remains necessary for incoming work; cargo cannot confirm
release. Competing active candidates remain reviewable, and existing resource
or custody conflicts prevent activation. Invalid or reversed actual times are
retained as source evidence without becoming accepted execution facts.

Source assignment identity includes raw names, resolved catalog identities and
mixed-resource positions. A changed proposal cannot silently reassign accepted
work or supply new actuals to the old assignment. Uniform visit additions do
not manufacture resource changes. Local stop ownership cannot hide the raw
resource observation. The workspace fingerprint protects an open correction
from intervening source changes. Explicit correction acknowledges that observed
assignment signature while retaining the prior accepted history.

Unaccepted work carries a durable review reason. Even a source claiming
completion with contradictory times remains in the reviewable itinerary;
source completion cannot hide an unresolved assignment. It blocks authoritative
planning at that segment. DispatchSourceLinks therefore joins the publication
lock inventory, increasing it from fourteen to fifteen tables. The existing
cold-preview query-count regression remains unchanged.

Stop correction now prepares its changes and uses ExecutionStopAcceptance for
accepted-row persistence, revision, history and planning demand. Transfer
lifecycle writers and source compatibility mirrors remain separate migration
work; this report does not claim that all competing writers are removed.

## Persistence and verification

The nullable system actor and source-review fields were consolidated into the
existing unapplied migration `20260917055902_RebuildExecutionStorage`. No
previously applied migration was edited. The migration retains its clean-reset
and downgrade guards. Generated model metadata agrees with the current model.

Final checks for this slice passed:

- Full suite: 2,674 Server, 1,014 Client C# and 561 JavaScript tests (4,249 total).
  Evidence: `artifacts/managed/diagnostic-lxV9U6/tests.log`.
- Strict migration probe build: zero warnings and errors.
  Evidence: `artifacts/managed/diagnostic-K7XL3E/build.log`.
- Isolated PostgreSQL transition, identity/password/role preservation, import
  bootstrap/progress/replay, resource protection and unresolved completed-source
  itinerary checks passed. The fixture was removed.
  Evidence: `artifacts/managed/diagnostic-U3edGa/postgresql.log`.

Authenticated browser checks and production performance measurements were not
run for this server slice. The Client compiled as part of the full suite; no
Client source or visual behavior was changed.

The first PostgreSQL run exposed an outdated pre-migration fixture: changing the
current actor property to nullable removed its former Guid default, while the
old schema still required a value. The old fixture now explicitly seeds its
historical empty Guid. Production import acceptance stores null under the new
schema; it does not fabricate an actor to satisfy the old schema. The failed
fixture was removed, and the following isolated transition passed.

## Remaining rebuild gates

- Explicit resource-proposal comparison and boundaries for ambiguous imports.
- Complete transfer writer unification and removal of compatibility mirrors.
- Common standalone/historical planning inputs and removal of mutable
  Dispatch.ForExecution projections.
- Durable source-road demand, complete per-truck publication revisions and
  retirement of the conservative publication locks only after writer coverage.
- Final release verification, authenticated browser flows, protected backup,
  approved compatible deployment and working-database cutover.

Financial modules and actual expense integrations remain the separately
requested future extensions. Production performance and lock contention were
not measured. No working database migration, reset or deployment was performed.
