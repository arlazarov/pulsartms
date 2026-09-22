# Shared route index, September 21

## Implemented boundary

RouteGeometry and FuelSearchGeometry now delegate to one Domain implementation,
RouteGeometryIndex. It retains original cumulative block miles, bounds the search
and refines original nearby segments. It does not substitute a simplified visual
line for calculation geometry.

Cached geometry captures coordinates in private value arrays instead of copying
both endpoints into an expanded record for every segment. The captured index
retains no mutable source list. Fuel search keeps its existing request-scoped
borrowed geometry contract. Small roads use blocks of at least 16 segments to
avoid a large bounding-box record for every small edge.

Display/fuel cache estimates use the captured representation. Fuel checks the
estimated budget before allocating the captured index. The old linear matcher
is retained only in test Support as an independent reference. Fuel equivalence
tests now compare against that reference, not another wrapper of the new index.

## Measured allocations

The curved two-leg fixture with 10,001 points per leg allocated 960,128 bytes
for the previous exact index and 410,576 bytes for the new exact index: about
57% less during construction, with all source points preserved. These figures
exclude construction of the input route and are not container-memory savings.

A synthetic 100-index straight-road fixture allocated 20,565,624 bytes for the
new exact representation. Its two-point display-only alternative allocated
60,024 bytes. The latter is not a production matcher for arbitrary roads.
Detailed allocation evidence is pinned in `diagnostic-lvN8j4`.

## Verification and remaining work

The affected Fuel/Routing dependency group passed 1,743 server tests, 234 Client
C# tests and 64 JavaScript architecture checks (`diagnostic-mpLfVE`). The initial
run identified a source-file size violation; construction and matching were
split without weakening the architecture rule. Detailed allocation tests passed.

Differential coverage includes curves, repeated legs, high latitudes, the date
line, zero-length legs, original cumulative progress, and independence from
later mutations of source lists. No production latency/capacity claim is made.

Sparse matching anchors, normalized immutable storage, movement history and
sustained end-to-end 100-truck testing remain separate subsequent steps in the
[fleet efficiency plan](../../architecture/fleet-efficiency.md). No schema
migration or deployment is included in this change.

The full suite subsequently passed 3,120 server tests, 1,052 Client C# tests and
625 JavaScript checks, without failures or skips (`diagnostic-wrKqR7`). The first
full attempt (`diagnostic-sa98uL`) had three PostgreSQL connection/query timeouts;
the unchanged retry passed. No test limits or database settings were changed to
obtain the pass. `git diff --check` also passed.
