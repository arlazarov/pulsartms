# Costs, and separating the order from the work

Status: a decided specification, not a menu. It settles the questions left open
in [module ownership](module-ownership.md) so that tolls, actual costs, driver
pay and owner-operator settlements can be added through defined boundaries.
Deciding is not approval to deploy; each slice is released on its own terms.

It is written against two facts about the business. One trip may carry loads
for several brokers, so any attribution that cannot express a share is wrong
for the common case. And owner-operator pay may follow a contract rather than
any distribution of miles or cost, so pay is not a special case of attribution.

## Decision 1: an expense and its attribution are two records

[Mileage attribution](mileage-allocation.md) keeps them as one, because a
movement is both the event and the quantity. A fuel purchase, a toll and a
settlement are not: each has an amount that exists before anyone decides who
bears it, and the decision can change without the amount changing.

**Expense** records that money was spent. It owns the amount, the currency, the
moment, what incurred it - truck, driver, trailer - and where it came from,
including the provider's own identifiers and the names it supplied. Imported
names are kept as the source stated them and are matched to internal resources
separately, so a failed match never rewrites the source fact. An expense is not
a claim about any load.

**ExpenseAttribution** records that a load bears part of an expense. It names
the expense, the load and an amount in the expense's currency, with the basis
that produced it and whether a person chose it.

The consequences are the point:

- An expense may have several attributions, so one purchase can be divided
  between loads for different brokers.
- The attributed amounts must not exceed the expense. What is left is
  unattributed and stays visible as such; it is never implied by subtraction.
- An expense with no attribution is a complete, valid record.
- Re-attribution changes attributions and never the expense.

## Decision 2: the basis is recorded, the formula is not generalised

Each attribution records the basis that produced its amount: `loaded-miles`,
`equal-share`, `manual`, or `contract`. The basis is a fact about how the
number was reached, kept so a later reader can tell a derived share from a
chosen one.

The mileage purpose ladder is not extended to costs. It reads travel intent,
which a fuel purchase and a toll do not have. Mileage attribution keeps its own
mechanism unchanged; the two answer different questions and may legitimately
disagree about the same trip.

## Decision 3: rounding belongs to the operation

There is no single money type with one rounding rule. A unit fuel price and a
final payout need different precision, so each is stored with the precision its
operation requires, and any conversion states its rate and the moment the rate
applied. Amounts are only ever compared or summed within one currency.

## Decision 4: pay is a settlement, not an attribution

Driver and owner-operator compensation is calculated from a contract and is
recorded as its own versioned result. It may read attributed costs as an input,
but it is not a share of them, and attribution must not be bent to produce it.
This specification does not define settlements beyond that boundary.

## Decision 5: the commercial order becomes its own record

Today `Dispatch` carries four things at once: the commercial order, a snapshot
of the source, the resource assignment, and a projection of execution. The
freight already has a home in `Shipment`, and the work already has one in
`ExecutionLeg` with `LoadExecutionLeg` linking the carried portion.

The missing owner is the commercial one. **Order** owns the customer or broker,
the agreed charge and the billing dates. A shipment belongs to exactly one
order; an order may cover more than one shipment. Nothing about a truck, a
driver or a leg belongs to it.

This is the invasive part and it is sequenced last. Costs and attribution do
not depend on it: an attribution names the load it is borne by, and that
identity survives the split.

## Order of work

1. Expense and attribution as new tables. Nothing existing changes, so this
   carries no risk to current operations.
2. The attribution rule, the sum invariant and immutable history, with the
   corrections path.
3. Reading attributed cost per load, alongside attributed mileage.
4. Settlements, once pay contracts are defined.
5. Order extracted from `Dispatch`, as a separately reviewed migration.

Each step is additive until step 5. Any step that changes transactions or
concurrent behaviour is verified against a real PostgreSQL fixture before it is
released, per [test selection](../testing.md).
