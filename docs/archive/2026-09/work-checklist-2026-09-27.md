# Work checklist — September 27, 2026

Every assignment from the owner and root since the September 27 audit,
with its state and evidence. States: **deployed** (live, verified),
**implemented** (committed and tested, not live), **proposed** (designed,
waiting for a decision), **open** (not started), **blocked** (waiting on
someone named). Nothing leaves this list without a state. Times are UTC.

## Releases today

| Release | What | Evidence |
| --- | --- | --- |
| `79bd0517` API 04:34, frontend 10:29 | Dispatch views, current load | `release-GTsl5s` |
| `76653109` frontend 10:47 | clocks, day bands, Papers money, ETA | `release-TDTuIV` |
| `2f798c05` frontend 11:14 | truck and trailer, short rates, distances | `release-OYVBFR` |
| `20b6ec91` API + frontend 11:49 | stop-save and Discard incident (root) | `release-H5e12a`, backup `…5M7i0m` |
| `c2618995` frontend 12:01 | Table by coloured days, RPM names | `release-ZyxGZ4` |
| `4b3adbf6` frontend 12:20 | speed, map focus, day order, views | `release-FlPysI` |
| `6ff3f19c` API + frontend 12:56 | search by displayed number, completed from Cards/Papers | `release-2RmBrJ`, backup `…Q95QKd` |
| `2c76b88c` API + frontend 13:46 | prefix search + paging, map ring, Papers order, 11006 fuel (root) | `release-BdSW74`, backup `…PuYcTF` |
| `aae625bc` frontend 13:59 | truck marks smaller than stops | `release-iCH61Y` |

Records: `docs/archive/2026-09/release-2026-09-26-evening.md`.

A publication proves the code that is served, not the data: a release
repairs no existing row, and "deployed" below never means that rows
written before it are correct.

## Owner's UI requests

| Request | State | Evidence |
| --- | --- | --- |
| Dispatch clocks on one line | deployed | `3b75f79e`, `release-TDTuIV` |
| Table days visible | deployed, redesigned | `3b75f79e`, then `354fcb33` |
| Papers money under the miles | deployed | `3b75f79e` |
| Map card ETA beside its label | deployed | `76653109` |
| Papers truck and trailer together | deployed | `2f798c05` |
| Papers rates named short | deployed, then renamed | `2f798c05`, `a692d5bb` |
| Table miles and km on one line | deployed | `2f798c05` |
| RPM / Total RPM on every view | deployed | `a692d5bb`, `release-ZyxGZ4` |
| Table variant A, coloured days, LTL | deployed | `354fcb33`, `c2618995` |
| Speed green only while engine runs | deployed | `5d1fac61` |
| Map focus when opened from Dispatch | deployed | `c76e8af7`; not seen on the real map |
| Table day order: pickup, delivery | deployed | `d2f2e441` |
| No Current/Next edge bar | deployed | `4f446b04` |
| Only the load number opens a load | deployed | `2d606052` |
| Views Cards, Papers, Table; Completed in Table only | deployed | `4b3adbf6` |
| 11006 ringed with its delivery while driving, far out | deployed | `9c0180d5`, `7b294f89`, `release-BdSW74` |
| Prefix search (AMF10 and 10), all history pages | deployed | `e6fed730`, `release-BdSW74` |
| Papers by next unfinished stop | deployed | `160498e5`, `3e8c174f`, `release-BdSW74` |
| Truck markers smaller than stops; selection does not enlarge | deployed | `aae625bc`, `release-iCH61Y` |
| Arrow between pickup and delivery | blocked: owner | never existed in the Table; asked whether to add one |
| Search by displayed number (AMF1408); Cards and Papers also find completed loads, labelled, openable | deployed, root to verify live | `6ff3f19c`, `release-2RmBrJ`, gen 278 |

These six shipped together as `4b3adbf6` (`release-FlPysI`, 12:20).

## Root's incident and reviews

| Item | State | Evidence |
| --- | --- | --- |
| Stop-save / Discard (root's d04, fe97, 944f, fae9) | deployed | `20b6ec91`, gen 276 |
| Blocker found in review (price scale) | fixed by root | `944fa9d6` |
| Table review: truck identity, shared equipment | deployed | `c2618995` |
| Truck 11005 resource conflict (AMF1403, 55904 vs 055904) | blocked: root owns | not touched here |
| Historical Total / RPM recovery for loads saved before the fixes | open: root owns | existing rows unchanged by any release |

## Application audit (September 27)

Document: `docs/archive/2026-09/application-audit-2026-09-27.md` on
`claude/audit-d1-d2` (`1ddba3a8`), not merged. Coverage reports:
`artifacts/managed/diagnostic-XrL576`.

| Item | State | Next step |
| --- | --- | --- |
| Coverage closure (endpoints, migrations, money, state, tests) | inventoried, awaiting root review; gaps below | root reads `1ddba3a8` |
| D2 roster through ReadCache (F2) | implemented, in root review | `07631585`, comment `02299cd7`; saving unmeasured |
| F22 cost totals over a truncated page | proposed with fix, awaiting root review | `88331393`; no load affected today |
| D6 fuel hand-over recovery (F16, F17) | proposed | design for root |
| D1 road wait reason (F1) | implemented, in root review | `207a0a5a`, `3db48240` (categories, company, signature) |
| F23 attempt cap and auditor rule for road requests | open | after D1 |
| F20 fuel import stops on one bad email | open | quarantine per message |
| F8/F18 manual syncs Admin-only; role model | proposed; role model blocked: owner | |
| D3 dead HOS read path (F3) | proposed, accepted in principle | |
| D4 key ring custody (F13) | blocked: owner | |
| D5 limits and proxy chain (F14) | blocked: platform | |
| F19 shared stations for several carriers | blocked: owner | |
| F21, F24, F25, F26 designs | open | designs before code |
| F4-F7, F9-F12, F15, F27 | open | after the above |
| Cold-read debt: Messenger driver work, cold 17 statements | open, measured | `DriverWorkCostTests`; production ratio unmeasured |
| PostgreSQL fixture for 42 skipped tests | blocked: tests owner | |

The audit's coverage is an inventory, not a proof: its remaining gaps
are listed in the audit's "Open gaps" and below, and no area is claimed
complete while they stand.

## Runtime detection gaps

Invariants no running check watches today; each needs an auditor rule
or a documented reason (owner in brackets).

- A WhatsApp fuel hand-over left `Sending` (F16) [routing, with D6].
- An accepted hand-over with no recorded visit (F17) [routing, with D6].
- `SourceRoadRequests` overdue or retried without end (F1, F23)
  [routing, with D1].
- A switch operation left planned or half-received [execution].
- Gmail watch lag and the synchronization and odometer leases [fuel,
  synchronization].
- Assignment revisions, route plans, planning summaries and ETA memory,
  listed as not checked by the auditor itself [planning].
- Sweep cursors are in memory: after a restart every rule reports
  never-run until it has run [auditor].

## Rules kept

No deploy without the owner's or root's word; no production writes; the
Maps key and credentials never printed; backups only in
`local-backups/`; one gate per release; artifacts pinned.
