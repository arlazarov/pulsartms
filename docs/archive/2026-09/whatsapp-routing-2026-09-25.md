# WhatsApp routing between AMF and the review demo (September 25)

**What was found:** replies and statuses for AMF's test WhatsApp number
went to the Meta review demo company, not to AMF. Meta was not slow: its
callbacks arrived within seconds, and PulsR accepted every one of them
(HTTP 200). The same test business number was saved in two companies,
and Meta calls only one company's webhook address.

**What was done:** production was read only; nothing was changed. The
owner decides the configuration. A local guard now refuses a second
company taking a number, with no production data changed (see below).

All times are UTC, September 25 unless marked. Evidence comes from
bounded `READ ONLY` queries and Cloud Run request logs (path and status
only). No message text, phone number, token or secret appears here.
Conversations and companies are named by company key.

## Configuration, as recorded

| Time | Change | Source |
| --- | --- | --- |
| Sep 24, 21:07 | Meta verified the webhook of `amfcarrier` | request log |
| 00:35:40 | `meta-review-demo` saved WhatsApp credentials (revision 1) | credentials row |
| 00:35 | Meta verified the webhook of `meta-review-demo` (the callback override) | request log; [owner's handoff record](meta-review-access-2026-09-24.md) |
| 02:15–02:31 | AMF had no usable connection for its conversation's number (see withdrawals) | message rows |
| 02:34:17 | `amfcarrier` saved WhatsApp credentials again (revision 5), for the same test number | credentials row; the owner reports a restore during troubleshooting |

Both companies' rows are saved and complete. The handoff record says
AMF's connection was cleared after the handoff; the later restore saved
it again.

## Where Meta's callbacks went

| Webhook | Calls (all HTTP 200) | First–last |
| --- | --- | --- |
| `amfcarrier` | 16 POST | Sep 24 21:09 – 00:10:34 |
| `meta-review-demo` | 24 POST | 00:36 – 02:46 |

## Driver replies (from the owner's test phone)

- **Recorded in AMF's conversation:** 5 inbound messages, the last at
  00:10:34.
- **Recorded in the demo company's conversation:** 5 inbound messages,
  at 00:36:32 and between 02:44 and 02:46.
- Both conversations have the same business number and participant.

## AMF's replies: 17 attempts

| Status | Count | When | Why |
| --- | --- | --- | --- |
| rejected, code 190 | 3 | Sep 24 21:24 – 00:00 | Meta refused the access token |
| delivered | 4 | 00:04 – 00:10 | callbacks still reached `amfcarrier` |
| withdrawn | 3 | 02:15 – 02:31 | AMF had no connection for the conversation's number at send time, so the outbox sent nothing |
| accepted | 7 | 02:34 – 02:46 | see below |

**The 7 accepted replies.** Meta accepted all seven. Their status
callbacks arrived one to two seconds later at `meta-review-demo`.
There, they match no message, because statuses are applied only within
the company whose webhook is called. They will stay "accepted" in AMF.
The replies were not lost at Meta; only their statuses were routed away.

**Four Send again presses at 02:35 answered 409.** Each was pressed on an
attempt that had already been sent again. The server was right; the page
kept the button on superseded attempts. Fixed locally in `85f65104`.

## Guard (local, released with the September 25 package if it ships)

**Before.** Nothing stopped two companies saving active credentials for
the same WhatsApp number, and the webhook silently served one of them.

**Now:**
- **A new number is refused if another company holds it.** Saving a
  number whose saved, complete credentials belong to another company is
  refused with 422. The settings page says, in its own words, that the
  number is connected to another PulsR company.
- **A company may keep a number it already holds.** For example, it can
  save a new token for its own number, so the intended demo assignment
  keeps working.
- **The settings card says when a number is shared.** It states it
  whenever another company also holds the number.
- **The check stays inside the credential store.** It answers only yes
  or no across companies. A row that cannot be read does not count.
- **Not atomic.** Two companies saving at the same moment can both pass.
  A database-level rule would need the number stored in plain text: a
  schema change plus a backfill of existing rows. Neither was done.

**Not changed.** Neither company's saved connection was touched. The
current duplicate therefore stays until the owner decides which company
keeps the test number:
- once AMF's copy is cleared, the demo company alone receives;
- clearing needs no code.

Sharing one number across companies is not supported; designing for it
is future research.
