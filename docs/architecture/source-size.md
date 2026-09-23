# Source size and cohesion review

Server C# files have a 400-line review threshold, not a claim that line count
measures architecture. Generated migrations remain outside this check.
Existing browser/style thresholds are separate policies and are unchanged.

When a maintained server file exceeds the threshold, first identify whether
it combines distinct responsibilities. Extract a coherent owner or helper
when that improves understanding. Do not create arbitrary numbered partials,
move unrelated methods, compress formatting or remove useful explanations
merely to satisfy the count. Files under 400 lines still require review of
coupling, public surface, ownership and testability.

A cohesive file may remain larger after explicit review. Record its exact
path and a bounded maximum in ServerStructureTests.Reviewed, together with
its responsibility, why extraction would make it harder to understand, and
a review reference (a maintained decision record or PR). The reference must
contain the reasoning, not merely a passing test result. Keep existing
layer/dependency checks unchanged. This mechanism is authorized for source
size only, never for bypassing architectural boundaries or making an unrelated
failing test pass.

Growth beyond the reviewed maximum requires a new cohesion review. Remove
stale entries when a file disappears or drops below the threshold. Review
rationale is assessed by a reviewer; automated checks only enforce that a
bounded, nonempty record exists. Do not add an exception preemptively.
