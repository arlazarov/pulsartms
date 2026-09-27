# Historical road input isolation

## Incident and owner

The authorized historical recovery remained at one newly repaired load, AMF1397,
with 70 pending native completed loads. Forty-nine low-priority demands had never
been attempted. A bounded AMF-only queue read found repeated high-priority
versions and fresh request times for current/planned work. The current API
c331aa00 is deployed; this correction is a separate local candidate.

SourceRoadInputs is the background observation owner. It includes history from
DeadheadHistoryService in its signature. Scan reads multiple loads; preparation
reads one. DeadheadHistoryService.ApplyExecutionBatchesAsync admitted completed
native legs using the combined predecessor and saved-reference sets of all loads
in a lookup batch. Consequently, an unrelated lookup peer changed a load's
history signature. The queue correctly interpreted different signatures as new
work, repeatedly competing with historical demands.

## Evidence

A bounded read-only diagnostic compared the same authoritative SourceRoadInputs
batch and single-load operations. Before the correction, seven of ten sampled
pending high-priority loads differed. After the correction, all ten sampled
loads matched. These were two separate observations while production remained
active, not a production test or a controlled same-snapshot performance measure.
The diagnostic performed no demands, provider route calls or worker execution.

Two isolated regression cases failed before the fix, covering predecessor-based
and saved-reference-based completed eligibility. Both now pass. The five-test
history snapshot class passed. The two-test publication class passed, retaining
the controlled mutation test that rejects a changed predecessor revision.

An older publication test explicitly expected batch-dependent extra history.
It now requires the dependent current load to retain that completed leg and the
unrelated future load to have the same signature alone and in a batch. Its
post-capture mutation rejection remains unchanged.

## Correction and cost

Completed eligibility is scoped to each work item's own predecessor identities
and saved reference. Active/planned leg selection remains truck scoped. The
existing batched database and execution reads are retained; no per-load database
query, cache, provider call, formula or queue-priority override is introduced.
Consumers include source-road background observation, deadhead calculations,
financial reads and fuel-horizon history/publication validation.

Foreground/background database call counts and production throughput have not
been benchmarked. The eliminated repetition is the reproduced difference in
history inputs caused solely by lookup peers; deployment must still establish
that queue versions settle and pending financials recover.

## Consistency coverage and recovery

Existing transaction and dependency-version publication guards remain in place.
Local tests cover batch/single equivalence, saved references and predecessor
mutation. Runtime auditor detection of batch-dependent signatures is not
implemented: the routing owner should add bounded detection only if retained
operational evidence warrants it. Completion criterion for this incident is
stable owner reads plus progress of the existing historical demands after an
approved release, not queue completion alone.

Do not reset attempts or priorities and do not requeue already-pending requests.
Re-read the normal Completed financial owner after actual progress. AMF1388 and
AMF1136 remain separate missing/ambiguous predecessor-input cases; do not invent
assignments or mileage. No migrations, frontend changes or production repairs
are included in this code change.

## Local completion checks

Final affected `bash test.sh dispatch routing` passed: Server 2,053, Client 714,
JavaScript Dispatch 16 and Architecture 67, zero failures/skips; JavaScript type
checks passed. The first affected run caught the obsolete batch-dependent test
expectation described above; the final run includes its corrected invariant.
The exact release candidate still requires its full gate by the deploy owner.
