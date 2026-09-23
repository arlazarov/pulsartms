# Driver messaging inbox

What is implemented of the
[messaging plan](../architecture/driver-messaging-plan.md). State on
2026-09-23: local only, not deployed, migrations `AddDriverInbox`,
`AddConversationOutbox` and `AddConversationTemplates` not applied
anywhere, and no message has been received from or sent to a real driver.

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

Every stored file is checked before it is released (`StoredFileCheck`):
its first bytes must be the kind it was declared as, and that kind one
PulsR shows and sends (PDF, JPEG, PNG, WebP, OGG, MP3, AAC, MP4). Anything
else is refused and never served. The inbound worker checks a file as soon
as it is stored; the storage reconciler checks any file left quarantined.

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

## Messages page

`/messages` (Admin and Dispatch) lists conversations beside the open one;
below the `md` breakpoint it shows one pane at a time. The thread reads
oldest first, marks itself read through its newest message, and claims the
conversation, at most once a minute, when the dispatcher starts typing.
A reply keeps its retry key until it is sent; a reply refused as stale
offers "Send anyway" with the same key. Outside the 24-hour window only
approved templates are offered, or a note that there are none.

One stream per browser and account, not per tab: every tab showing
messages joins `Scripts/shared/messagingChannel.ts` under a scope naming
the signed-in account and this sign-in's session. A Web Lock of that scope
elects one tab, which reads `/api/messaging/events` through the app's
authenticated client (`Services/MessagingSignals.cs`) and relays each
signal to the other tabs over a BroadcastChannel of the same scope, so a
tab signed in as someone else never shares its leader. A sign-in, sign-out
or account change leaves and joins again. Without both BroadcastChannel and
Web Locks, or when the channel module cannot be loaded or joined, every tab
reads its own stream and signals only itself. After every connect the
reader sends "resync"; while the stream is down it sends a "poll" tick at
most every 30 seconds and reconnects with backoff (2 to 60 seconds). Each
view reads again on a signal and discards an answer older than one it
already has.

Outside `/messages` nothing is signalled yet: a tab joins only while the
Messages page is shown. A shell-level notice (navigation unread count and
opt-in browser notifications) is the next stage.

## Local provider

For a developer's machine, `WhatsApp:Provider = local` replaces the Cloud
API adapter with `LocalDriverMessaging`. Anything but Development refuses
it at start. It sends nothing: every send is accepted under a `local.` id.
A driver message is simulated by posting a Cloud API notification to the
company's ordinary webhook address, addressed to
`WhatsApp:Local:PhoneNumberId` (default `local`) and signed with
`WhatsApp:Local:AppSecret` (`X-Hub-Signature-256: sha256=<HMAC>`); without
that secret nothing is accepted. Every media id opens the same synthetic
1x1 PNG, so the capture pipeline can be followed end to end.

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

## Files and templates out

- `POST /api/messaging/conversations/{id}/files` (form upload, at most
  16 MiB for now because uploaded forms are buffered by the server): the
  browser states the file's SHA-256; the file is stored under the retry key
  as its id in `Sent/{day}`, checked, and queued only when it may be sent,
  within WhatsApp's limits per kind (images 5 MiB, audio and video
  16 MiB). The outbox uploads it to WhatsApp and sends it by media id; a
  refused upload sends nothing. A file no longer available when its turn
  comes is withdrawn without a call.
- `GET /api/messaging/templates` lists the templates configured as
  approved (`Messaging:Templates`: name, language, number of parameters,
  text). None are configured until Meta approves some.
  `POST .../conversations/{id}/templates` sends one with its parameters
  filled in; a template may go outside the 24-hour window, but still only
  from the carrier's current business number.
- `GET /api/messaging/attachments/{id}/content` serves a file only after
  it passed its check.

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
claims), `Server.Tests/Routing/ConversationFileTemplateTests` (a file sent
once, a disguised file refused, an unreadable file withdrawn, approved
templates only, download of checked files only),
`Server.Tests/Storage/StoredFileCheckTests`,
`Server.Tests/Routing/LocalDriverMessagingTests` (refused outside
Development, no network, signed simulation). Client:
`Client.Tests/Routing/MessagesPageTests` (list and thread, read marker,
stale reply confirmed with the same key, closed window without templates,
a stream signal reads the open thread again),
`Client.Tests/Routing/MessagingSignalsTests` (stream lines, account scope
and rejoin on account change, local stream and polling after a failed
import or join, a slow tick that outlasts the wait),
`Client/tests/messaging/messagingChannel.test.js` (one leader per account,
relay, hand-over, separate accounts, every tab leading without
BroadcastChannel or Web Locks) and the `/messages` page in the offline UI
smoke. Not run: a real WhatsApp webhook or media download, several real
browser tabs, and PostgreSQL.
