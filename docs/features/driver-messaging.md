# Driver messaging inbox

What is implemented of the [messaging plan](../architecture/driver-messaging-plan.md).
State on 2026-09-23: local only, not deployed, migration `AddDriverInbox`
not applied anywhere, and no message has been received from or sent to a
real driver. Sending from a conversation, the Messages page and the browser
stream client are later stages (see the plan's checklist).

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

## Tests

`Server.Tests/Fuel/WhatsAppWebhookTests` (inbound recording, duplicates,
business numbers, files, unsupported kinds, ambiguous numbers, reply
statuses), `Server.Tests/Routing/InboundMediaTests` (copy, hash mismatch,
backoff, expiry, leases, a crash between the updates, a lost finalize,
naming), `Server.Tests/Routing/InboxReadTests` (read cost, unread, markers,
paging, stream isolation). Not run: a real WhatsApp webhook or media
download, and PostgreSQL.
