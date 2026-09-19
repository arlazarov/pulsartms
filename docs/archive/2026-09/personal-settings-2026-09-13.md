# Personal settings: September 13, 2026

## Implemented

Personal settings appears in the account disclosure beside Logout. The separate
authenticated page owns Light/Dark and temperature/distance units, with automatic
saving through the caller-scoped appearance endpoint. Company Settings is Admin
only and retains integrations, load numbering and fuel preferences.

The root account provider restores and cascades personal units independently of
company prefix reads. Partial unit writes preserve omitted fields; theme-only
writes preserve both units. Failed writes retain confirmed selections. Account
changes and logout reset units and discard late responses. No planning inputs,
financial formulas, provider requests or map geometry algorithms were changed.

`20260913142839_AddUserDisplayUnits` adds two Users columns and seeds existing
accounts from the former shared settings. New accounts default to Both. Legacy
company columns remain for compatibility but do not drive Client display units.
This migration and the preceding account-theme migration remain unapplied in
this workflow. No live database settings or deployment were changed.

## Verification

- `bash test.sh all`: 821 Client C#, 1,678 Server C# and 513 Node checks passed.
- Strict isolated Client build: zero warnings and errors.
- Release Client staging succeeded; optional wasm-tools optimization is absent.
- `appearanceSmoke.mjs`: desktop 1440px and mobile 390px passed, using six fresh
  browser contexts with synthetic account-scoped API persistence. Verified menu
  navigation, both themes, personal unit saves, isolation and new-device restore.
  No horizontal overflow; theme buttons measured 40px desktop and 44px mobile.
- Screenshots inspected at both sizes. Report and images are in managed artifact
  `browser-ui-g99DbI`; the tested Client stage is `scratch-K9glZd/publish/wwwroot`.
- PostgreSQL migration SQL generation and model consistency passed without
  opening a connection. Actual PostgreSQL migration execution was not run:
  no suitable isolated fixture was available. SQLite tests verify account
  persistence and isolation, not PostgreSQL migration execution.

Browser checks use synthetic authentication and settings; they do not certify
live integrations, real account sign-in or every page's visual correctness.
