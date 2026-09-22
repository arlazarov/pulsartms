# What the fuel preview reads, and the one read it did not need — 2026-09-22

Three production previews of load 11006 after `77fe89b` gave a median handler
of 820.7 ms against 788.3 ms before, and a horizon of 361.1 ms against 299.5 ms.
Batching the horizon's saved rows was confirmed — two queries where there had
been four — but no speedup was. `fuel-edit/inputs` read 219.1, 966.8 and
218.0 ms across the three, which is spread, not a cause.

This traced `fuel-edit/inputs` to the statements it actually sends, counted the
whole preview, and fixed the one read that was provably not needed.

## What the inputs read costs

`IFuelWorkInputsReader.ReadFreshAsync` goes to `TruckItineraryReader` inside
`IExecutionReadScope`, which opens a repeatable-read transaction. Counted with
a command interceptor on the SQLite fixture:

| | statements |
|---|---|
| one `ReadFreshAsync` | **3**, plus its BEGIN and COMMIT |
| one preview, end to end | **16** before this change, **15** after |

The three are the work batch, an `EXISTS` check and the legacy dispatch load.
Transaction boundaries do not pass through a command interceptor and are not in
those counts.

**The preview reads its inputs once.** No statement in a preview was sent
twice, before or after. The production spread in `fuel-edit/inputs` is
therefore not duplication on this path; what it is remains unestablished.

## The read that was not needed

`ReadConnectionAsync` has two branches. When the predecessor has no execution
leg the connection is *captured* — `DeadheadService.CaptureRouteAsync`, which
does its own reads — and that branch never looks at the prefetch. The prefetch
collected a key for it anyway, so every preview whose predecessor was a legacy
load fetched a saved connection that nothing read.

`KeysToReadAhead` now follows the same predecessor chain the loop does, keying
a connection only where the branch that consults the prefetch will run. Base
routes are unaffected: both branches want them.

Statements per preview: **16 → 15**. On the fixture's legacy chain the batched
connection query disappears entirely.

## Considered and not done

Trimming the legacy dispatch load, which hydrates every column of the dispatch
and its stops while the projection uses about twenty of each. Measured against
the working database first: `SourceAddressJson` averages 129 bytes,
`Notes` 126, and `DispatchStops` is 256 kB for 78 rows. A truck's itinerary is
a handful of stops, so the unused columns are worth on the order of a kilobyte
per read. Reshaping a reader shared by several paths for that is complexity
bought with a hypothesis, so it was left alone and the measurement recorded
here instead.

## Commands and results

```sh
bash test.sh fuel
#   1,803 Server, 236 Client, 64 JavaScript — 0 failures
bash verify-release.sh
#   625 JavaScript, 1,054 Client, 3,216 Server; 297 assets, 11 graphs
```

The tests were checked against a revert of the fix: three fail, including the
preview count at `Expected: 15, Actual: 16`.

## Limits

The counts come from the SQLite fixture. Statement counts carry — the number of
commands EF sends does not depend on the provider here — but transaction
isolation and wall-clock cost do not, and no timing is claimed from them. The
fixture has one truck, a current load and one follower, all without execution
legs; a chain whose predecessors carry legs takes the other branch, which the
unit tests cover but the fixture does not exercise end to end.

Nothing here establishes a production improvement. One query of a few hundred
bytes was removed from a path whose handler measures in hundreds of
milliseconds; it should be read as removing work that was doing nothing, not as
an expected latency change.
