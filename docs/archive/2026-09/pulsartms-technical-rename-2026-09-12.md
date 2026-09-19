# Technical rename and cloud preparation — September 12, 2026

This follows the [initial display rebrand](pulsr-rebrand-2026-09-12.md). The user
confirmed the exact technical slug `pulsartms`, including the checkout, GitHub and
cloud. The UI wordmark remains PulsR, with TMS as its descriptor.

## Completed changes

- Moved the existing checkout from `Developer/projects/AMFTMS` to
  `Developer/projects/pulsartms`; the old directory is absent. Git metadata,
  history and pre-existing working-tree changes remain intact.
- Renamed GitHub repository ID `1365422820` from `arlazarov/AMFTMS` to
  `arlazarov/pulsartms`, retaining public visibility and the main default branch.
  Updated local origin to `https://github.com/arlazarov/pulsartms.git`. No source
  commit or push was performed.
- Renamed the solution to `pulsartms.slnx` and frontend package to
  `pulsartms-client`. Updated build, CI, test, root-discovery and maintained
  documentation references.
- Made `PULSARTMS_*` canonical for release and artifact controls. Corresponding
  legacy variables are fallbacks only when the canonical input is unset. New
  managed artifacts use `.pulsartms-artifact.json`; old markers are read with the
  same conservative deletion rules. Canonical invalid markers cannot fall back
  to legacy metadata, and symlink markers remain ineligible.
- Moved the existing local User Secrets directory to `pulsartms-api-local`
  without changing its contents. Updated the API and diagnostic probe ID.
- Migrated Dispatch view preferences through a valid legacy read only when the
  new key is absent. Migration writes are best effort and cannot discard a
  readable selected view. Updated the in-process HTTP session option, the Gmail
  library's in-memory user identifier and request metric names.
- Retained encryption purposes, persisted roles, cross-tab authentication locks,
  historical evidence and active production resource IDs as documented in
  [product compatibility](../../development/product-branding.md).

## Cloud preparation, not cutover

Created Google Cloud project `pulsartms`, number `528891511512`, with display name
PulsR TMS, under the existing organization and billing account. Registered Firebase
and its default Hosting site, reserving `https://pulsartms.web.app`.

Independent metadata checks found no Hosting release, Cloud Run service, Pub/Sub
topic or subscription in the target. No app image, runtime configuration, secret,
database or integration worker was copied or deployed. Existing production remains
in `amftms`; its traffic, Gmail configuration, IAM and custom domain were not changed.
The CLI default project and deployment targets still select current production.

The full transfer requires a new Gmail OAuth client and mailbox-owner consent,
coordinated with topic/push setup, runtime configuration, provider restrictions,
Hosting rewrites and domain routing. Saved application-managed credential bundles
take precedence over deployment environment values. Their production presence was
not inspected; replacing a live saved bundle before topic cutover is unsafe.
Do not activate both old and new synchronization/Gmail maintenance owners.

## Verification

- Full final `bash test.sh all`: server 1,533/1,533, Client 700/700, Node 435/435
  passed, including architecture checks.
- An earlier run passed before the throwing-storage regression was added. The
  next run correctly failed a new test's xUnit analyzer rule; its assertion was
  corrected. A subsequent run reproduced the existing Settings timing failure
  documented in the initial rebrand. That test now waits for its actual fuel
  input instead of assuming a separately loaded integration panel proves fuel
  settings are ready. All behavior assertions remain unchanged. The final full
  run above passed after those changes.
- Direct Client and API Debug builds passed with warnings treated as errors,
  zero warnings and errors, from the new checkout path.
- Restarted the existing local API and Client from the new path on ports 5086
  and 5067. API startup explicitly disables automatic migrations, fleet
  synchronization and Gmail background maintenance. Other normal local planning
  workers remain enabled; this was not an isolated database-test fixture.
- `/api/health/live` returned Healthy; localhost Client returned the PulsR TMS
  document and generated framework/import-map references. These are startup
  checks, not proof of live integrations or authenticated browser behavior.
- `git diff --check` passed. The managed retention runner removed no outputs in
  these test runs.

No migration was added or applied for the rename; unrelated existing migrations
were not certified by this work. No production deployment, DNS cutover, domain
purchase or credential replacement occurred. Real PostgreSQL fixture checks and
an authenticated browser matrix were not run. The earlier offline branding
screenshots remain historical evidence, not a new visual pass. No performance
claim is made.
