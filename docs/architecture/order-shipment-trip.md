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

"Which truck is on this load" has several possible answers today:
`Dispatch.TruckId`, `Dispatch.PlanningTruckId`, the leg's `TruckId`, and the
truck named on each stop. Assigning a truck therefore writes to several
places that must agree afterwards, which is why `SetTruckAssignment` spans
Dispatch and Execution inside one transaction, and why the assignment carries
a revision of its own.

Anything that reads "the truck" has to know which of those answers is the
real one. That is the headache: not that the data is missing, but that the
same fact is stored four times and can disagree.

The fix is not new tables. It is that the leg owns the assignment and the
load stops carrying it - the load says which leg it is on, and the leg says
who is driving. One writer, one answer to the question.

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
- Not introduce an `Order` table before the duplication is gone. While the
  truck is stored in four places, a new table is a fifth.

The first change worth making is subtraction, not addition: one owner for
the assignment. Everything else is easier afterwards and most of it may turn
out to be unnecessary.

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
