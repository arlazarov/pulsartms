# Public privacy policy — September 23, 2026

Published at https://tms.amfcarrier.com/privacy/ without authentication.
Data deletion instructions: the same URL with `#data-deletion`.
The user supplied the policy and explicitly requested publication for Meta.
Only the draft notice became “Effective September 23, 2026.” The remaining
policy text was compared against the supplied document and matches exactly.

## Release scope

Firebase Hosting version `59c824701ecaa492` preserves the configuration and
all 299 file hashes from live version `14c4e3e5d2b189f0`. It adds only
`/privacy/index.html` and `/privacy/privacy.css`. The full working tree was
not deployed. No API, database, WhatsApp changes or authentication changes
were included. The static page uses existing public theme tokens and assets.
The Hosting REST release was guarded against a changed live version; its
prepared file manifest was verified before publication.

## Checks

- Exact policy-text comparison, formatter and whitespace checks passed.
- Browser checks at 390 and 1280 pixels: ten sections and no horizontal
  overflow. The mobile screenshot was visually inspected.
- Full `bash test.sh` was attempted: Client 1,073 passed; Server 3,493
  passed and one failed. The failure is in the unrelated local WhatsApp
  work: `LocalDriverMessagingTests.WithoutTheSettingTheRealAdapterIsUsed`
  cannot resolve `IIntegrationCredentials` for `WhatsAppCloudMessaging`.
  The runner stopped before its JavaScript phase. This is not a full pass.
  Those application changes are excluded from this static-only release.
- No new runtime consistency rule applies: this page has no stateful
  workflow, data mutation, external message or tenant records.

The login UI was not rebuilt; adding a footer link can accompany its next
reviewed release. Meta can use the public policy URL immediately.
