# Dispatch workspace density follow-up

Local UI follow-up after the September 14 workspace release. These changes have
not been deployed. No server contracts, persistence or migrations changed.

## Changes

- Order tabs as Broker & billing, Overview, History; retain Overview as default.
- Keep native assignment, transfer, mileage and stop-update tools in Overview.
  Preserve mounted components, drafts, existing write boundaries and save locks.
- Change via Switch focuses and scrolls to the resource tools without changing
  the selected visit or fetching data again.
- Show exact truck, trailer, driver and optional co-driver snapshots in each
  stop row. Missing resources remain unknown; long names wrap in full.
- Give the route more desktop width and retain reorder controls beside its
  facts. Stack only when needed; preserve shared text and touch-control sizes.
- Keep one selected-stop editor below the full list, with denser field groups.

## Checks

`bash test.sh dispatch styles` passed: 596 Client C#, 761 Server C# and 191 Node
checks, including architecture and the selected dependency groups. This is an
affected-category run, not a full-suite or release-gate pass.

- Log: `artifacts/managed/scratch-O4JtP6/affected.log` from repo root.
- Strict Client Debug build: zero warnings and errors.
- Strict Client Release publish: `artifacts/managed/release-Axj8d4/publish`.
- Published-asset verification: 270 assets and 7 JavaScript dependency graphs.
- Current and staged CSS SHA-256 both begin `7994e3a2024db8b4`.
- Targeted CSharpier, Prettier and diff whitespace checks passed.
- Workspace browser matrix: 8 cases, including both themes, 320/390/1440px
  widths and 200% root text. No browser errors or unexpected requests.
  Report: `artifacts/managed/browser-ui-fiXP9e/report.json` from repo root.
- Generic UI smoke: 1440px light, 6 pages; no errors or unexpected requests.
  Report: `artifacts/managed/browser-ui-YAFNJQ/report.json` from repo root.
- Desktop and mobile screenshots were inspected after the density refinement.

New regressions cover driver-preserving reorder, missing-driver rendering,
retained resource/editor instances and drafts across tabs, save locks and the
real Switch focus/scroll action without extra requests. Initial new-test
failures were corrected for bUnit's retained-element reference serialization,
Sass slash whitespace and an ambiguous browser heading selector.

Browser checks use intercepted synthetic API responses and writes, not live
business operations. No production mutation, real document-storage check,
provider check, PostgreSQL fixture run or deployment was performed. No safe
isolated PostgreSQL fixture was available. Tests and screenshots do not prove
complete visual correctness or production performance; neither was claimed.

Local Client was rebuilt and restarted on port 5067. The existing local API was
left running with migrations and synchronization disabled.
