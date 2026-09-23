# Driver messaging inbox

What is implemented of the [messaging plan](../architecture/driver-messaging-plan.md).
State on 2026-09-23: local only, not deployed, migrations `AddDriverInbox`
and `AddConversationOutbox` not applied anywhere, and no message has been
received from or sent to a real driver. Files and templates out, the
Messages page and the browser stream client are later stages (see the
plan's checklist).

## Receiving

The signed WhatsApp webhook (`WhatsAppWebhookHandlers`) records, in its own
transaction, what drivers write (`InboxRecorder`):

- A message is keyed by its provider id under company, channel and business
  number: a repeated notification records it once, and the same id under
  another carrier's number is that carrier's own message.
- A conversation is one participant on one business number. Its driver is
  matched by the WhatsApp number on the driver's contacts, and stays
  unmatched when no driver or more than one has that number.
- Text is kept, bounded at 4096 characters; a file keeps its caption, media
  id, declared type, hash and name; kinds PulsR cannot show (location,
  contact, sticker) are recorded as unsupported under their type name.
- The 24-hour window runs from the conversation's last inbound message.
  The fuel hand-over's window record is still kept as before.
- Statuses of messages sent from a conversation move forward only, matched
  by provider id under the same business number.

## Files drivers send

`InboundMediaOperation` copies each file into the company's storage (see
[file storage](file-storage.md)) before WhatsApp's seven-day media id
expires. It claims an attachment with a fenced lease, fetches the
provider's five-minute download address just before streaming (never
stored or shown), and stores the file under the attachment's own id with
the provider's SHA-256 as its fingerprint, quarantined, in the inbox folder
for the day it was sent. Only a complete durable file (quarantined or
available) marks the attachment stored; an upload still in progress is
retried. The attachment's outcome and the conversation's revision commit
in one transaction, and the change signal is sent after the commit.
A provider that does not answer is asked again with backoff; bytes that do
not match the provider's hash, a vanished file, or an expired media id end
the attachment as failed, with the reason shown.

## Reading

Dispatch policy: `GET /api/messaging/inbox?unread=` (newest 50, a fixed
number of reads whatever the number of conversations),
`GET /api/messaging/conversations/{id}?before=` (50 messages per page,
newest first, with attachment states), `POST .../{id}/read` (this
dispatcher's marker; it only moves forward and never past the newest
message). Unread counts are each dispatcher's own and tell the driver
nothing.

`GET /api/messaging/events` is a server-sent event stream of
"conversation changed" signals (id and revision, no content) for the
caller's company, raised after commit, with a keep-alive every 25 seconds.
Signals live in this process only and each subscriber keeps at most 64; a
reader that reconnects or misses signals reads the inbox again.

## Replying

`POST /api/messaging/conversations/{id}/messages` with a body, a retry key
and the newest message the dispatcher had on screen:

- Refused outside the driver's 24-hour window (only an approved template
  may go then; templates are a later stage).
- Refused as stale when the driver or a colleague wrote since the message
  on screen, unless the dispatcher confirms. The dispatcher's own last
  reply does not make the next one stale.
- Committed as `queued`, with the conversation claimed for two minutes so
  colleagues see who is answering (a courtesy, never a lock). The same
  retry key returns the first reply; with other text it is refused.
- `OutboundMessageOperation` sends queued replies. A worker takes a reply
  by setting its fence to the value it read plus one, only if nobody moved
  it since, so it knows its own fence without reading it back. Right
  before sending it checks again that the driver's window is open and that
  the carrier still sends from the conversation's business number; a
  reply that can no longer go is `withdrawn` without calling the
  provider. `sending` is committed under the fence before the call, and the
  answer is recorded only under it, together with the conversation's
  revision; the signal follows the commit. A `sending` reply whose lease
  (two minutes, longer than the provider's request timeout) ran out becomes
  `unknown`. A worker that lost its fence may still record the provider's
  id, in the same kind of transaction with the revision, on its own attempt
  if it is still `sending` or `unknown` without one; a retry is a separate
  attempt and never receives an older attempt's answer. Nothing is sent
  again without a dispatcher. This promises no silent duplicate from PulsR,
  not exactly-once delivery.
- `POST /api/messaging/messages/{id}/retry` sends an unknown, refused or
  failed reply again as the next attempt of its retry key; nothing else
  does. `POST .../conversations/{id}/claim` claims without sending.

## Tests

`Server.Tests/Fuel/WhatsAppWebhookTests` (inbound recording, duplicates,
business numbers, files, unsupported kinds, ambiguous numbers, reply
statuses), `Server.Tests/Routing/InboundMediaTests` (copy, hash mismatch,
backoff, expiry, leases, a crash between the updates, a lost finalize,
naming), `Server.Tests/Routing/InboxReadTests` (read cost, unread, markers,
paging, stream isolation), `Server.Tests/Routing/ConversationReplyTests`
(queue and send once, window, retry keys, stale replies, a take
overtaken before sending, a lease lost mid-send, a late answer on its own
attempt, withdrawal for a closed window or changed number, explicit retry,
claims). Not run: a real WhatsApp webhook or media
download, and PostgreSQL.
