# RoutePoint as a class or a value — measurements, 2026-09-22

Taken with `tools/RoutePointShapeProbe` against three saved plans copied out of
the working database. The probe's README holds the method and the limits; this
file holds what it read and what follows.

## Conditions

.NET 10.0.12, Arm64, Release, workstation non-concurrent GC. Three plans of
5,973 / 9,917 / 12,612 points. Both shapes decode identical JSON bytes; the
decoded coordinates were compared point by point and agreed in every case.
Both shapes warmed 350 times, then measured in alternating rounds — 7 rounds of
20 iterations, median reported.

## What was read

Bytes allocated per operation, class → value:

| scenario | 5,973 pts | 9,917 pts | 12,612 pts | change |
|---|---|---|---|---|
| A cold load, full geometry | 912 KB → 389 KB | 1.61 MB → 843 KB | 1.98 MB → 929 KB | **−48…−57 %** |
| B display, simplified | 339 KB → 168 KB | 615 KB → 261 KB | 830 KB → 428 KB | **−49…−58 %** |
| C metadata only | 955 B → 955 B | 955 B → 955 B | 955 B → 955 B | **0 %** |
| D index boundary (model) | 7,235 B → 67 B | 7,235 B → 67 B | 7,235 B → 67 B | **−99 %** |

Time per operation, class → value:

| scenario | change |
|---|---|
| A cold load, full geometry | **−21…−27 %** |
| B display, simplified | **−17…−21 %** |
| C metadata only | within noise, ±7 % |
| D index boundary (model) | **not measurable here** — see the README |

## What follows

**The metadata path is unaffected, exactly.** Not "nearly": 955 bytes both
ways, on all three plans. `TrimForDisplay` sets `GeometryOmitted` and empties
every leg, so a read by a client that already knows the plan version never
decodes a point and has nothing to gain. Allocation does not fall to zero
either — the rest of the payload still decodes.

**The geometry paths roughly halve their allocation.** A cold plan load drops
from about two megabytes to under one. A display read for a client that does
not know the version drops from 830 KB to 428 KB. This is the case for the
change.

**The index boundary is pure allocation and no latency.** 7,235 − 67 = 7,168
bytes, which is exactly 224 returned points × 32 bytes. The index already
stores coordinates as values; it allocates an object for each one on the way
out. Removing that removes the allocation and moves the clock by nothing
measurable.

**The time figures are real but small in absolute terms.** A fifth off the
decode is around half a millisecond per plan load. The fuel path that performs
those loads measures in seconds, and its time is spent elsewhere — see
`waste-audit-2026-09-22.md`. This change belongs to the memory work, not the
latency work.

## What this does not establish

That GC pressure falls in the server. The probe's collection counts sit near
zero for both shapes because twenty iterations of a megabyte rarely trip a
collection under workstation GC. Showing an effect on collections needs a
sustained load on the deployed shape, not a microbenchmark.

That application CPU falls by a fifth. Scenario A times a decode, not a
request.

That the magnitude carries to production. Arm64 and workstation GC here;
x64 and Server GC in a 512 MiB container there.

## A method note worth keeping

The first version of this probe measured one shape to completion and then the
other. That made whichever ran first absorb tier-0 JIT, and it reported the
value shape as **20 % slower** on the first plan while reporting it 25 % faster
on the next two. The contradiction is what exposed the error. Running the same
plan alone reproduced it at −64 %, which confirmed the reading was positional
rather than a property of the data.

Alternating the rounds fixed it, and the byte figures — which were never
affected — reproduce to within seventeen bytes across runs. When a probe in
this repository disagrees with itself between plans, suspect the harness before
the data.
