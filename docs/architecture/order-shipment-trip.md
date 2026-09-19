# Order, freight and run

Status: a proposal, written on 2026-09-19 at the owner's request, against the
code as it stands. It names what is already there, what is genuinely
conflated, and what two named people - the dispatcher and whoever does the
accounting - get out of separating them. The decisions at the end are the
owner's and are not assumed here.

## The three things already half exist

| Idea | Where it lives today |
| --- | --- |
| The run: who drives what, when | `Trip` -> `ExecutionLeg` |
| The freight and its stops | `Dispatch.Stops`, and `Shipment` for document data |
| The commercial order | fields on `Dispatch`: `OrderNumber`, `CustomerName`, `Price`, `Currency`, `LoadedMiles` |

`LoadExecutionLeg` links a load to a leg with a `Sequence` and the visit the
load starts and ends at. That link is the multi-broker trip: one leg, several
loads, each with its own stretch of it. The structure for what the owner
asked about is mostly present.

What is not separated is `Dispatch`. It carries the commercial order **and**
the execution: `TruckId`, `DriverId`, `TrailerId`, `TruckNumber`,
`TrailerNumber`, `Status`, `ExecutionLegId`, `ExecutionStatus`,
`AssignmentRevision`, `PlanningTruckId`, `PlanningAssignmentRevision` - and
`DispatchStop` carries `TruckId` and `TruckNumber` again, per stop.

## What the dispatcher pays for that

An earlier draft of this document said the truck was stored four times and
could disagree, and that the first change worth making was to collapse them.
That was wrong, and wrong in the direction that would have done damage, so
it is corrected here rather than quietly removed.

The fields are not copies of one fact. They are different facts with a
precedence rule applied the same way everywhere - `PlanningTruckId ?? TruckId`
in `DispatchProjection`, `GetDispatche`, `SetStopOperation`,
`UpdateDispatchWorkspace` and `SetTruckAssignment` alike:

| Field | What it means |
| --- | --- |
| `Dispatch.TruckId` | what the broker's system says |
| `Dispatch.PlanningTruckId` | what the dispatcher decided, overriding it |
| `DispatchStop.TruckId` | the provider's per-stop assignment |
| `ExecutionLeg.TruckId` | what is actually executing |

`TruckAssignmentTests.ConfirmationIsSeparateFromProviderAssignmentsAndSurvivesSync`
states the intent: a dispatcher's confirmation sets `PlanningTruckId` and
deliberately leaves `TruckId` and the stops alone, so a later import cannot
be mistaken for the decision and the decision cannot be mistaken for what
the broker sent. That is the owner's own rule - an imported value is a source
fact - applied to assignments.

Checked against the working database: twelve loads linked to live legs, no
disagreement between the load's truck and its leg's truck, and none between
the planning assignment and the leg.

So there is no duplication to subtract here. What is genuinely conflated in
`Dispatch` is narrower than the earlier draft claimed: the commercial fields
(`OrderNumber`, `CustomerName`, `Price`, `Currency`) sit on the same record
as the execution state (`Status`, `ExecutionLegId`, `ExecutionStatus`,
`AssignmentRevision`). That is one record with two lifetimes, which is a
real question, and it is a smaller one than "the truck is stored four
times".

## What accounting pays for that

Revenue arrives per order: the broker pays for a load. Cost arrives per run:
fuel, tolls and pay belong to a truck's stretch of road, not to any one
broker's freight. When one leg carries three brokers' loads, the leg's fuel
has to be divided among them.

Today that division is entered by hand. `ExpenseAttribution` splits an
expense across loads by weights a person supplies, with the remainder going
to the largest weight. It works, and it asks a human to invent a number
every time.

It does not have to be invented, because the miles are already attributed.
`Movement` is a physical travel interval for one truck, and
`MileageAllocation.Resolve` already picks at most one load to bear it, under
a stated policy with its own revision. Each load's share of a leg is
therefore a number the system already holds.

So the accounting shape is:

- revenue hangs on the order
- cost hangs on the leg
- the split between loads is *derived* from attributed miles, not entered
- a hand-entered split stays available for the cases mileage cannot answer,
  such as a lumper paid for one specific load

That last point matters: some costs belong to one load outright and must not
be spread by miles. The kind is what decides - the owner has already said
tolls and lumper need separate records rather than being folded into fuel.

## What I would not do

- Not merge `Dispatch`, `Shipment` and `Customer`. The owner has already
  ruled that out, and the reason holds: they have different owners and
  different lifetimes.
- Not rename anything as a first step. Renaming `Dispatch` to `Order` moves
  every reference in the codebase and changes nothing about who writes what.
- Not collapse the assignment fields. They are a precedence rule over three
  different facts, not one fact stored repeatedly, and flattening them would
  lose the distinction between what the broker sent and what the dispatcher
  decided.

There is no obvious first change. The assignment fields are not duplication
and must not be collapsed. What remains is the narrower question of one
record carrying a commercial lifetime and an execution lifetime, and whether
the accounting split should be derived from attributed miles - which is a
change to how costs are read, not to how loads are stored.

## Decisions that are the owner's

- Whether a load may ever be split across two legs, and what that means for
  the order. `LoadExecutionLeg` allows it structurally today.
- Whether an order can cover freight that travels on different days, which
  decides whether order and freight are one record or two.
- Which cost kinds are spread by miles and which belong to one load outright.
- Whether a historical hand-entered split is recomputed when the mileage
  policy changes, or frozen as it was decided. `MileageAllocationPolicy`
  already records the revision a movement was decided under, so either is
  possible and they give different books.
