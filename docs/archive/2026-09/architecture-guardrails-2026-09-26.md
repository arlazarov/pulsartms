# Architecture corrections — September 26, 2026

## Candidate and coordination

Codex implements these corrections directly in the isolated
`codex/architecture-guardrails` worktree. Claude continues the Fleet/Fuel UI
and Messages work in the original checkout. No root source or build output
is overwritten, and Driver Pay is outside this change.

The isolated checkout started at `186ce562` and incorporated Claude's
committed work through `a65ab9ad` before the completion gate. The corrections
have no file overlap with those five commits. This is local integration for
verification, not publication or a change to Claude's branch.

The [audit](application-audit-2026-09-26.md) is the historical starting
assessment. Its original reproductions are not results for this candidate.

## Implemented corrections and prevention

### A1

Fleet validates company, active truck and external ID on retrieval; Samsara
captures immutable credentials and reuses the bounded shared cache under
company/credential identity.

Regression: Equal vehicle IDs across companies; credential rotation with held
old response; expired and foreign retrieval; 32 cold readers share one
provider request.

### A2

Credential store rechecks new saved-channel claims inside a write transaction;
PostgreSQL advisory lock serializes admission across stores. Unreadable
existing claims fail closed for new admission.

Regression: Held stale prechecks; direct-store admission; release/reclaim; a
real isolated PostgreSQL transaction holds the first writer while the second
attempts admission.

### A3

Planning input discovery groups/prioritizes trucks and limits results in SQL.

Regression: Many legs per truck; active priority; one bounded SQL query; zero
work for limit zero.

### A4

Shared cache generations and relay keys distinguish company scope and
explicitly global reference data.

Regression: Company A invalidation leaves B warm, including another process;
late A loader cannot fill the new generation; explicit global invalidation
reaches both.

### A5

Infrastructure classifies only the exact message-attempt uniqueness conflict.
Messaging lets other persistence failures reach its boundary.

Regression: Actual duplicate insert is recognized; unrelated foreign-key
failure propagates and sends nothing.

### A6

Messaging owns driver recipient selection and channel readiness. Fuel supplies
driver identity and intent, records the delivery owner's channel, and retains
compatible JSON field names.

Regression: A fake non-WhatsApp recipient can be ready with no phone or reply
window; existing withdrawal/window/contact-safety checks; architecture check
blocks contact selection in Routing.

No new external channel is implemented by the fake-channel regression.
Existing message records, wire field names and delivery uncertainty are
preserved. The application still uses its configured WhatsApp adapter.

### A7

Fleet no longer depends on the Synchronization feature. It consumes the
neutral `IFleetCollectionState` runtime contract for collection mode, activity
and high-frequency locations; the synchronization host owns its implementation.
The compiled module-edge allowlist shrank rather than gaining an exception.

Regression: module dependency architecture checks plus proactive HOS and fleet
telemetry tests for enabled, idle and high-frequency collection modes.

### A8

The server remains the sole owner of fuel quote eligibility and selects cash,
IFTA, current and previous comparisons for the requested date. The map and the
station list now choose only the requested display basis and format that
selection; they no longer inspect raw quote dates or independently select a
discount.

Regression: stale raw discounts cannot replace the server selection; map and
list use the prepared cash/IFTA values and comparisons.

### A9

Unread-message signals arriving inside the 500 ms coalescing window share one
count read. A signal arriving during the actual read earns one bounded follow-up
read so a later commit is not lost. Deterministic work handles replaced timing
guesses in the affected tests.

The Client JavaScript build now fingerprints every source and build input and
records hashes for every generated output in the intermediate directory.
Unchanged, intact output skips esbuild; source deletion, missing output and
changed output force a rebuild. The measured local no-change step fell from
about 0.19 seconds to 0.05 seconds. This is a local build-step measurement, not
an end-to-end CI or production claim.

The new application-wide AGENTS ownership rules require tracing existing
owners and all foreground/background consumers before adding work. Automated
regressions guard concrete isolation, query-count, admission and ownership
invariants. These are protections against recurrence, not proof that arbitrary
future code will contain no duplication or error.

## Existing state and runtime auditor coverage

- A1/A4 are transient read/cache identities. A new process starts with empty
  local entries; no persisted business rows need rewriting. Mixed versions
  still require normal deployment drain. Legacy global invalidation events
  remain safe and may invalidate more widely until expiry.
- A2 preserves old saved ownership, including existing duplicate claims. It
  prevents new competing claims and allows existing token rotation/release;
  it does not silently decide ownership or move conversations. Integrations
  owns a deferred bounded duplicate-claim diagnostic and explicit resolution
  workflow. Closure requires an authorized inventory and a legacy-duplicate
  recovery regression. No production inventory has run here.
- A2 serializes infrequent settings writes and streams other saved claims.
  It does not yet replace that scan with an indexed protected claim identity.
  PostgreSQL lock behavior is checked with synthetic rows in the recorded
  dedicated fixture, never the application database.
- A3 is a query cost invariant, covered by SQL/call-count tests; a runtime
  business-state detector would not establish bounded reads or latency.
- A5 covers a failure before a row exists; boundary errors and regression
  tests are the evidence. A6 preserves historical attempts and JSON shape.
  Neither adds a new runtime business-auditor rule.

## Still open in the wider audit

A7 is reduced by one real edge; 19 recorded cross-feature dependencies remain
and are still technical debt. A9 removes two measured repeated-work paths but
does not claim every test or build stage is optimal.

A10 shared messaging notification across instances remains open. Messaging
change fan-out is process-local and another instance repairs by polling. A
durable company-scoped event cursor or equivalent shared fan-out is required
before increasing the current single-instance deployment bound. Reusing cache
invalidation rows as an untyped message bus was rejected because it would mix
unrelated ownership and retention contracts. This record does not certify all
UI visuals, production performance, or the whole application as complete.

## Verification

Targeted checks passed for camera/provider isolation, integration settings,
cache generations/relay, planning discovery, messaging recipient safety and
fuel delivery. The new PostgreSQL two-store admission regression passed
against the existing dedicated fixture with an isolated schema.

`bash test.sh all` passed on `a65ab9ad` plus these corrections: server 3,769
passed (2m 59s), Client 1,237 passed (6s), JavaScript 657 passed (5.316s).
None were skipped. Client compilation, JS type checking and both architecture
suites were included. Pinned CSharpier check and `git diff --check` passed.
The source hashes and gate summary are in the
[evidence manifest](architecture-guardrails-2026-09-26-evidence.json).

The second block's affected checks passed: server Fleet, Messaging,
Synchronization and Architecture dependencies (2,617 tests); Client Fleet,
ETA, Messaging, Fuel and Architecture (453 tests); map JavaScript (416),
messaging JavaScript (13) and JavaScript architecture (67). TypeScript checking
passed. The first combined runner attempt could not build Client because this
isolated worktree intentionally had no duplicate `node_modules`; the missing
dependency was supplied from the original checkout for the Client checks. The
server part of that attempt completed successfully and was not repeated.

The first full second-block gate exposed a stale test-only copy of the read
cache's stripe-key formula: the test could accidentally choose the occupied
stripe and time out after two seconds under full load. The cache now exposes
its actual stripe choice internally to the test assembly, and the controlled
test uses that owner instead of duplicating the formula. Its focused check
passed in 74 ms. The changed candidate then passed one full gate: server 3,769,
Client 1,239 and JavaScript 659, with no failures or skips. TypeScript checking
also passed.

Claude subsequently committed `ccb0838f` (map road presentation). The isolated
branch was rebased onto that commit without conflicts after the full gate, then
the combined tree passed `bash test.sh map`: server 282, Client 317, map
JavaScript 417 and JavaScript architecture 67. TypeScript checking passed.
No independent visual/browser matrix, production data repair, real provider
send, deployment or production performance measurement ran here.
