# Fuel optimizer allocation reduction

## Evidence and change

A 60-second EventPipe GC allocation trace on the isolated 50-truck fixture
covered three planning reads and one fuel/ETA calculation. Approximately
4.45 GB of allocation ticks were recorded. Leading types included optimizer
sorting iterators (824 MB), fuel stops (433 MB), caching comparers (301 MB),
enumerators (251 MB) and state arrays (244 MB). Inlined stacks resolved to
`FuelRouteSearch.Chains`; source inspection located repeated ranking and
purchase-prefix construction in `FuelOptimizer`.

The Domain optimizer now retains its four existing ranked winners in one
stable scan. It preserves fewest-stop priority, cash cost, terminal fuel value,
range ranking, and original tie order. Dominance checks use a direct loop.
Destinations reached after the same purchase share its purchase prefix during
search. Lists are never extended in place after publication to a state;
subsequent purchases copy their prefix. Result numbering still occurs only
after winner selection. Search limits and financial formulas are unchanged.
The selection version therefore remains unchanged.

## Regression evidence

The new fractional-balance fixture allocated 145,747,640 bytes with the original
compiled Domain assembly, 38,753,240 after ranking changes alone, and 10,242,952
after sharing purchase prefixes and removing dominance closures: approximately
93% less than the original. Both original and revised assemblies selected
Station 3, purchased 180 gallons, cost $560 and arrived with
25.8805970149254 gallons. The regression checks those results and a 20 MB
allocation ceiling. The old assembly fails that ceiling.

Full verification passed: 3,177 server tests, 1,052 Client C# tests and 625
JavaScript tests (4,854 total), including financial/tie/arrival-policy coverage
and architecture checks. JavaScript type checking, Release probe publishing,
CSharpier and diff checks passed. No application migration or deployment was
performed. The actual local Client displayed the recalculated route and ETA.

## Scope and limitations

The paired API comparison restarted each build on the same prepared fixture,
with identical telemetry and three planning reads followed by fuel/ETA for the
first truck. There were no concurrent builds, tests or bulk load driver.

| Measurement | Original | Revised |
| --- | ---: | ---: |
| Cold sequence allocation bytes | 4,860,625,016 | 787,652,008 |
| Warm sequence allocation bytes | 4,532,355,296 | 575,005,752 |
| Cold fuel/ETA seconds | 18.04 | 11.35 |
| Warm fuel/ETA seconds | 16.93 | 9.71 |
| Warm full sequence seconds | 18.50 | 12.86 |

Warm allocation traffic fell by approximately 87%; the warm fuel/ETA request
was approximately 43% faster in this single paired run. Both returned two fuel
stops and two populated forecasts with the same feasibility status. Ordinary
planning-read timings did not establish a consistent improvement. Timing is
illustrative rather than a statistically established production speedup.

Allocation traffic is not retained memory or container RSS. This change does
not establish a new 50-truck container peak, eliminate all planning latency,
or prove production capacity. The test runtime uses Linux x64 emulation on
an ARM Mac, workstation GC, one CPU, synthetic routing/HOS and a remote
isolated PostgreSQL fixture. No 100-truck run was started.

Raw evidence lives in managed local diagnostic runs:

- `diagnostic-x62HqR`: original allocation trace.
- `diagnostic-2mwci8`: allocation type and stack aggregate.
- `diagnostic-sWU4P2`: original-assembly allocation and result regression.
- `diagnostic-hciG5X`: optimized allocation and result regression.
- `diagnostic-0c5fOn`: full test output.
- `diagnostic-TxsQa3`: paired API comparison on the saved 50-truck fixture.
- `diagnostic-Xvxrp8`: optimized local executable.

All paths above are under `artifacts/managed`. Raw traces remain local.
