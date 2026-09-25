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

## Guard (released at 05:29 UTC in `ce465d2d`, see [the release](release-2026-09-25.md))

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

## Inbound not arriving (reported later on September 25)

**Documented evidence (from the read above, up to 02:46 UTC).** This is
not fresh:
- Meta's callbacks went to `meta-review-demo`;
- the test phone's replies were recorded in the demo company;
- AMF's statuses could not reach AMF.

**Fresh evidence: none yet.** Production reads need a renewed
`gcloud auth login`, and none were made.

**Code on the inbound path.** Nothing in the September 25 release changes
it except driver matching (through the shared WhatsApp rule; covered by
the database group) and quick-reply taps. There is no known code
regression.

**Where a notification can stop, and what shows it:**

| Stage | Outcome | Visible before | Visible now (local, not deployed) |
| --- | --- | --- | --- |
| Meta sends nothing (callback elsewhere, app unsubscribed) | no request | only the absence of requests | same: check the request log |
| Unknown company key in the URL | 404 | request log | counted, logged by key |
| Company not configured | 401 | request log | counted, logged "not-configured" |
| App secret mismatch | 401 | request log | counted, logged "signature" |
| Signed, but for another business number | **200, dropped** | **nothing** | counted, logged with the count of dropped changes |
| Recorded in another company | 200, stored there | that company's inbox | same |
| Recorded but not shown | 200, stored | Messages after a reload | unchanged |

Counters live under `driver-messaging-webhook/…` in `GET
/api/diagnostics/stages` (admin). They show where delivery stops; they
repair nothing.

**Evidence still needed (read only):**
1. Request log for `/api/webhooks/whatsapp/*` since 02:46: the paths
   (which company key), statuses and times.
2. Latest `ConversationMessages` rows per company, inbound (counts and
   times only).
3. Credential rows for WhatsApp per company: revision and time only.
4. In Meta's app settings, the callback URL the test WABA uses (the
   owner can see it; PulsR cannot).

**Configuration change that may be needed (owner's decision; nothing
changed here).** If AMF should receive, point the test WABA's callback
back to `/api/webhooks/whatsapp/amfcarrier` and clear the demo company's
copy of the number. If the demo should receive, clear AMF's copy.
Either way, one company holds the number.

**Fresh evidence (read at 11:57 UTC, read only).**
- No webhook call at all between 02:47 and 11:19.
- 11:19–11:24: AMF sent 4 messages. Meta then made 16 POSTs, all HTTP
  200, and every one went to `/api/webhooks/whatsapp/meta-review-demo`.
- In the same minutes, 4 driver replies were recorded in the demo
  company. None were recorded in AMF.
- Credential rows are unchanged: demo revision 1 (00:35), AMF revision 5
  (02:34). AMF holds 4 conversations, the demo company 1.

**Conclusion.** Inbound messages are arriving, and PulsR stores them, in
the demo company, the one whose webhook Meta calls for the shared test
number. AMF therefore sees none, and its replies' statuses stay
"accepted". No code change repairs this.

**Configuration change needed (owner's decision; not made).** Pick which
company owns the test number:
- **AMF:** set the WABA callback to
  `/api/webhooks/whatsapp/amfcarrier` in Meta, and clear the demo
  company's saved WhatsApp connection.
- **The demo:** clear AMF's saved connection (revision 5, restored during
  troubleshooting).

Replies already stored in the demo company stay there. Moving them is a
separate, explicit decision.

## Evening check (after the 19:57 release)

The owner reported that incoming messages still do not arrive. Evidence
from Cloud Run request and application logs (path, status and time
only) and bounded `READ ONLY` queries (counts and times, no text):

| Stage | Evidence | Result |
| --- | --- | --- |
| Meta delivery | 27 webhook POSTs since 02:44, all HTTP 200; 4 on the new revision at 21:42:55.4, 21:42:55.9, 21:42:56.0 and 21:43:02.1 | delivered |
| Company | every one to `meta-review-demo`; none to `amfcarrier` since 00:10:34 | the demo company |
| Signature | 200, not 401, on each | valid for the demo company |
| Number | no "addressed to another business number" warning; both companies' chats have the same business number and participant | same test number |
| Storage | `meta-review-demo` chat `d9a9a728` (no driver): inbound at 11:19:43, 11:21:48, 11:21:53, 11:24:47 and 21:43:02.27 | stored, in the demo company |
| AMF | chat `c9f5c6c8`: last inbound 00:10:33; replies sent at 11:19, 11:23, 11:24 (×2) and 21:42:51 stay "accepted" | nothing arrives |
| Driver filter | AMF chat's driver is active (`IsActive` and imported both true) | not involved |
| Page | AMF's inbox and unread reads answered 200 at 21:42:55, 21:43:05 and 21:43:21 | polling works; the data is elsewhere |

**At 21:42:51**, AMF sent a reply; the three POSTs a few seconds later
match that reply's status callbacks, which the demo company cannot apply.
The owner's answer from the phone arrived at 21:43:02 and was stored in
the demo company's chat 0.17 s later.

**Configuration** is as in the morning: `amfcarrier` WhatsApp revision 5
(02:34:17) and `meta-review-demo` revision 1 (00:35:40), both holding the
same test number; Meta's callback address is the demo company's.

**Stage that fails:** routing at Meta's callback address, a configuration
choice, not the release. No code defect was found on this path, so no
regression was added. Nothing was changed. Correcting it needs the
owner's decision:
- which company the test number belongs to;
- if AMF, the callback address in the Meta app set to
  `/api/webhooks/whatsapp/amfcarrier`, and the demo company's
  credentials cleared in PulsR;
- if the demo company must keep it for App Review, AMF needs its own
  number.

**Observability gap:** an accepted notification logs nothing; its
inbound and status counts exist only in the live stage counters.

## Owner's decision (evening)

The owner decided AMF must see the messages: AMF is the receiving company
for the test number. Nothing is moved: the ten inbound messages already in
`meta-review-demo` stay there, and tenant isolation is unchanged.

The route is Meta's callback address, which only the owner can change in
the Meta app. Meta calls
`https://amftms-api-ddgxhwho3a-uk.a.run.app/api/webhooks/whatsapp/<company>`;
AMF's address passed Meta's check on September 24 (GET 200) and received
16 notifications (all 200) before the switch. Steps, no secrets involved
beyond what AMF already saved:
1. In PulsR, signed in to AMF: Settings → Integrations → WhatsApp shows
   "Webhook URL for Meta" (the `amfcarrier` address). The verify token is
   the one saved in AMF's WhatsApp credentials (revision 5, 02:34).
2. In the Meta app: WhatsApp → Configuration → Webhook → Edit, set the
   Callback URL to AMF's address and the Verify token to AMF's; keep the
   `messages` field subscribed. Meta verifies it at once (GET).
3. If Meta refuses the verification, AMF's saved token or app secret
   differs from the app's: re-enter them in AMF's WhatsApp settings.
4. Optional, the owner's call: disconnect the number from
   `meta-review-demo` (Settings → Integrations → WhatsApp there) if the
   Meta review no longer needs it; the settings card flags the shared
   number until then.

Evidence to read afterwards (read only): a GET 200 on
`/api/webhooks/whatsapp/amfcarrier`, later POSTs there, and new inbound
rows in AMF's conversation. Earlier "accepted" replies stay as they are;
their statuses went to the other company.
