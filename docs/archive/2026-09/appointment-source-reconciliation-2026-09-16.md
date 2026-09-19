# Appointment source reconciliation

## Evidence

Read-only inspection on September 16 at 12:06 UTC found AMF1383's imported
delivery appointment at September 16, 09:00 and arrival at
2026-09-16T03:07:16.261Z. Its active native section still held September 14,
05:00. TorqueAI had supplied the update; native reconciliation rejected an
appointment change as a changed visit. Separately, the Fleet card replaced a
dated planning appointment from load details without replacing its lateness
forecast. This allowed visibly inconsistent appointment and lateness values.

## Local changes

- Merge ordinary appointments independently from visit address/operation
  identity. Keep native transfer visits and completed sections protected.
- Three-way merge workspace appointment groups against their source baseline,
  preserving local appointment overrides without freezing unrelated fields.
- Use existing section revisions and planning outbox invalidation. Identical
  imports do not create another revision or rebuild request.
- Enrich only missing Fleet planning appointments from load details. A dated
  plan retains its appointment alongside its associated lateness forecast.

## Verification

- `bash test.sh all`: 2,184 server, 1,012 Client C# and 561 JavaScript tests
  passed, including architecture checks.
- Regressions cover changed delivery windows and arrival events, local schedule
  overrides, unchanged replay, native handoff protection, completed-section
  retention and provider-to-native synchronization with planning invalidation.
- Strict Client build: zero warnings or errors.
- Strict diagnostic-tool build: zero warnings or errors.
- `git diff --check`: passed.

At local completion no deployment or production writes had been performed.
Integration regressions used SQLite fixtures; isolated PostgreSQL integration
checks were not run. No database migration is required.

## Approved publication

The user subsequently requested publication. The complete local release gate
passed, including zero-warning Release builds, the tests above, staged artifact
verification and the offline UI smoke matrix (12 viewport/theme/text-scale
cases, 52 page checks, no reported failures).

- Cloud Build: `ad1f7263-c16f-4746-ab8f-1df3d494a40c`.
- API revision: `amftms-api-b-ad1f7263-c16f-4746-ab8f-1df3d494a40c`.
- Image digest:
  `sha256:c10b1b46adf1dec1c4ad21be24767d1800a38dfee8291d8f38630dee86dff7af`.
- Cloud Build repeated the full automated gate successfully before deployment.
- Requested and observed service generation: 171; verified 100% traffic on
  the new revision.
- Firebase Hosting published the exact verified local artifact from
  `artifacts/managed/release-XS2UYA/publish/wwwroot` (273 files).
- Production HTML, CSS and `Client.ak9uqy7kp0.wasm` returned HTTP 200 and
  matched local SHA-256 hashes. Entry HTML/CSS revalidate; the fingerprinted
  assembly has immutable caching. The public API health endpoint returned 200.

Normal background synchronization updated AMF1383's active section from revision
2 to 3 without a manual database repair. At 12:23 UTC its appointment was
September 16, 09:00. The saved ETA used the imported actual arrival
September 15, 23:07 EDT, the correct appointment and zero late minutes.

Google Cloud required interactive reauthentication. Hosting used a short-lived
token from that approved account, held only in the child-process environment,
with `amftms` as its quota project. No service-account keys were created.
The redundant pending Firebase login was cancelled. Authenticated production
browser interaction was not exercised; post-publication checks used read-only
data inspection, public HTTP checks and exact artifact comparison.
