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

A7 module cycles, A8 shared station price presentation, A9 build/test
improvements and A10 shared messaging notification across instances remain
open. A8 concerns Claude's newly committed UI and must be coordinated before
editing the same owner. A10 remains a limitation before increasing the current
single-instance deployment bound. This record does not certify those items,
all UI visuals, production performance, or the whole application as complete.

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

Claude subsequently committed `ccb0838f` (map road presentation) while this
gate ran. It is outside this candidate and has no overlapping source file.
Do not treat this gate as acceptance of later changes or the combined future
release. No independent visual/browser matrix, production data repair, real
provider send, deployment or production performance measurement ran here.
