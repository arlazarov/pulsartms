# How mileage is attributed to a load today

Status: an observed record of the code on 2026-09-19. It exists because the
mileage attribution mechanism is the candidate basis for attributing other
costs, and that decision needs the mechanism described rather than assumed.
It proposes nothing: the questions at the end are open.

Read [operational mileage and load attribution][attribution] for the
behaviour this supports.

[attribution]: ../features/mileage-attribution.md

## What is attributed

A `Movement` is a physical travel interval for one truck. Attribution picks at
most one load to bear it, stored on the movement as `AllocatedDispatchId` with
`AllocationTarget`, `AllocationReason` and the `PolicyRevision` it was decided
under.

The movement carries three candidate loads: `PreviousDispatchId`,
`NextDispatchId` and `CarriedDispatchId`. A target names which of the three is
chosen, so attribution is a choice between neighbours in the itinerary.

## The rule

`MileageAllocation.Resolve` is a short ladder:

1. Cargo state `loaded` attributes the movement to the carried load.
2. Purpose `pickup-approach` attributes it to the next load.
3. Any other purpose - `yard-return`, `home`, `maintenance`, `reposition` -
   reads a per-purpose setting from the policy, each of which is `previous`,
   `next` or `unallocated`.

`ForTarget` then resolves the target to a load id. When the chosen target has
no load the result degrades to `unallocated` with the reason
`missing-{target}-load`, so an unattributed movement always records why.

`MileageAllocationPolicy` is a single row with its own `Revision` and four
settings, one per policy-driven purpose. A movement records the policy revision
it was decided under, so a later policy change does not silently rewrite the
reasoning behind an existing attribution.

## Corrections and history

`UpdateMovementAllocation` accepts an explicit target from an operator. It
refuses a target that has no load, except `unallocated`. An automatic
re-resolution keeps the operator's note appended to the derived reason.

Every change appends an immutable `MovementAllocationEvent` recording the
movement, its revision, the previous and new load, the target, the reason, the
policy revision, whether it was manual, and the actor and time. The events are
also read as evidence elsewhere: execution acceptance and switch cancellation
consult them before changing work that has attributed mileage.

Writes advance `Movement.Revision` and rely on optimistic concurrency; a
conflict returns 409 and asks for a reload rather than merging.

## What it does and does not express

It does express: one owner per movement, a derived default, an explicit manual
correction, a durable reason for every outcome including no attribution, and
immutable history tied to a policy version.

It does not express:

- **A share.** `AllocatedDispatchId` is one nullable id. A movement cannot be
  split between two loads, so a trip carrying loads for several brokers
  attributes each movement wholly to one of them.
- **An amount.** The quantity attributed is implicit - the movement's miles.
  Nothing in the mechanism carries a value, a currency or a unit.
- **A cost that is not travel.** There is no record of an expense; there is a
  movement that happens to have miles.

## Open questions

- Whether an expense and its attribution are one record or two. Mileage keeps
  them as one because the movement is both the event and the quantity; a fuel
  purchase, a toll and a settlement are not.
- Whether attribution must support a share. Mileage does not, and the trips
  that carry loads for several brokers are the case that would need it.
- Whether the purpose ladder generalises. It reads travel intent, which a toll
  or a fuel purchase does not have.
- Whether owner-operator pay is attribution at all, given that it may follow a
  contract rather than any distribution of miles or cost.
