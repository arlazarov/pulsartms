# Integration credentials in Settings

Settings has three independent Admin-only connections: TorqueAI, Samsara and
Google for Emails. Maps, Places, TomTom, Torque's base URL, database credentials,
Gmail watch configuration and deployment authentication are outside this editor.
Google email uses the existing Gmail readonly OAuth flow, not a Maps API key.
This editor does not initiate Google consent, register a watch or send email.

## Existing credentials and updates

Until its Admin saves a replacement, the original carrier reads the existing
server configuration: `TorqueAI:ApiKey`, `Samsara:ApiToken` and
`Gmail:ClientId`, `Gmail:ClientSecret`, `Gmail:RefreshToken`. Opening Settings
does not copy credentials or create database records. No deployment configuration,
local secret file or Google credential is deleted or rotated by this feature.

Other carriers start without deployment credentials and must configure their own
connections. Saved bundles and optimistic revisions are keyed by company and
provider. No carrier inherits another carrier's saved or deployment credentials.

Each connection has independent fields and revision. Read responses contain only
presence, source and revision metadata, never credential values or suffixes.
"Configured" means required values are present, not that a provider accepted them.
Replacement fields start empty. Blank fields mean unchanged; rejected validation
and stale revisions do not overwrite active credentials. A lost save response can
have an uncertain outcome; reload status before retrying. The Client clears entered
values on successful save, cancel and disposal; it does not persist them locally.

A successful save stores a complete credential bundle. Google token/secret-only
replacement retains the effective Client ID and other tuple values. A different
Client ID requires its Client secret and Refresh token in the same request;
incomplete tuples are rejected. Later changes to deployment configuration cannot
silently combine a new deployment Client ID with a saved token.

Explicitly confirming "Use server configuration" clears only the saved override,
and only when the retained deployment values are complete. It leaves a revisioned
tombstone so old first-save requests cannot overwrite the restored state.
Optimistic concurrency also protects two tabs saving the same connection.

## Ownership and storage

Application owns the provider allowlist, Admin checks, blank-preservation rules,
OAuth tuple validation and credential selection. API controllers use MediatR,
require the existing Admin policy, disable response caching and bound update size.
The existing role service checks the caller's active profile again in handlers.
Auditing records actor, operation, provider, revision and outcome, not request
fields or values. Validation messages must not echo credential input.

`IIntegrationCredentialStore` and `IIntegrationDeploymentCredentials` are
Application boundaries. Infrastructure implements persistence and configuration
reads; saved values are protected using the existing durable AMFTMS Data Protection
key ring and a company/provider-specific purpose. Existing original-carrier
bundles remain readable with their legacy protection purpose; other carriers
cannot use that compatibility path. Each operation uses its own short-lived
database context. Unique company/provider identity and revision
concurrency guard writes; reads do not update timestamps or revisions.
Missing rows use deployment configuration. Corrupt or undecryptable saved values
fail closed instead of silently selecting a different credential.
An unreadable saved bundle requires operator recovery; the editor does not expose
or automatically replace corrupt data.

Data Protection keys currently live in the same database and their XML is not
independently encrypted. Protected credential rows therefore do not protect
against disclosure of a complete database backup containing that key ring.
Restrict database/backup access and preserve the key ring with the encrypted rows.
Separately protected key storage is independent security hardening; this feature
does not change that existing deployment configuration.

Samsara and Torque resolve credentials for each outbound request and set
Authorization on that request, never by mutating shared client headers. Gmail
resolves one complete bundle when creating a Gmail client. Already-running
requests can finish with their captured credentials; later requests use the saved
bundle. No provider call or key verification occurs simply by opening or saving
Settings. Invalid replacement credentials can still be rejected by the provider;
an Admin can restore the retained server configuration explicitly.

## Rollout and checks

The `IsolateCarrierIntegrationCredentials` migration must precede the isolated
credential API. It assigns existing bundles to the original carrier without
changing their encrypted values or revisions. The key becomes company/provider.
New writes use company-bound protection; automatic downgrade is refused because
merging owners or reverting protection would require explicit recovery. Drain
old writers before enabling the new API. Deploy the compatible API before the
Client. An API predating the credential editor ignores saved overrides; it can
resume the original server credentials, so coordinate rollback deliberately.

Run the full test suite and strict Client build for these shared contract,
persistence and DI changes. Tests cover configuration preservation, non-disclosure,
Admin enforcement, concurrency, encryption/tampering, OAuth tuple consistency,
per-request rotation and independent Client drafts. Tests use dummy credentials
and isolated fixtures, not production providers, mailboxes or databases. See
[test selection](../testing.md) and [release procedures](../operations/release.md).
