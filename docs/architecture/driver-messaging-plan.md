# Driver messaging inside PulsR: plan

Status: a proposal written on 2026-09-23 from the code at `cba763cc` and the
official Meta and Google documentation cited below. Nothing here is
implemented, enabled or approved. No message is sent, no cloud folder is
created, no access is granted and no data is moved by this document. The
interactive prototype that accompanies it uses synthetic data only.

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

One owner for conversations, messages and attachments: a **Messaging**
module. It owns the tables below, the outbox, inbound processing, reads for
the inbox and conversation, and links from messages to work. The existing
fuel hand-over becomes one caller of it instead of a second message store.

- Providers stay behind Application interfaces: `IDriverMessaging` (sending,
  webhook reading, media fetch and upload) grows media operations;
  WhatsApp stays in Infrastructure. A later provider (another messenger,
  SMS) is another adapter.
- Files go through a provider-independent `IFileStorage`: put a stream with
  a content type and sha256, open a stream by key, delete by key, list by
  prefix for reconciliation. Keys are opaque and tenant-scoped
  (`company/{id}/messages/{attachment}`); the core never sees a bucket, a
  Drive folder or a URL. Adapters: database (today's bytea, for the MVP and
  tests), object storage, and optionally Drive.
- **Module boundary:** `ModuleDependencyTests` forbids new edges between
  feature modules. Today driver messaging lives in Routing. A Messaging
  module that Routing (fuel hand-over), Dispatch (documents) and Fleet
  (drivers) touch needs new recorded edges, which the test says never to
  add to make a change pass. This needs the owner's decision before code:
  either Messaging is a leaf reached only through Application interfaces
  declared in it, with the edge set changed deliberately and reviewed, or
  it stays inside Routing. The plan assumes the first.

## Data model

Metadata in PostgreSQL; bytes in storage. No bytes or base64 in
`ReadCache`, the planning summary, the display caches or any in-memory
cache.

| Table | Holds | Keys and guards |
| --- | --- | --- |
| `Conversations` | company, driver (nullable until matched), channel, participant number (E.164), last inbound at, last message at/preview, state (open/archived), claimed by + until | unique (company, channel, participant) |
| `Messages` | conversation, direction, kind (text/file/template/system), body text (bounded), author user for outbound, reply-to, provider message id, idempotency key + attempt, status (sending/unknown/rejected/accepted/sent/delivered/read/failed/withdrawn), status at, error code, created at | unique (company, provider id); unique (company, idempotency key, attempt) — the shape `DriverMessages` already has |
| `MessageAttachments` | message, original name, content type (sniffed), size, sha256, storage key, provider media id + expires at, state (pending/stored/failed/deleted), attempts, next attempt at, failure reason | unique (company, sha256, message); storage key unique |
| `MessageLinks` | message or attachment → load, stop, execution leg or load document; state suggested/confirmed/rejected; who and when | a file is filed as a load document only through a confirmed link |
| `ConversationReads` | user, conversation, last read message | per-user unread; not provider "read" |
| `MessagingOutbox` | message to send, not before, attempts, lease owner/until | exactly one sender per row by lease; a lost answer becomes unknown, never retried silently |
| `WebhookReceipts` | provider, event hash, received at | de-duplication window; pruned |

`DriverMessages` and `FuelVisitSends.MessageId` migrate into `Messages`
without losing history; `DriverMessagingWindows` becomes
`Conversations.LastInboundAt`.

## Flows

**Inbound.** The webhook verifies signature and business number, writes the
receipt, the message and a *pending* attachment row in one transaction, and
answers 200. A media worker then asks for the 5-minute URL, streams the
download with the kind's size cap, sniffs the type against an allowlist,
checks the sha256, writes to storage and marks the row stored. Failures back
off and retry until the media id's 7-day expiry, then the attachment is
failed and visible as such. The worker never keeps a file in memory beyond a
bounded stream buffer.

**Outbound text.** The composer carries an idempotency key and the id of the
last message the dispatcher saw. The command refuses with 409 when a newer
inbound message or a colleague's reply arrived since, unless the dispatcher
confirms; otherwise it commits the message and an outbox row. The sender
leases the row, calls the provider once, and records accepted, rejected or
unknown. Unknown is sent again only by an explicit dispatcher action, as the
fuel hand-over already does.

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

**Delete and retention.** Deleting marks the row deleted, removes the link,
and queues the storage delete; a periodic reconciler lists storage by
prefix and removes objects without a live row, and reports rows whose object
is missing. Retention is a company setting, off until chosen.

## Consistency

The [consistency contract](fleet-efficiency.md#consistency-contract)
applies. Messages depend on conversation and provider id; statuses only move
forward and apply to their own provider id; an attachment is stored before
it is marked stored, and marked stored before any link can file it. Storage
and database are not atomic: a stored object without a row is an orphan
for the reconciler; a row without its object is shown as missing, never as
an empty file.

## Notifications

Unread counts come from one bounded, company-scoped inbox summary read,
generation-cached and invalidated after an inbound commit. It rides the
existing ten-second cadence of pages that already poll, or one additional
poll only while the Messages page is closed; never a request per card or per
conversation. Delivered is not read, and a dispatcher opening a
conversation is not the driver reading anything.

| Option | Reaches | Needs | Cost |
| --- | --- | --- | --- |
| In-app badge and toast | an open PulsR tab | the summary read above | smallest; recommended first |
| Browser notification (Notification API) | an open tab in the background | per-user permission | small; second |
| Web Push (service worker, VAPID) | a closed tab, a phone browser | a service worker, subscription storage, push sender | the app has no service worker today; needs its caching rules reviewed |
| Server push (SSE) for the inbox | open tabs, lower latency than polling | a streaming endpoint on Cloud Run (one instance today) | replaces polling for this one read |
| Native mobile app | always | an app | out of scope |

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

1. **Inbox, read only.** Store inbound text and file metadata; conversations
   and unread counts; the Messages page; no sending beyond today's fuel plan.
2. **Reply.** Composer within the 24-hour window, outbox and statuses,
   claim and the stale-reply guard, per-user read state.
3. **Files in.** Media worker, storage adapter, previews for PDF and images,
   manual filing to a load.
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
- Outbox: one sender per row under two workers, lost answer is unknown,
  explicit resend only.
- Collaboration: two dispatchers replying to the same message; the stale
  reply refused with 409 and the newer message shown.
- Linking: suggestion never files; confirmation files once; another
  carrier's load never offered.
- Reads: one summary read per poll regardless of conversation count
  (query-count regression).

## Open questions

1. Module boundary: accept a new Messaging module with reviewed edges, or
   keep messaging inside Routing?
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
