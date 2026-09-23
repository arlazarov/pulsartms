# Driver messaging inside PulsR: plan

Status: a proposal written on 2026-09-23 from the code at `cba763cc` and the
official Meta and Google documentation cited below. Nothing here is
implemented, enabled or approved. No message is sent, no cloud folder is
created, no access is granted and no data is moved by this document. The
interactive prototype that accompanies it uses synthetic data only:
<https://claude.ai/artifact/E5ogEDLRCsbmon2zXDTfGg> (private to its owner
until shared). It shows the inbox with unread and needs-reply filters, a
thread with delivery states, a driver file checked and then filed to a load
by confirmation, the reply guard when a colleague or the driver moved
first, the closed 24-hour window, a refused and an unanswered send, the
trip beside the conversation, notifications, and phone layout.

## Goal

Dispatchers do all WhatsApp work with drivers inside PulsR: a list of
conversations with unread counts, the conversation itself, files in both
directions with previews, the driver's current trip beside it, a composer,
honest delivery states and notifications of new messages, on desktop and
on a phone. Several dispatchers share the inbox without answering the same
message twice. A file a driver sends is filed to a load only when a
dispatcher confirms where it belongs.

## What exists today

Read from the code; see
[fuel planning rules](../features/fuel-planning-rules.md#sending-over-whatsapp)
and [integration settings](../features/integration-settings.md).

- `IDriverMessaging` (Routing) with `WhatsAppCloudMessaging` (Infrastructure)
  sends text only, one request, no retry; an unanswered call is unknown.
- `DriverMessages` holds outbound fuel hand-overs: the fixed text, recipient,
  visits, an idempotency key with attempts, the provider message id and a
  status that only moves forward.
- `WhatsAppWebhookHandlers` verify the signature and the business number,
  apply statuses by provider message id, and record only **when** a number
  last wrote (`DriverMessagingWindows`). **The text and files of inbound
  messages are not stored.** There is no inbox.
- Driver contacts: a WhatsApp number per driver, never taken from the phone.
- Load documents (`DispatchDocument`) keep their bytes in PostgreSQL: PDF,
  PNG and JPEG by signature, 5 MiB each, 50 per load, downloaded only on an
  explicit authenticated request.
- The Client polls: the board, Fleet Map and planning pages every ten
  seconds. There is no server push, SSE, WebSocket or service worker.
- Automatic fuel sending is a setting that stays off and is wired to
  nothing.

## External constraints (verified 2026-09-23)

WhatsApp Cloud API
([media](https://developers.facebook.com/docs/whatsapp/cloud-api/reference/media),
[mark as read](https://developers.facebook.com/docs/whatsapp/cloud-api/guides/mark-message-as-read),
[webhooks](https://developers.facebook.com/docs/whatsapp/cloud-api/webhooks)):

| Fact | Consequence here |
| --- | --- |
| Documents up to 100 MB; images 5 MB (JPEG, PNG); audio and video 16 MB; stickers up to 500 KB | Size caps are per kind; our own cap can be lower, never higher |
| A media id received by webhook expires after 7 days; an uploaded one after 30 | Inbound files are copied to our storage promptly; a failed copy has a deadline and is shown as failed after it |
| The download URL from `GET /{media-id}` is valid for 5 minutes and needs the access token | Never stored, never given to a browser; fetched immediately before each download |
| The media response carries a `sha256` | Verified after download; used for de-duplication |
| Marking read: `status: read` for a message id, within 30 days; earlier messages are marked too | "Read by dispatcher" is our own state; telling WhatsApp is an explicit, separate step |
| Free-form messages only within 24 hours of the driver's last message; otherwise an approved template | Composer shows the window; outside it only templates (none approved yet) |
| Webhooks are retried and can arrive out of order and more than once | Receipts are de-duplicated and statuses only move forward (already true for statuses) |

Google Drive
([API limits](https://developers.google.com/workspace/drive/api/guides/limits),
[shared drive limits](https://support.google.com/a/answer/7338880)):

| Fact | Consequence |
| --- | --- |
| 750 GB upload per user per 24 hours, across My Drive and shared drives | A single integration identity is capped per day |
| A shared drive holds at most 500,000 items, including trash | Per-message files would need several drives or periodic archiving |
| Quota units per minute per project and per user; list and download cost more than reads; 403/429 require backoff | Every file needs a queue with backoff, not a request on page load |
| Up to 100 nested folder levels | Folder-per-company/driver/month layouts are possible |

## Proposed ownership

One owner for conversations, messages and attachments, inside the module
that already owns driver messaging: **Routing**. It owns the tables below,
the outbox, inbound processing, the inbox and conversation reads, and links
from messages to work. The existing fuel hand-over becomes one caller of it
instead of a second message store.

- Providers stay behind Application interfaces: `IDriverMessaging` (sending,
  webhook reading, media fetch and upload) grows media operations;
  WhatsApp stays in Infrastructure. A later provider (another messenger,
  SMS) is another adapter.
- Files go through a provider-independent `IFileStorage` declared in
  `Application.Interfaces`, outside every feature module: put a stream with a
  content type and sha256, open a stream by key, delete by key, list by
  prefix for reconciliation. Keys are opaque and tenant-scoped; the core
  never sees a bucket, a Drive folder or a URL. Adapters: database (today's
  bytea, for the first stage and tests), object storage, and optionally
  Drive.
- **Module boundary, without new edges.** `ModuleDependencyTests` records
  the dependencies between feature modules and fails on a new one; AGENTS
  forbids weakening it or adding exceptions. The composition above needs
  none: Routing already depends on Fleet (drivers), Execution and Eta (trip
  context) and Dispatch (load documents); Dispatch already depends on
  Routing; and `IFileStorage` outside the features adds no edge for either.
  API reaches all of it through MediatR commands and queries as usual. A
  separate Messaging module would need new recorded edges; that is an
  owner's decision to take explicitly later, not something this plan
  assumes. The cost of staying in Routing is a larger Routing module.

## Data model

Metadata in PostgreSQL; bytes in storage. No bytes or base64 in
`ReadCache`, the planning summary, the display caches or any in-memory
cache.

| Table | Holds | Keys and guards |
| --- | --- | --- |
| `Conversations` | company, driver (nullable until matched), channel, business number id (the carrier's sending identity), participant number (E.164), last inbound at, last message at/preview, state (open/archived), claimed by + until | unique (company, channel, business number, participant): a driver writing to two carrier numbers is two conversations |
| `Messages` | conversation, direction, kind (text/file/template/system), body text (bounded), author user for outbound, reply-to, provider message id, idempotency key + attempt, status (sending/unknown/rejected/accepted/sent/delivered/read/failed/withdrawn), status at, error code, created at | unique (company, channel, business number, provider id); unique (company, channel, business number, idempotency key, attempt). Today's `DriverMessages` keys omit the business number because a carrier has one; they are widened in the same migration |
| `StoredFiles` | storage key, content type (sniffed), size, sha256, state (pending/quarantined/available/deleting), created at | one row per stored object; the only owner of the object |
| `MessageAttachments` | message, stored file, original name, provider media id + expires at, download state (pending/stored/failed), attempts, next attempt at, failure reason | references a stored file; never owns bytes |
| `MessageLinks` | message or attachment → load, stop, execution leg or load document; state suggested/confirmed/rejected; who and when | a file is filed as a load document only through a confirmed link |
| `ConversationReads` | user, conversation, last read message | per-user unread; not provider "read" |
| `MessagingOutbox` | message to send, not before, lease owner, lease until, fencing token | see Outbox below: a lease plus a fencing token, not an exactly-once guarantee |
| `WebhookReceipts` | provider, business number id, event hash, received at | de-duplication window per business number; pruned |

Load documents reference `StoredFiles` too once they move out of bytea, so a
file filed from a conversation is one object with two references, not a
copy. `DriverMessages` and `FuelVisitSends.MessageId` migrate into `Messages`
without losing history; `DriverMessagingWindows` becomes
`Conversations.LastInboundAt`. Every idempotency and de-duplication key
carries company, channel and business number: several carrier numbers must
never collide.

## Flows

**Inbound.** The webhook verifies signature and business number, writes the
receipt, the message and a *pending* attachment row in one transaction, and
answers 200. A media worker then asks for the 5-minute URL, streams the
download with the kind's size cap, checks the sha256, writes to storage as
*quarantined*, and only then sniffs the type against an allowlist; a file
that passes becomes *available* for preview and filing, one that does not
stays quarantined and is never served. Failures back off and retry until the
media id's 7-day expiry, then the attachment is failed and visible as such.
The worker never keeps a file in memory beyond a bounded stream buffer.
Because media ids expire in seven days, this worker ships with the first
stage that accepts inbound media at all; a stage that records media
metadata without it would promise files it may lose.

**Outbound text.** The composer carries an idempotency key and the id of the
last message the dispatcher saw. The command refuses with 409 when a newer
inbound message or a colleague's reply arrived since, unless the dispatcher
confirms; otherwise it commits the message and an outbox row.

**Outbox.** A lease alone does not make one sender: a worker can pause past
its lease, lose it, and still reach the provider. So:

- Taking a row increments its fencing token in the same conditional update
  (`… WHERE lease_until < now() OR lease_owner IS NULL`); the worker keeps
  the token it got.
- Before the provider call the worker commits `sending` with a conditional
  update on its token. If that fails, another worker owns the row and this
  one stops without calling.
- After the call it records the result with the same conditional update.
  If that fails, a newer holder exists; the provider message id it received
  is still written, because the provider's answer is a fact, but only onto a
  row still `sending` or `unknown`, and never as a second send.
- Crash windows: a crash before the call leaves `sending` with an expired
  lease; a crash after the call and before recording leaves the same. From
  outside they look alike, so an expired `sending` becomes `unknown`, never a
  new send. Unknown is sent again only by an explicit dispatcher action, as
  the fuel hand-over already does.
- The database and the provider cannot be committed together. What is
  promised is no silent duplicate from our side; exactly-once delivery is
  not promised, and a status webhook for a message id we never recorded is
  kept as an orphan receipt for review rather than guessed onto a row.

**Outbound file.** The dispatcher picks a load document or a local file; the
file is stored first, then uploaded to the provider's media endpoint and
sent by media id. A failure keeps the stored file and the message as
failed.

**Linking a file to a load.** Suggestions come from the driver's current
work (the same planning reader), the stop being worked and the file kind.
A suggestion is never applied silently: the dispatcher confirms "File to
AMF1407 · POD" or chooses another load; until then the file stays in the
conversation as unfiled. Confirming creates the load document by reference
to the stored object, not by copying bytes.

**Delete and retention.** A stored file is deleted only when nothing
references it: deleting a message attachment or a load document removes a
reference, and the `StoredFiles` row moves to *deleting* only when the last
reference is gone, checked in the same transaction. The storage delete runs
after that commit. Retention is a company setting, off until chosen.

**Reconciliation.** A periodic pass lists storage by prefix. An object with
no `StoredFiles` row is removed only when it is older than a grace period
(long enough to cover an upload between the object write and its row
commit), is not named by a pending upload or a live lease, and the database
still shows no row when checked again immediately before the delete. Rows
whose object is missing are reported and shown as missing, never repaired
by guessing.

## Consistency

The [consistency contract](fleet-efficiency.md#consistency-contract)
applies. Messages depend on conversation and provider id; statuses only move
forward and apply to their own provider id; an attachment is stored before
it is marked stored, and released from quarantine before any link can file
it. Storage and database are not atomic: an object written before its row
commits is an orphan only after the grace period (see Reconciliation); a
row without its object is shown as missing, never as an empty file.

## Notifications and real time

A ten-second poll is fine for the board and far too slow for an open
conversation. One app-level channel per browser carries messaging events;
pages do not each open their own, and tabs do not each poll.

| Option | Latency | Cost and risk here |
| --- | --- | --- |
| Per-page polling (today's pattern) | up to the poll interval | duplicated across pages and tabs; ten seconds is visible in a chat |
| One SSE stream per browser | about a second | one long request per browser; Cloud Run supports streaming responses up to the request timeout, so the stream reconnects; one instance today, so no fan-out between instances yet |
| SignalR (WebSockets, SSE or long-poll fallback) | about a second | a hub, sticky connections and a backplane once there are several instances |
| Bounded polling fallback | the fallback interval | used only while the stream is down |

Recommended: one server-sent event stream per browser, opened by one tab
elected with a Web Lock and shared with the other tabs through a
`BroadcastChannel`. Events are small ("conversation 42 changed, version
17"); the tab that needs the content reads it once. When the stream is down,
the same elected tab falls back to one bounded poll of the inbox summary,
never one per page. The summary read is company-scoped, generation-cached
and invalidated after an inbound commit; there is never a request per card
or per conversation. SignalR stays an option if two-way features (typing
indicators, presence) become worth a hub. Several instances need a fan-out
(for example the existing database invalidation relay or a pub/sub) before
either choice scales out.

Delivered is not read, and a dispatcher opening a conversation is not the
driver reading anything.

| Where the dispatcher is | How they learn of a message |
| --- | --- |
| On Messages | the conversation updates in place |
| Elsewhere in PulsR | badge on Messages and a short card |
| Another tab or app | browser notification (Notification API), after permission |
| PulsR closed, or on a phone | Web Push with a service worker: a later stage; PulsR has no service worker today and its caching rules would need review |
| Native app | out of scope |

## Storage: Drive or object storage

| Question | Object storage (for example Cloud Storage, same GCP project) | Google Drive (shared drive) |
| --- | --- | --- |
| Identity and ownership | service account; private bucket; the app is the only reader | OAuth user or domain-delegated service account; files in a shared drive belong to the drive, not to a person |
| Access for people | only through PulsR | people can browse the folder in Drive, which is the point and also a second access path to secure |
| Limits that matter | none at this scale beyond request rates | 750 GB per user per day; 500,000 items per shared drive; per-minute quota units |
| Tenant isolation | key prefix per company, enforced by the app | a drive or folder per company, enforced by Drive sharing |
| Lifecycle | retention and delete by prefix, lifecycle rules | trash and retention through the Drive API |
| Pricing model | per stored GB and operations | Workspace plan storage pool |

No cost figures are given here: they depend on plan and region and were not
measured. Recommendation: object storage as the primary store behind
`IFileStorage`; Drive as an optional, explicit export or mirror of filed
load documents for people who work in Drive, not as the system of record.

## Stages

1. **Inbox, read only.** Store inbound text; conversations and unread
   counts; the Messages page; the app-level event channel. Inbound files are
   downloaded durably into quarantine from the start (media worker and
   storage adapter), because their media ids expire in seven days; they are
   listed but not yet previewed or filed. No sending beyond today's fuel
   plan.
2. **Reply.** Composer within the 24-hour window, outbox with fencing and
   statuses, claim and the stale-reply guard, per-user read state.
3. **Files in, usable.** Type checks release quarantined files; previews for
   PDF and images; manual filing to a load; stored-file references shared
   with load documents.
4. **Files out and templates.** Sending load documents; approved templates
   for outside the window.
5. **Notifications beyond the tab.** Browser notifications, then Web Push.
6. **Extensions.** Drive mirror, search across conversations, retention,
   several business numbers, a second provider.

## Tests to add with each stage

- Webhook: signature, business number, duplicate and out-of-order events,
  another carrier's number and message ids.
- Media: size caps per kind, type sniffing against the declared type, sha256
  mismatch, the 5-minute URL expiring mid-download, the 7-day deadline.
- Storage: put/open/delete through a fake adapter; orphans and missing
  objects found by the reconciler; nothing written to shared caches.
- Outbox: a worker that lost its lease cannot mark `sending` or record a
  result over the newer holder (fencing); an expired `sending` becomes
  unknown and is not resent; a late provider answer fills in the message id
  without a second send; explicit resend only.
- Keys: the same provider message id and idempotency key under two business
  numbers are two rows, not a conflict or a merge.
- Reconciliation: an object inside the grace period, or named by a pending
  upload, is kept; an object whose row appears between listing and delete
  is kept; a file referenced by a load document survives deleting the
  message.
- Real time: one stream for several tabs; fallback polling from one tab
  only; no page-level polling added.
- Collaboration: two dispatchers replying to the same message; the stale
  reply refused with 409 and the newer message shown.
- Linking: suggestion never files; confirmation files once; another
  carrier's load never offered.
- Reads: one summary read per poll regardless of conversation count
  (query-count regression).

## Open questions

1. Is messaging inside Routing acceptable for the first stages, with a
   separate module left as an explicit later decision about the recorded
   edges?
2. Object storage bucket and service account, or Drive, as the system of
   record? (Recommended: object storage.)
3. Retention period for messages and files, and who may delete.
4. Which file kinds to accept from drivers beyond PDF and images (audio
   notes, video)?
5. Should opening a conversation mark messages read on WhatsApp (blue
   ticks for the driver), or only on an explicit action?
6. Which templates to submit to Meta for approval (for example "New fuel
   plan - reply to receive it")?
7. One shared inbox, or conversations assigned to a dispatcher by truck?
8. Virus scanning for inbound files: required before download is allowed?
