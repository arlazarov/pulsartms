# Dispatch workspace implementation — September 14, 2026

## Scope

Implemented the full-page editor for existing loads. The maintained contract
is in [Dispatch workspace](../../features/dispatch-workspace.md). Cards,
Table, Papers and map links open the same load identity, with exact stop
selection where provided.

The page includes future-stop editing and reorder, appointment windows,
broker contacts, instructions, documents, internal calls/notes, open issues,
saved billing values, audit history and existing native execution workflows.
Native load creation, customer invoicing and payroll are not included.

## Safety and corrected invariants

- Future changes stay inside eligible assignment segments. Recorded work,
  transfer boundaries and mileage decisions remain protected.
- Saves carry revision, source fingerprint and actor-bound retry identity.
  Imported updates cannot replace a locally managed stop sequence.
- Notes preserve their original author, time and text through resolution.
  Documents can be attached after completion without unlocking route edits.
- Save enrichment updates projections without recreating stop editors or
  losing the next input. Conflicts retain drafts for explicit resolution.
- Native drag uses the enumerated HTML draggable value. Arrow controls
  provide the same reorder operation; selected stops can collapse.
- Address/contact disclosures reduce the open form height. Light and dark
  themes use shared tokens, including a neutral editor surface.

## Verification

Final `bash test.sh all` was run without a concurrent release publish:

- Client: 933 passed, zero failed or skipped.
- Server: 1,864 passed, zero failed or skipped.
- JavaScript, styles and architecture: 538 passed, zero failed or skipped.
- Strict Client Release publish and the strict Server build passed.
- JavaScript type checking and focused SCSS/JS formatting passed.
- Published artifact integrity: 270 assets and seven module graphs passed.
- Browser matrix: eight scenarios passed, with no browser errors,
  unexpected requests, horizontal overflow or clipped checked controls.

The matrix covers 1,440, 390 and 320 pixel viewports, both themes, and
390 pixels with 200% root text scaling. It exercises native reorder,
Save/Discard, invalid time retention, forecast invalidation, calls/notes,
issue resolution, uploads, navigation guards and collapsed route lists.
Root text scaling is not browser zoom or proof of complete accessibility.

The production Client artifact was served with synthetic identity and API
fixtures. All browser writes stayed in memory. No live business data,
credentials or provider calls were used. Screenshots were visually reviewed.

One existing batch-read readiness test intermittently timed out while an
independent release publish was running. Its waits now yield asynchronously
with unchanged assertions and timeout. Two standalone Client runs and the
final complete run passed. The contention cause remains unproven; no
production workaround or relaxed assertion was added.

## Local evidence

Paths below are relative to the repository root and use managed retention:

- Final Client artifact: `artifacts/managed/scratch-6L8lCn/publish`.
- Full suite log: `artifacts/managed/scratch-YrBOj4/full-suite.log`.
- Browser report and screenshots: `artifacts/managed/browser-ui-XxRCpT`.
- Additional standalone Client TRX files: `artifacts/tests/results`.

The publish environment does not have the optional wasm-tools workload;
the successful publish is not evidence of workload-level WebAssembly
optimization or measured production performance.

## Rollout status and exclusions

Migration `20260914153020_AddDispatchWorkspace` is generated, not applied.
No application or production database was changed, no live runtime was
restarted, and no deployment was performed. PostgreSQL execution/migration
checks were not run because no approved isolated fixture was available.
SQLite fixture results do not establish PostgreSQL behavior.

Live authentication, real provider geocoding, real document storage under
production load and production performance were not measured by this work.
