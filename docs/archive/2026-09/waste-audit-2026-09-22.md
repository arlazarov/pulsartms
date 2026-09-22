# Where PulsR spends more than it needs — 2026-09-22

Audited on request: every path from a database write, through the caches, to
the local calculations. Everything below is measured unless it says otherwise.
One hypothesis in here was disproved by measurement and is kept in place,
because it is the reason the rest can be trusted.

## The number this whole audit hangs on

The working database, read directly:

| | |
|---|---|
| trucks | **4** |
| drivers | 9 |
| loads | 31, of which **8 active** |
| whole database | **54 MB** |

That fleet is served by a container that steadies at **456 MiB of RAM**, kills
itself on a 512 MiB limit, and answers its slowest endpoint in **3.2 seconds**.
Nothing below is a micro-optimisation. The system costs roughly ten times its
own data.

---

## 1. Memory: the GC is doing exactly what it was told, and nobody told it anything

### The mechanism, end to end

`Server/API/bin/.../API.runtimeconfig.json` contains one GC line, and no one
in this repository wrote it:

```json
"System.GC.Server": true
```

It comes from `Microsoft.NET.Sdk.Web`. There is **no GC configuration anywhere
in the project** — not in any `.csproj`, not in the Dockerfile, not in
`deploy-server.sh`, no `Directory.Build.props`. So every default applies, and
in a container the defaults are relative to the container limit:

| setting | default | on a 512 MiB container |
|---|---|---|
| `GCHeapHardLimitPercent` | 75 % | managed heap may reach **384 MiB** |
| `GCHighMemoryPercent` | 90 % | GC turns aggressive only at **461 MiB** |
| `GCConserveMemory` | 0 | no bias toward a small heap |
| `System.GC.Server` | true (SDK) | server heaps, not workstation |

Now the production measurement, sampled a minute at a time:

```
04:01  261 MiB    04:10  446 MiB    04:16  455 MiB
04:03  401 MiB    04:13  456 MiB    04:18  458 MiB
04:05  439 MiB    04:14  449 MiB    04:20  456 MiB
```

The process climbs and then flattens at **456–459 MiB**. That is not the
application finding its natural size. **That is `GCHighMemoryPercent` — 90 % of
512 MiB is 460.8 MiB.** The GC is holding the line precisely where it was told
to start caring, which leaves about 51 MiB for everything the GC does not
count: JIT, loaded assemblies, thread stacks, Kestrel and Npgsql buffers, the
ICU data. That is not enough, so the container crosses 512 MiB and is killed.

It has been killed **seven times**:

```
18:23  85dd8597      21:47  862d8158      22:44  411eb4e0
19:08  255ec64d      22:06  862d8158      04:30  8f044594  <- current revision
19:38  255ec64d
```

Note the last line. The revision deployed at 04:00 **also died**, at 04:30,
while this audit was being written. The route-chunk work moved the plateau from
~475 MiB to ~458 MiB — about 20 MiB, real but not enough, because the ceiling
was never the problem. The threshold was.

### What to do, cheapest first

1. **Raise the container to 1 GiB.** One flag in `deploy-server.sh`. The
   defaults become sane immediately: hard limit 768 MiB, aggressive threshold
   922 MiB, and the present 458 MiB working set sits at 45 % with real
   headroom. Cost is a few dollars a month. This should happen before anything
   else in this document, because right now production restarts under its own
   users.
2. **Then tell the GC the truth about the budget**, so the limit is a limit and
   not a target. In `API.csproj`:
   ```xml
   <PropertyGroup>
     <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>
   </PropertyGroup>
   <ItemGroup>
     <RuntimeHostConfigurationOption Include="System.GC.HighMemoryPercent" Value="70" />
     <RuntimeHostConfigurationOption Include="System.GC.ConserveMemory" Value="5" />
   </ItemGroup>
   ```
   `HighMemoryPercent=70` makes the GC start compacting at 70 % of whatever the
   container is, instead of 90 %. `ConserveMemory=5` biases every decision
   toward a smaller heap at the cost of more frequent collections — which this
   workload can easily afford, since it is 4 trucks and the CPU is idle.
   Microsoft's own advice for this knob is to start between 5 and 7.
3. **Leave Server GC and DATAS alone.** The folklore fix is
   `ServerGarbageCollection=false`. Do not apply it blind: DATAS has been on by
   default since .NET 9 and already shrinks server heaps toward the live data
   size. Change one thing at a time and read the plateau after each.

### The self-inflicted part of the footprint

Declared cache budgets, added up:

| cache | budget |
|---|---|
| `CacheBudgets.RouteIndexes` | 32 MiB |
| `CacheBudgets.Reads` | 16 MiB |
| `CacheBudgets.RouteDisplay` | 16 MiB |
| `SamsaraHosHistoryCache` | 16 MiB |
| `FleetLocationStream` | 16 MiB |
| `TelemetryFeedAccumulator` | 16 MiB |
| `CacheBudgets.Fuel` | 8 MiB |
| `CacheBudgets.TruckHistory` | 8 MiB |
| `PlanningSummaryCache` | 8 MiB |
| **total** | **136 MiB** |

**A quarter of the container is promised to caches, for four trucks.** These
are budgets, not measurements — but they are budgets the GC must plan around,
and any of them may legitimately fill. For a fleet this size every one of these
numbers should be divided by four. There is no scenario in which four trucks
need 32 MiB of route indexes.

And several stores have **no budget at all**:

- `EtaMemory.Results`, `.Viewed`, `.demandedInputs`, `.scopes` — plain
  `ConcurrentDictionary`. Bounded only by `Due()` forgetting anything not
  viewed for 10 minutes. The diagnostics endpoint says so out loud:
  `new("eta-current", Results.Count, null, null, "unmeasured")`.
- `FleetTelemetryCache.latest` and `ServerTelemetry.values` — one
  `ConcurrentDictionary<Guid, FleetLocationsResponse>` each, per company, never
  evicted.
- `SynchronizationState.Vehicles` — grows with every external id ever seen and
  is serialised into the database checkpoint.

### Two types that are the wrong shape

**`RoutePoint` is a class.**

```csharp
public sealed record RoutePoint(double Latitude, double Longitude);
```

On 64-bit that is 8 bytes of header + 8 of method table + 16 of payload = 32
bytes on the heap, plus 8 more for the reference in the enclosing
`List<RoutePoint>`. **40 bytes to carry 16 bytes of coordinates, as a separate
collectable object.** As `readonly record struct` it is 16 bytes, stored inline
in the array, invisible to the GC.

Route geometry is the largest data in this system. A cached long-haul route is
tens of thousands of these. At 48 MiB of route budget that is potentially over
a million tiny objects — precisely the allocation profile that makes gen0/gen1
expensive and fragments the heap.

The telling detail: `RouteGeometryIndex.cs`, the newest code in the repository,
already does it right — `private readonly record struct Coordinate(double
Latitude, double Longitude)`. The index stores coordinates as values; the thing
it indexes still stores them as objects.

**GPS readings are `decimal`.**

```csharp
public class VehicleLocationPoint
{
  public decimal Latitude { get; set; }   // 16 bytes each
  public decimal Longitude { get; set; }
  public decimal Speed { get; set; }
  public decimal Heading { get; set; }
}
```

`decimal` is 16 bytes and its arithmetic is software-emulated — roughly an
order of magnitude slower than `double`. It exists for money, where base-10
exactness is the whole point. A latitude needs nine significant digits;
`double` gives fifteen. There are **99 such declarations** across Domain and
Application for latitude, longitude, speed, heading, miles, gallons and metres.
`FleetLocationStream` retains up to 65,536 of these points.

This is a wide change, so it is not a weekend job. But it is the difference
between a geometry-heavy system that fits in 256 MiB and one that does not.

---

## 2. Speed: the slowest endpoint is slow for one reason, and it is bookkeeping

Production latency, from the request logs, grouped by path:

| path | n | p50 | max |
|---|---|---|---|
| `/api/dispatch/{id}/planning/fuel/reset` | 2 | 3.23 s | **3.23 s** |
| `/api/dispatch/{id}/planning/fuel/edit/preview` | 2 | 2.52 s | **2.52 s** |
| `/api/fleet/hos` | 66 | 0.03 s | 0.75 s |
| `/api/dispatch/truck/{id}/next-routes` | 9 | 0.24 s | 0.33 s |
| `/api/fleet/trucks/{id}/planning` | 35 | 0.00 s | 0.31 s |
| `/api/fleet/locations` | 99 | 0.00 s | 0.31 s |

The board is fine. Locations are fine. `next-routes` is now 0.24 s — it used to
be 1.2 s, so that work landed. **Fuel is the problem, and only fuel.**

### Where the 3.2 seconds goes

`FuelCheckedRouteSearch.cs:163` verifies each leg's detour with the routing
provider:

```csharp
foreach (var index in needed)
{
  ...
  var raw = await routing.CalculateAsync(requested, profile, ct);  // line 172
}
```

Sequential. Three or four legs, one after another. And each of those calls goes
through `TomTomRoutingProvider.Budget.cs`, which before the provider is even
contacted does this:

1. `ReservationGate.WaitAsync` — a **static `SemaphoreSlim(1, 1)`**, process-wide
2. `BeginTransactionAsync`
3. `SELECT pg_advisory_xact_lock(710246710)`
4. cached-result lookup
5. `CountAsync` — calls today
6. `CountAsync` — calls in the last minute
7. `SaveChangesAsync` — insert the reservation
8. `CommitAsync`

**Seven database round trips before a single byte is sent to TomTom**, and then
two or three more `SaveChangesAsync` on the way back. This project has already
measured its own database at **~66 ms per query regardless of what it asks**.
Ten round trips is **~660 ms of pure bookkeeping per routing call**, and every
one of them is inside a mutex that admits one caller at a time.

Three legs × (660 ms of accounting + ~400 ms of provider) ≈ **3.2 seconds.**
That is the measurement, explained exactly.

The irony is sharp: `TomTomRoutingProvider` declares
`ProviderSlots = new SemaphoreSlim(2, 2)` — it is *willing* to run two calls at
once. `ReservationGate(1, 1)` upstream guarantees it never will.

### What to do

1. **Collapse the reservation into one statement.** The two counts are one
   query with `COUNT(*) FILTER (WHERE ...)`. The whole reservation — check the
   cache, check both limits, insert the row — is a single
   `INSERT ... SELECT ... WHERE` returning whether it was accepted. Seven round
   trips become one. That alone takes a routing call from ~1 s to ~0.4 s.
2. **Drop the advisory lock while `--max-instances 1` stands.** It serialises
   across processes; there is one process. `ReservationGate` already covers the
   in-process case. Put it back the day a second instance runs — and note that
   `deploy-server.sh` already says a two-instance run has never been tried.
3. **Run the leg checks concurrently, bounded to 2**, matching `ProviderSlots`.
   Be honest about the trade: the budget is checked *before* the loop, so the
   number of paid calls is already fixed — the only new cost is when an early
   `Reject` would have skipped a later call. Bounded at 2, that is at most one
   extra call on a rejection path. Roughly halves the remainder.

Together: **3.2 s → well under 1 s**, without touching a single business rule.

---

## 3. A hypothesis I had, and the measurement that killed it

I expected to find the ETA's cost in `RouteRegionLookup`. It answers "which
country is this point in" by calling `GeoTimeZone.TimeZoneLookup` against a
3.4 MB embedded dataset, with no cache, allocating a record per call — and it
is called from inside nested loops in `EtaRouteTiming`, once every **two miles**
(`var count = (int)Math.Ceiling(leg.Miles / 2);`). A 2,500-mile run is 1,250
lookups, and the result is then collapsed into segments that, for a US-only
route, come to a single segment. Thousands of expensive questions to learn one
word.

So I measured it — Los Angeles to New York, sampled exactly the way the code
samples:

```
samples  : 1250 (one every 2 miles over 2500 mi)
elapsed  : 1.3 ms
per call : 1.0 us
allocated: 227 KiB
```

**Wrong.** GeoTimeZone uses a precomputed geohash index, not polygon
intersection. A microsecond a call. 1.3 ms per route compile is nothing, and
there is nothing to fix here.

I am leaving this in because it is the difference between this audit and a list
of plausible suspicions. The project's own rule — *remove round trips, never
guess which one is slow* — applies to CPU too. Section 2 is trustworthy because
section 3 exists.

---

## 4. The database: one table is 26 % of it, and 84 % of that is expired

Every table, by size:

| table | rows | size |
|---|---|---|
| **RoutingApiCalls** | **138** | **14 MB** |
| OdometerIntervals | 14,699 | 7.6 MB |
| DispatchBaseRoutes | 26 | 3.9 MB |
| DispatchRoutePlans | 16 | 3.2 MB |
| TruckFuelPlans | 4 | 2.1 MB |
| SynchronizationCheckpoints | 706 | 1.6 MB |

`RoutingApiCalls` is the largest table in the database at **138 rows**. Average
`ResultJson` is **421 KB** — full route geometry, serialised as JSON text. Of
those 138 rows, **116 are already expired**, and the oldest is from
2026-09-18. There is no pruning code anywhere: `grep` for a delete or prune on
that table returns nothing.

It is a cache that never evicts, storing the single largest payload in the
system, inside the transactional store, and it is read on the hot path by the
two `CountAsync` calls in section 2.

**Fix:** delete rows past `ExpiresAt` on the schedule that already exists —
`PlanningRefreshOperation` runs `store.PruneAsync(now.AddDays(-7), ct)` every
hour and is the natural home. Ten lines.

`SynchronizationCheckpoints` deserves a look too: **706 rows** in a table whose
shape (`Id`, `Owner`, `LeaseUntil`, `StateJson`) says "one row per background
job". The extra 702 are fuel stations — `FuelStationLookupStore` calls
`new CheckpointLeaseStore(db, Id(stationId))` with a GUID derived by hashing
the station id. It works, but a table named for worker checkpoints is being
used as a general key-value store with synthetic keys, and several of those
keys are not valid UUIDs. Worth naming before a third thing adopts the trick —
`GmailWatchStore` already did.

---

## 5. Why you have to click a truck before anything is calculated

Three separate mechanisms, and only two of them are the problem.

**Route and fuel planning is already eager, and already covers the whole
fleet.** `FleetSynchronizationOperation.PlanningLoopAsync` runs every
`PlanningSeconds = 30`, reads the whole dispatch board and takes
`MaxTrucksPerPlanningCycle = 10` trucks per cycle. You have **4 trucks**. Every
truck is planned, every 30 seconds, whether or not anyone is looking. That part
is not lazy at all.

**The ETA is demand-driven, deliberately.** `EtaMemory.View(id, now)` is called
when something reads a forecast; `EtaRefreshOperation` only recalculates ids in
`Viewed`; and `EtaMemory.Due()` does this:

```csharp
if (item.Value < now.AddMinutes(-10))
{
  Forget(item.Key);
  continue;
}
```

Ten minutes after you stop looking at a truck, its forecast is dropped and the
next look rebuilds it from nothing. That is the click.

**Next-load routes and deadheads are fetched for the selected truck only.**
`FleetMap.NextLoads.cs` requests `api/dispatch/truck/{id}/next-routes` keyed on
`SelectedDispatchId`. Nothing is fetched until a truck is selected.

### The honest trade, and why it now falls the other way

The laziness buys bounded memory: the ETA cache holds only what someone is
watching. That was a defensible choice when memory was the binding constraint —
which, per section 1, it is.

But the arithmetic has changed. **Eight active loads.** Eight forecasts. A
`DispatchEta` with its stops and hours is kilobytes, not megabytes. Warming
every active load costs on the order of a hundred kilobytes — against a 136 MiB
cache budget already promised elsewhere.

So: **have `EtaRefreshOperation` seed `Viewed` from the active dispatch board
instead of waiting to be looked at.** The board is already read every 30 seconds
by the planning loop; the ids are in hand. Raise the 10-minute forget window for
loads that are active. Both are small changes, and the map stops being cold.

Do section 1 first. Warming a cache inside a container that is already dying at
90 % of its limit will make things worse, not better.

---

## 6. Two things shipping in the production container that should not be

**`Microsoft.EntityFrameworkCore.InMemory` and
`Microsoft.EntityFrameworkCore.Sqlite`** are `PackageReference`s in
`Server/Infrastructure/Infrastructure.csproj` — the production project, not the
test project. The InMemory provider is documented by Microsoft as not for
production use. Both ship in the image and are loaded and JIT-compiled at
startup. (`Microsoft.Data.Sqlite` is genuinely used by
`IntegrationCredentialStore`; the EF providers appear not to be — worth
confirming before removing.)

**`AddDbContext`, not `AddDbContextPool`** (`DependencyInjection.cs:53`). Every
request constructs a fresh `AppDbContext` with its change tracker and internal
scope. Pooling is the standard fix and measurable. The caveat is real, though:
this context carries per-request state (`ServingCompany`, the `asked` flag), and
pooled contexts must reset it correctly. Worth doing, not worth doing carelessly.

---

## 7. On "ETA is over-complicated" and "fuel search cannot be that hard"

### The ETA — complicated because the domain is

About 4,500 lines excluding migrations. What it actually models: US and Canadian
hours-of-service, three cycle modes (`Observe`, `Recap`, `Restart`), regional
rulesets with a north-of-60 exclusion, alternatives offered when a cycle runs
short, chained forecasts across the next load, fuel stops folded into the drive
clock, and off-route recovery. That is not invented complexity — that is what
a legal drive-time forecast in two countries requires.

What *is* excess is how many places hold the same answer. One forecast lives in
`EtaMemory.Results`, `EtaMemory.Timing`, `EtaMemory.futureTimings`, **and** the
`DispatchEtaForecasts` table. Four stores, four invalidation rules, four chances
to disagree. That is where I would look first, and it is a much smaller job than
"simplify the ETA".

### The fuel search — the algorithm is right, the rules are in the wrong place

`FuelOptimizer.Optimize` is a dynamic program over (station index × whole
gallons) with Pareto dominance pruning. For minimum-cost fuel purchasing under
a tank constraint, that is the correct and standard algorithm. No complaint.

The problem is what is bolted inside its inner loop. Eleven separate `continue`
guards, each one a business rule expressed as **search pruning**:

- prefer filling here over a dearer optional stop
- overlapping alternative paths cannot both be detours
- fill to the limit before an uncertain poor-fuel destination
- do not skip the last substantially cheaper station when a top-up is reachable
- stations closer than 10 miles apart
- …and six more

A pruning rule removes a branch from the search. A cost term makes a branch
expensive. **They are not the same thing:** pruning can make the true optimum
unreachable, and pruning rules interact combinatorially — each new one can
resurrect a plan an older one was added to prevent. The evidence that this is
happening is in the file's own first line:

```csharp
public const int SelectionVersion = 32;
```

Thirty-two revisions of the selection logic, each invalidating every cached
plan. That is the signature of rules fighting each other.

**The direction:** move those eleven guards out of the loop and into
`FinalScore` as penalties. The DP keeps finding the true optimum; the rules
compose by addition instead of by exclusion; and a new rule becomes one term
rather than one more `continue` whose interaction with the other ten nobody can
predict. It is a real piece of work, and it is the difference between a search
that can be reasoned about and one that can only be patched.

12,451 lines across 30 rule files is a lot for "find cheap diesel along a
route". Most of that is not the optimiser — it is the machinery around it:
horizons, signatures, freshness, replay, redistribution, integrity, ranking.
Much of it exists to decide *whether to run the search again*, which is itself a
consequence of the search being expensive (section 2) and its answers being
unstable (`SelectionVersion = 32`). Fix those two and a lot of this scaffolding
loses its reason to exist.

---

## Order of work

1. **Container to 1 GiB.** One flag. Production is restarting under load right
   now. Everything else is optional until this is done.
2. **`HighMemoryPercent=70`, `ConserveMemory=5`.** Two lines. Read the plateau
   after, before changing anything else.
3. **Prune `RoutingApiCalls`.** Ten lines in a loop that already runs hourly.
   Reclaims most of the database and speeds up the hot-path counts.
4. **Collapse the routing reservation to one round trip**, drop the advisory
   lock while there is one instance, run the leg checks two at a time. 3.2 s →
   under 1 s.
5. **Divide the cache budgets by four.** They were written for a fleet that
   does not exist yet.
6. **Seed the ETA from the active board** so the map is warm. After step 1.
7. **`RoutePoint` to `readonly record struct`;** then GPS `decimal` → `double`.
   Large, mechanical, and the biggest single lever on geometry memory.
8. **Move the eleven fuel guards from pruning into cost.** The real engineering
   job in this list, and the one that stops `SelectionVersion` from reaching 40.

## Sources consulted

- [Memory management and patterns in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/memory?view=aspnetcore-10.0)
- [Garbage collector config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)
- [Running with Server GC in a Small Container — Hard Limit for the GC Heap](https://devblogs.microsoft.com/dotnet/running-with-server-gc-in-a-small-container-scenario-part-1-hard-limit-for-the-gc-heap/)
- [Preparing for the .NET 10 GC (DATAS)](https://devblogs.microsoft.com/dotnet/preparing-for-dotnet-10-gc/)
- [Optimize ASP.NET Core memory with DATAS](https://www.thinktecture.com/en/net/optimize-asp-net-core-memory-with-datas/)
- [Top 10 Ways to Reduce .NET Memory Usage in Kubernetes](https://ardalis.com/top-10-ways-to-reduce-net-memory-usage-in-kubernetes/)
