# Driver messaging inbox

What is implemented of the
[messaging plan](../architecture/driver-messaging-plan.md). State on
2026-09-23: local only, not deployed, migrations `AddDriverInbox`,
`AddConversationOutbox`, `AddConversationTemplates`,
`AddConversationReadRevisions`, `AddConversationArrivalSequence` and
`AddFiledDriverFiles` applied in production on 2026-09-23 with
integrations disabled; `AddDriverMessageBusinessNumber` (local, not
applied anywhere, one nullable column) follows. No message has been
received from or sent to a real driver.

## Receiving

Messaging (`DriverMessagingWebhookHandlers`) verifies a signed provider
notification's signature and business number and records all of it in
one transaction: what drivers write (`InboxRecorder`), the statuses of
dispatchers' replies, and the statuses of texts other modules asked it to
send (fuel plans). A notification whose commit failed leaves nothing, and
the provider's retry records it once. Fuel planning is told which of its
plans moved after the commit (`IDriverTextObserver`):

- A message is keyed by its provider id under company, channel and business
  number: a repeated notification records it once, and the same id under
  another carrier's number is that carrier's own message.
- A conversation is one participant on one business number. Its driver is
  matched by the WhatsApp number on the driver's contacts, and stays
  unmatched when no driver or more than one has that number.
- Text is kept, bounded at 4096 characters; a file keeps its caption, media
  id, declared type, hash and name; kinds PulsR cannot show (location,
  contact, sticker) are recorded as unsupported under their type name.
- The 24-hour window runs from the conversation's last inbound message
  under the company's current business number; fuel plans are held to the
  same window. A driver who wrote to another number has not opened it.
  The older window rows (`DriverMessagingWindows`) name no number, so
  they open nothing; Messaging keeps writing them only so that revisions
  released before 2026-09-24, which read them, keep working through a
  rolling cutover. Dropping the table is a later explicit cleanup.
- Statuses move forward only, matched by provider id under the business
  number the message went from: replies by their conversation's number,
  fuel plans by the number recorded on the attempt. A fuel plan sent
  before that number was recorded has none and no status moves it; a
  status addressed to a number other than the current one is dropped
  before any of this.

## Sending for other modules

A module that decides what to say asks Messaging to deliver it through
`IDriverTextDelivery` (in `Application/Interfaces`, outside every
feature). Fuel planning (`FuelIssueSender`) builds the words, the
recipient, its key and its own references on the attempt; Messaging
(`DriverTextDelivery`) reads the window, records the attempt with the
business number before calling the provider, asks the requester once
more whether it is still wanted, and keeps the provider's answer. The
same key is one message: a taken attempt settles it, one in flight or
without an answer is not repeated unless the requester says so.

Every send, a fuel plan or a reply, names the business number it was
recorded under. The adapter reads the carrier's credentials once for the
call and sends with exactly those; if they now name another number it
sends nothing, and the attempt is withdrawn. The window is read again
under that number right before the call, after the attempt is committed;
a window that closed meanwhile withdraws it. During a rolling cutover a
revision from before 2026-09-24 still applies fuel statuses by provider
id alone and records attempts without a number; those attempts are then
moved by no status.

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

Dispatch policy: `GET /api/messaging/inbox?unread=&search=&afterAt=&afterId=`
(50 conversations a page, newest first, a fixed number of reads whatever
the number of conversations; `next` in the answer continues below the
last one shown, by its last message time and id, so conversations with
the same time are neither skipped nor repeated; `search` narrows to a
driver's name or three or more digits of a number, at most 100
characters),
`GET /api/messaging/conversations/{id}?beforeSentAt=&beforeCreatedAt=&beforeId=&seen=`
(50 messages per page, newest first, with attachment states; `next`
continues below the last message by its time, when PulsR recorded it and
its id, so messages sharing a time are neither skipped nor repeated;
`before=` alone, the earlier clients' form, still reads everything older
than a time), `POST .../{id}/read` (this
dispatcher's marker: the conversation revision of the view they read; it
only moves forward and never past the current revision). The marker is
one upsert that keeps the higher revision (`ConversationReadMarkers`, SQL
for PostgreSQL and for SQLite in Infrastructure), so two writers in any
order never move it back, and a database failure reaches the request
boundary instead of being reported as "not marked". Unread counts are
each dispatcher's own and tell the driver nothing.

Unread is decided by when PulsR recorded a message, not by the provider's
time: each driver message keeps the conversation revision it was recorded
at (`ArrivedRevision`), a concurrency token that orders the
conversation's commits, and a message is unread while the dispatcher's
marker is below it. A message delivered late with an older time is
therefore still unread. The conversation is read before its messages, so
a marker at the view's revision never covers a message the view did not
contain. Each page also answers `readThrough`, the revision the client
marks: opening a thread reads what was recorded before the driver
messages it shows, as a messenger does, but never an unread driver
message recorded after one it shows and not shown itself, such as one
delivered late with a time among older pages. Such a message keeps the
conversation unread until the page showing it is opened; an older page
is asked for with `seen`, the revision the thread was read at, so one
recorded among pages already shown after they were read stays unread
too. A page showing no driver message lets nothing below it be marked.
The check is one aggregate over the conversation's driver messages above
the reader's marker; its cost grows with those, and it has not been
measured on PostgreSQL.

`GET /api/messaging/unread` is the notice: how many conversations are
unread (up to 99, then `more`) and `newest`, the highest company arrival
sequence among them. Recording a driver message raises the company's
arrival sequence (`ConversationArrivalHeads`, a concurrency token) in the
same transaction, so numbers rise in commit order: a concurrent recording
that read the same number fails as a write conflict and the provider's
retry records it after. A conversation that comes into view because
another was read carries an older number, so `newest` does not rise for
it. Two reads; the database walks the company's conversations that have
driver messages (index `CompanyId, LastInboundSequence`) and this
dispatcher's marker for each, so the work grows with conversations, not
messages. Not measured on PostgreSQL.

`GET /api/messaging/events` is a server-sent event stream of
"conversation changed" signals (id and revision, no content) for the
caller's company, raised after commit, with a keep-alive every 25 seconds.
Signals live in this process only and each subscriber keeps at most 64; a
reader that reconnects or misses signals reads the inbox again.

## Messages page

`/messages` (Admin and Dispatch) follows the Driver Messages prototype
(the published artifact is the reference for its layout): conversations,
the thread and the driver's trip in three panes that fill the window and
scroll on their own. The trip pane shows the driver (name, truck,
WhatsApp number), their hours of service from the fleet's shared snapshot
with how old the reading is, or that Samsara has none, never zeros, and
the current load with its stops and the next one. Below
`messages-trip-beside` (1101px) the trip opens over the thread (Trip),
and the thread's header keeps the truck, load and drive and shift left;
below `md` one pane shows at a time. The context read composes three
owners in the API: Messaging's driver, Execution's work and Fleet's
`GetDriverHosQuery`, keyed by the driver so a co-driver never shows the
truck driver's clocks; it runs when a conversation opens, not per
message. The list searches (by driver name or number, on Enter) and
shows 50 at a time;
"Show more conversations" continues below. A change signal reads the
first page again and keeps the pages shown below it: a conversation that
moved up is shown once, at the top, and those further down keep what was
last read for them until more is asked for. A new search or filter
starts over, and a page still on its way for the earlier list is
dropped.

Opening a conversation reads its messages and its trip side by side, and
the messages show as soon as they arrive: the list, the templates, the
stream join, the trip and the read marker never hold them back, and each
answer is fenced by its conversation and read generation. The read marker
is posted after the messages show, and only while something is unread
for this dispatcher. A signal for the open conversation rereads it beside
the list; a poll tick rereads it only when the list shows it at another
revision. Rereads run one at a time per conversation, and a demand that
comes while one is on its way is read once more after it. Measured with
production's median latencies on the synthetic demo (Chromium, local
build): opening from a cold page went from 1.94 s to 0.42 s after the
first API call, and a switch between conversations from about 0.8 s to
0.29 s. Production itself was not measured.

The thread reads oldest first, marks itself read at the revision it
showed, and claims the conversation, at most once a minute, when the
dispatcher starts typing. Enter sends; Option (Alt) or Shift with Enter
starts a new line, and a key an input method is composing is left to it
(`Scripts/messages/composer.ts`). A reply keeps its retry key until it
is sent; a reply refused as stale offers "Send anyway" with the same key.
A send belongs to its conversation: its answer never clears or blocks
another conversation opened meanwhile.

Files picked with the paperclip or dropped on the conversation wait under
the reply box (`StagedFile`), with a thumbnail for photos, their size and
a remove button, until Send; at most five, photos up to 5 MB and other
files up to 16 MB, as the server takes them. Each is read once and sent
as its own file message, the text as the first one's caption, with the
same stale check as a text reply. A file keeps its retry key and caption,
so "Send again" after a failure is the same message. A file on its way
shows an indeterminate bar: the upload's progress is not measured.
Outside the 24-hour window only approved templates are offered, or a
note that there are none.

**Where a driver's WhatsApp messages go** is one rule,
`Domain/Rules/Fleet/DriverWhatsApp`: the driver's own WhatsApp number when
one is set, otherwise their phone, each only when it is a valid E.164
number. An explicit WhatsApp number that is not valid is reported as
invalid and never replaced by the phone. A phone used this way is only an
address to try: nothing knows it is registered on WhatsApp until the driver
writes, and the list labels it "(phone)". The driver list, opening a
conversation, matching an inbound number to its driver (exactly one driver
must resolve to it) and the fuel hand-over's recipient all use the rule;
database filters use the same choice through
`Application/Features/Fleet/Services/DriverRecipients`, and the rule still
decides per row. A conversation's number never changes: after a driver's
phone or WhatsApp number is edited, the history and anything queued stay
with the old number, and choosing the driver opens a conversation for the
new one.

Below the conversations, **Drivers without a chat**
(`Pages/Messages/MessagingDrivers`) lists the active drivers who have such
a number and no conversation yet for it on the number the company sends
from (`GET /api/messaging/drivers?withoutConversation=true`). A page may
hold fewer than 50 drivers, since rows whose stored number is unusable are
read and skipped. It follows the list's search
and the chosen driver group, pages 50 at a time by name, and is hidden
under Unread. Conversations with unknown numbers stay in the list above.
Choosing a driver (`POST .../drivers/{id}/conversation`,
`DriverConversations`) opens the driver's conversation on the company's
current number, or creates it with no message, no reply window and
nothing unread for anyone, so its first message is an approved template.
Choosing sends nothing. Two dispatchers choosing at once get one
conversation: the conversation's unique key refuses the second insert,
which reads the first. Without WhatsApp settings, or for a driver without
a WhatsApp number, nothing is created (409). An empty conversation stays
in the list as "No messages yet"; nothing removes it. The drivers list
is read when the page opens, when the search or the group changes, and
not on every signal: a driver whose conversation a colleague created
meanwhile still opens that one.

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
most every 30 seconds and reconnects with backoff (2 to 60 seconds). In
production the browser reaches the API through Firebase Hosting's
`/api/**` rewrite, which may hold a streamed response back until it ends.
A stream that sends no headers within 10 seconds, or no line within 40
(the server sends a keep-alive every 25), therefore counts as down and the
poll ticks carry the views. Whether Hosting passes the stream through live
is checked only after release. Each
view reads again on a signal and discards an answer older than one it
already has.

Every page shows the unread count beside Messages in the navigation
(`Services/MessagingNotices.cs`, `Shared/MessagesNotice`), which keeps the
tab joined to the stream on every page. Each tab reads the count once when
it starts and when the account changes. After that only the leading tab
reads it: a burst of signals becomes one read after 500 ms, and the count
goes to every tab of the account over the channel, so a browser reads once
per burst however many tabs are open. A tab that marks a conversation read
tells the others ("read"), and the leader counts again.

Browser notifications are opt-in on the Messages page ("Notify me of new
messages"), per browser. Only the leading tab notifies, and only when
`newest` rises above the highest it has counted, one number however long
the session; reads, claims, replies and older conversations coming into
view never raise it, and the first count after a start or an account
change is only a baseline. A notification
names no driver and quotes nothing, is skipped while the tab is in front,
and shares one tag so a newer one replaces an older one. Every result
carries the account generation it was asked for and is checked again
right before it is shown, relayed or notified, so nothing from an earlier
account or a disposed notice gets through. Limit: a leader that is
hidden while another PulsR tab is in front still notifies.

## Driver, trip and filing

Beside a conversation (`GET .../conversations/{id}/context`): the driver
and the truck they are on. Trucks come from the driver's planned and
active execution legs, as driver or co-driver; only without such a leg,
from the fleet's assignment of the truck (one driver per truck). On one
truck, that truck's current and upcoming loads as the Dispatch board reads
them (`ExecutionWorkReader`), at most five; on several, all trucks are
listed and no load is offered, since choosing one would be a guess. Read
when a conversation opens or its driver changes, not per message.

When the number matched no driver, or the wrong one, a dispatcher chooses
the driver (`PUT .../conversations/{id}/driver`) at the revision they saw;
a newer revision refuses it, and the change is signalled after commit.

A file a driver sent is filed to a load only when a dispatcher presses
File (`POST /api/messaging/attachments/{id}/file`, owned by the Dispatch
documents): a suggested load of the driver's, or any load by its number.
The load document refers to the stored file and copies no bytes; the same
file on the same load is filed once. Only a released PDF, PNG or JPEG up
to 5 MB is filed. Everything is read and checked inside the committing
transaction, and the stored file's row is claimed there on the checked
state, so a quarantine or removal that commits after the check stops the
filing. The download reads a filed document through the file store, which
now bounds every read to the recorded length and hash, and refuses a file
that is no longer released or no longer what was recorded; the auditor's
`dispatch.filed-document-unavailable` finds such documents.

Unlike the plan's `MessageLinks`, suggestions are not stored: they are
computed from the context each time, and a confirmed filing is the load
document itself (`SourceAttachmentId`, unique per load), so there is no
link that could outlive or contradict its document.

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

**Throughput, assessed and deliberately unchanged (September 25).** The
worker sends one message at a time: each pass takes up to ten queued
messages per company, oldest first, and companies are passed one after
another. A provider that does not answer holds the next message for up to
its request timeout (20 seconds; a file also uploads first), so ten slow
answers delay a company's later replies by minutes, and another company's
by as long. Nothing has been measured, because production has never sent a
message and serves one company. Sending in parallel would need the fence
per message kept exactly as it is (it already makes concurrent workers
safe) and a per-company and provider-rate bound. Revisit when a queued
reply waits longer than a minute in production, or a second company sends;
until then a slow provider shows as "Waiting to send" and nothing is lost.

## Files and templates out

- `POST /api/messaging/conversations/{id}/files` (form upload, at most
  16 MiB for now because uploaded forms are buffered by the server): the
  browser states the file's SHA-256; the file is stored under the retry key
  as its id in `Sent/{day}`, checked, and queued only when it may be sent,
  within WhatsApp's limits per kind (images 5 MiB, audio and video
  16 MiB). The outbox uploads it to WhatsApp and sends it by media id; a
  refused upload sends nothing. A file no longer available when its turn
  comes is withdrawn without a call.
- Templates are the carrier's own (`ApprovedTemplates`: company, channel,
  business number, name, language, number of parameters, text). An
  administrator records those Meta approved for the number the company
  sends from now (`GET/POST /api/settings/integrations/whatsapp/templates`,
  `DELETE .../{id}`, in the WhatsApp card of Settings, Integrations);
  PulsR does not ask Meta, so one Meta did not approve is refused by it
  when sent. The deployment-wide `Messaging:Templates` setting is gone;
  production had none.
  `GET /api/messaging/templates` lists those of the current number.
  `POST .../conversations/{id}/templates` queues one only while it is
  approved for the number the conversation is on and that number is the
  one the carrier sends from; the worker checks both again before the
  call and withdraws a reply whose template was removed or whose number
  changed. A template may go outside the 24-hour window.
- `GET /api/messaging/attachments/{id}/content` serves a file only after
  it passed its check.

## Tests

`Server.Tests/Messaging/DriverMessagingWebhookTests` (inbound recording,
duplicates, business numbers, files, unsupported kinds, ambiguous
numbers, reply statuses, a failed commit retried, fuel-plan statuses only
under the number they went from, a window under another number),
`Server.Tests/Messaging/InboundMediaTests` (copy, hash mismatch, backoff,
expiry, leases, a crash between the updates, a lost finalize, naming),
`Server.Tests/Messaging/InboxReadTests` (read cost, unread, markers,
paging, 120 conversations continued past 50 with 70 at one time and a
late arrival, search, stream isolation),
`Server.Tests/Messaging/ConversationHistoryTests` (120 messages at one
time read to the end, a late message below the page kept unread until
shown, one among pages already shown not marked),
`Server.Tests/Messaging/ConversationReplyTests` (queue and send once,
window, retry keys, stale replies, a take overtaken before sending, a lease lost mid-send, a late answer on its own
attempt, withdrawal for a closed window or changed number, explicit retry,
claims), `Server.Tests/Messaging/ApprovedTemplateTests` (recorded for this
carrier's current number only, refused as Meta would, administrators
only), `Server.Tests/Messaging/ConversationFileTemplateTests` (another
carrier's or number's template neither offered nor queued, one removed
or a number changed before the send not sent, a file sent
once, a disguised file refused, an unreadable file withdrawn, approved
templates only, download of checked files only),
`Server.Tests/Storage/StoredFileCheckTests`,
`Server.Tests/Messaging/LocalDriverMessagingTests` (refused outside
Development, no network, signed simulation). Client:
`Client.Tests/Messaging/MessagesTripTests` (hours with their age and the
load beside the thread, missing hours said plainly, Trip opens and
closes, "You" and one's own claim), `Server.Tests/Fleet/DriverHosReadTests`
(a driver's own clocks, never a co-driver's, none when unknown),
`Client.Tests/Messaging/WhatsAppTemplatesTests` (a refused template
keeps its draft, removal only after confirming),
`Client.Tests/Messaging/MessagesPageTests` (list and thread, read marker,
stale reply confirmed with the same key, closed window without templates,
a stream signal reads the open thread again, more conversations kept
through a refresh, a late page for an earlier search dropped, earlier
messages by cursor marking only what the server allows, and as before
against a server without it),
`Client.Tests/Messaging/MessagingSignalsTests` (stream lines, account scope
and rejoin on account change, local stream and polling after a failed
import or join, a slow tick that outlasts the wait),
`Client.Tests/Messaging/MessagingNoticesTests` (one read and one relay per
burst on the leader, none on a follower, only a risen arrival notifies,
an account change or disposal during the relay or the import notifies
nothing, an old account's answer dropped),
`Client.Tests/Messaging/MessageFilingPageTests` (File pressed files the
suggested load, a number on several trucks, choosing the driver),
`Client/tests/messaging/messagingChannel.test.js` (one leader per account,
relay, hand-over, separate accounts, an ended join's lock, counts and read
marks between tabs, every tab leading without BroadcastChannel or Web
Locks), `Client/tests/messaging/messagingNotices.test.js` (opt-in, tab in
front, refusal, no API) and the offline UI smoke (`/messages`, and the
navigation count on Dispatch). Server: `MessageFilingTests` (filed once
by reference, company, kind and state, by number, a quarantine after the
check, download defenses, auditor), `ConversationContextTests` (driver
link at a revision, co-driver, several trucks, fleet fallback),
`UnreadNoticeTests` (fixed reads, late message, an older conversation
coming into view, arrivals in commit order, the marker keeping the higher
of two writers).
Browser: `Client/tests/browser/messagingTabsSmoke.mjs`, in the release UI
gate (two tabs on the release build with synthetic fixtures: one leader,
relay, hand-over on close, sign-out and sign-in as another account).
`MessageSwitchTests` (an editor or a request open on one conversation or
file does not carry to another). Not run: a real WhatsApp webhook or media
download, and a real notification permission. On the isolated PostgreSQL
fixture, `MessagingPostgresTests` applies the whole migration chain with
its backfills over existing rows, and exercises the read-marker upsert,
the arrival head's commit order, the filing's row claim and the new
auditor reads.

Auditor coverage of delivery ownership: that a status moved only an
attempt from its own business number cannot be detected afterwards (no
record says which notification moved it), so it rests on the regression
and on the query's scope, not on a runtime check. Fuel attempts recorded
before the number was kept have none and stay at their last status; none
are expected in production, where sending was never configured, but that
count has not been read.
