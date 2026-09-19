# PulsR product naming

The full display name is **PulsR TMS**, with **PulsR** as its short form and that
capitalization preserved. Follow the authoritative [brand design](../design/brand-design.md)
and use the selected Pulse artwork through the shared `BrandLogo` component;
do not recreate the wordmark in a font. TMS is the supplied compact descriptor,
beside the wordmark, not part of carrier names or load numbers. The sidebar,
sign-in page, browser titles and favicon identify PulsR. The solution is
`pulsartms.slnx`, the private frontend package is `pulsartms-client`, and the Gmail API
client identifies its application as `PulsR`.

The canonical technical slug is `pulsartms`. The local checkout is
`/Users/antonarlazarov/Developer/projects/pulsartms` and the GitHub repository is
[arlazarov/pulsartms](https://github.com/arlazarov/pulsartms). Repository history and
the existing working tree are preserved; a repository rename does not publish
uncommitted source changes.

Local development uses User Secrets ID `pulsartms-api-local`; the existing secret
directory was moved without changing its contents. Other checkouts using the old
ID must move their configured store before starting the renamed API. Do not copy
secret values into source, logs or disposable build outputs. The Gmail library's
in-memory credential user ID is `pulsartms`; this is not an OAuth client ID and
does not migrate its Google project.

New Dispatch view preferences use `pulsartms.dispatch.view`. An absent new value
reads and best-effort copies a valid legacy preference; an existing new value wins.
The old key remains readable by old tabs. The in-process HTTP request option is
`pulsartms.required-session`. New metrics use meter `PulsarTms.Application` and
histogram `pulsartms.request.duration`; collectors must subscribe to the new names.
Historical metric series are not rewritten.

AMF Carrier remains the operating carrier. Its name, Samsara tag, configured load
prefix, assignments, email addresses and imported business records are not product
branding and must not be replaced.

## Compatibility identities

These legacy identifiers intentionally remain unchanged. They are not display
names, and a global text replacement can break existing sessions, credentials,
authorization, deployment or monitoring.

| Identity | Reason to preserve |
| --- | --- |
| Data Protection application name `AMFTMS` and purpose `AMFTMS.IntegrationCredentials.v1` | Existing authentication and saved integration credentials use the durable key ring and its isolation purposes. |
| Persisted claim type `amftms:role` and its database index filter | Existing users and authorization depend on the exact stored claim type. Historical migrations remain unchanged. |
| Browser lock `amftms:auth-session` | Old and new open tabs must coordinate authentication under the same Web Lock; changing it alone creates cross-tab refresh races. |
| Legacy preference key `amftms.dispatch.view` | Read-only fallback when the canonical preference is absent; new writes use `pulsartms.dispatch.view`. |
| Legacy `AMFTMS_RELEASE_*` and `AMFTMS_DEPLOY_ENV_FILE` inputs | Scripts use canonical `PULSARTMS_*` controls and fall back only when the corresponding canonical variable is unset. |
| `AMFTMS_ARTIFACT_DIR` and `.amftms-artifact.json` | The managed runner exports the legacy directory alias alongside `PULSARTMS_ARTIFACT_DIR`. New runs use `.pulsartms-artifact.json`; existing legacy markers remain recognized conservatively. |
| Google Cloud/Firebase project `amftms`, service `amftms-api`, registry paths, Pub/Sub topics, service accounts and audiences | These identify existing external resources, not labels that can be renamed in code. |

Historical archive records retain the name and commands used when their evidence
was collected. Compatibility tests may contain old names to verify fallback behavior.

## Cloud migration boundary

Google Cloud/Firebase project `pulsartms` (number `528891511512`, display name
PulsR TMS) exists with the same billing account as the current project. Its default
Hosting site reserves `https://pulsartms.web.app`, but no application release or
Cloud Run service has been deployed there. Existing production remains on
`amftms`, including `amftms-api`, `amftms.web.app` and `tms.amfcarrier.com`.
The default Firebase project and deployment resource names deliberately remain
unchanged until a verified cutover; the new project is not a usable replacement yet.

Project IDs, service names, registry addresses and authentication audiences cannot
be migrated by a text replacement. The remaining cutover must prepare API/runtime
configuration, provider key restrictions, Gmail OAuth/watch/push ownership and
Hosting rewrites/domain routing together. Gmail requires the watch topic and OAuth
client to belong to the same Google developer project. A fully renamed Gmail
integration therefore needs a new project's OAuth client and mailbox-owner consent,
with credentials stored through the existing secure configuration path.
Saved application-managed Google credentials override deployment environment
values as a complete bundle. Inspect their presence metadata before cutover;
do not replace the live bundle while the old service still targets the old topic.
A client-ID change requires its matching client secret and refresh token together.

Do not activate a second synchronization or Gmail-maintenance owner against the
working database. Preserve the current service and rollback configuration until
the replacement has been verified. No database migration, domain purchase, DNS
change or production deployment is implied by the local/GitHub rename. Follow
[release operations](../operations/release.md) before publishing.
