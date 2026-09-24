# File storage

Where PulsR keeps files that drivers send and documents a company attaches.
State on 2026-09-23: implemented locally, not deployed; migration
`AddFileStorage` is not applied anywhere. Existing load documents still keep
their bytes in PostgreSQL and are not moved, renamed or copied.

## Choosing where files go

Each company chooses in Settings, File storage. It may keep several
connections: PulsR storage, its own Google Drive, and later other providers
(Dropbox and OneDrive are listed as not available yet). One connection is the
default for new uploads. Every stored file records the connection that holds
it, and an upload already recorded stays with its connection when the
default changes; the UI is the same whichever holds a file.

The default cannot be disconnected until another is chosen, and no
connection that holds files, or uploads on their way to it, can be
disconnected: they would silently become unreadable. The check and the
disconnect are one serializable transaction, and an upload records its
file in one that reads the connection as connected, so an upload and a
disconnect that interleave cannot both commit (`StoragePostgresTests`).
Rows left from before that fence are reported by the consistency auditor
(`storage.file-on-disconnected-storage`), read only. A company that has
chosen nothing gets PulsR storage on its first file, but only on a server
where PulsR storage is set up; a server without it never substitutes
another store.

## Identity is not the name

A file's identity is its PulsR file id, stable for its whole life, and the
provider key reserved for it. Messages and loads refer to the file id. The
readable folder and name are presentation, written with the file: renaming
or moving the file in the company's storage does not break any reference.
A file whose object cannot be read shows as missing or unavailable, never
as an empty file.

## Upload guarantees

`Application.Storage.FileStore` owns stored files. `StorageTargets` owns
how connections are reached: the connection new files go to (creating the
managed one on first use), a connection's provider and target (its secret is
unprotected only there), and the size a provider accepts. Connection
management and reconciliation use it directly. Adapters implement
`IFileStorageProvider` in Infrastructure.

- **Fingerprint.** The caller states the content's SHA-256, length and type.
  They are recorded with the upload before the provider is called and never
  change. The same file id with other content is refused
  (`StorageConflictException`), whether the first upload finished or not.
- **Reserved key.** The object key is reserved when the upload is recorded:
  a Drive id from `files.generateIds` used to create the file (a second
  create of it fails with 409 instead of duplicating), or the Cloud Storage
  name `{company}/{file}` written with `ifGenerationMatch=0`.
- **Checked before the last byte.** The content is hashed as it streams.
  On the read that would complete the declared length, before that read
  returns, the whole hash is compared and one byte is read ahead from the
  input, which must be its end. A provider that stops reading at the
  declared length and completes an object only on its full length
  therefore never completes one that is longer, shorter or different; such
  an upload is `rejected`. An empty read consumes nothing.
- **One attempt at a time.** An attempt claims the upload with a fenced
  lease (`UploadToken`, `UploadLeaseUntil`, compare-and-set) and only the
  holder completes it. Another attempt meanwhile gets
  `StorageBusyException`; an attempt whose lease was taken over cannot
  complete it. The lease (30 minutes) outlasts the providers' request
  timeout (10 minutes).
- **Lost answers.** A retry with the same id and content, or the reconciler,
  claims the upload and asks whether the reserved key holds an object: if
  so it is complete, because only matching content can have completed it;
  if not, the retry uploads and the reconciler marks it `failed`. Nothing is
  deleted on an ambiguous outcome.
- **Quarantine.** Every upload ends `quarantined`; callers cannot choose
  otherwise. Only a server-side check releases a file as `available`.
- **What `available` means.** Recorded under its fingerprint, and its
  first 16 bytes show an accepted kind that matches its declared type
  (`StoredFileCheck`). It is not a malware scan and not a full read. Every
  full read checks the length and SHA-256 again; a released file read back
  different (a company drive edited outside PulsR) is marked `changed` and
  not served again, and a message shows why. A check that finds content
  already shorter than declared refuses the file (`rejected`). An upload
  settled by the reconciler because its key holds an object is only as
  good as that object: it goes through the same check, and a later change
  is caught the same way.
- **Reconciliation that cannot starve.** Each pass takes the stalled
  uploads and unchecked files that are due, least recently tried first. A
  file it cannot settle (its storage disconnected or unreachable, or the
  check unable to read it) waits a backoff that doubles from the pass
  interval up to six hours (`ReconcileAfter`, `ReconcileFailures`), so a
  batch of them never keeps newer files from being reached. One file's
  failure is logged for that file and the pass goes on.
- **Bounds.** At most `Storage:MaximumMegabytes` (100) per file, less where
  an adapter says so, and `Storage:MaximumConcurrentUploads` (2) per process.

Not guaranteed: an attempt whose final bytes arrive at the provider after
its lease was taken over could, in principle, complete while the next
attempt found nothing and uploaded; the reserved key makes that the same
object, not a second one. Objects without any row (for example after a
database reset) are not searched for.

## Readable names and folders

The layout is a company setting (Settings, File names and folders), the
same for every provider:

- Load folders under a chosen folder (default `Dispatch/Loads`), named by a
  template of `{date}` (yyyy.MM.dd), `{load}`, `{broker}`, `{order}` and
  `{truck}`, joined by " - ". The default template is an example shaped like
  a common carrier layout, not any company's data. A missing field is left
  out with its separator; a cancelled load gets the company's suffix
  (default `Canceled`).
- Documents named for what they are, with their own extension: `POD.pdf`,
  `BOL.pdf`, `Rate Confirmation.pdf`, `Receipt.jpg`. A second file of the
  same name in a folder becomes `Receipt (2).jpg`, compared without case.
  The name the file arrived with is kept as `OriginalName`.
- A file nobody has filed yet goes to the inbox (default `Inbox`) under a
  folder for its day, with its own sanitized name. PulsR does not guess what
  an unknown file is; it is filed to a load only when a person confirms.
- Every folder and file name is normalized (NFC), stripped of path
  separators, control and reserved characters and leading or trailing dots
  and spaces, never a device name (`CON`, `NUL`), at most 120 characters
  with its extension kept; `..` never climbs out of a folder.
- A folder name is decided when its first file is written. When a load's
  date or truck changes later, its files stay where they are; renaming
  them is a deliberate, separate action, never a background mass move.

Per provider: Google Drive gets real folders under the folder the
administrator picked. With the `drive.file` scope PulsR sees only folders it
made or was shown, so it does not reuse a same-named folder a person made by
hand. PulsR storage in Cloud Storage keeps objects keyed by id and records
the readable path as object metadata for a future export; it does not
promise folders in the bucket.

## Moving files between storages (future; not implemented)

No transfer exists yet and none may run until built to this contract.
Changing the default only affects uploads recorded after the change.

1. A durable transfer job per file, with a checkpoint, written before any
   side effect. The target key is reserved and recorded first, so a resumed
   or repeated step writes the same target object, never a second one.
2. Copy from a stable source version. Record the source's version marker
   when reading starts and check it after; a source changed outside PulsR
   during the copy stops the step (or copies that recorded version where
   the provider can address it). A provider ETag or version is not a
   content hash.
3. Verify the target: exact SHA-256 and length of what can be read back
   from the target, against the file's fingerprint.
4. Swap the file's pointer (connection and key) by compare-and-set against
   the expected file version and company. A concurrent change loses the
   swap and leaves the file where it was.
5. Keep the source. Removing it is a separate, explicit action after a
   retention period, a check that nothing references it, and permission;
   it is never part of a successful copy.
6. Every outcome is journaled; a partial failure is not shown as complete,
   an ambiguous one is reconciled by reading both sides, and no step is
   retried blindly. Cancelling or resuming never loses the source.
7. A connection with referenced files or running transfers cannot be
   disconnected. Rolling back means pointing at a verified, still readable
   earlier copy, and never discards edits made since.

Tests the transfer must bring: a crash mid-copy and resume; a checksum
mismatch on the target; access revoked on either side; a race on the
pointer swap; an identical retry making no second object; a source changed
during the copy; cancel and rollback keeping the source.

## Adapters

| Kind | Adapter | Status |
| --- | --- | --- |
| PulsR storage | `CloudStorageFileStorage`: a Cloud Storage bucket, service identity from the metadata server, streaming | code only; needs `Storage:Managed:Provider=gcs`, `Storage:Managed:Bucket` and bucket access for the service account (not created) |
| PulsR storage, development | `DatabaseFileStorage`: bytes in `ManagedFileBlobs`, whole file in memory, 16 MiB | development and tests only, when `Storage:Managed:Provider=database` (`appsettings.Development.json`) |
| Google Drive | `GoogleDriveStorage`, `GoogleDriveAuthorization`, `GoogleDriveRootPicker` | code, tested against a scripted Google; needs the registration below |
| Dropbox, OneDrive | none | listed, not available |

## Connecting Google Drive

1. An administrator presses Connect Google Drive. PulsR stores a pending
   connection with a nonce and a PKCE verifier (protected, ten minutes) and
   sends them to Google's consent page for the `drive.file` scope only.
2. Google redirects to `GET /api/storage/connections/callback` without the
   administrator's session. The state must name the company, the connection
   and the nonce; it is spent (saved as failed, "Connect again") before the
   code is exchanged, so a replay or a crash leaves a visible failed
   connection. A successful exchange stores only the protected refresh
   token, and the connection waits for a folder.
3. Choose folder opens Google Picker (My Drive and shared drives) with a
   short-lived access token for that connection. Picking the folder is what
   lets `drive.file` reach it. The server verifies it through the
   connection's own grant: a folder, not trashed, `canAddChildren`. Nothing
   is created in the account before this; changing the folder moves only
   new uploads.

Blocked on the owner: a Google OAuth client (`GoogleDrive:ClientId`,
`GoogleDrive:ClientSecret`, `GoogleDrive:RedirectUri` =
`https://<host>/api/storage/connections/callback`), a Picker browser key
(`GoogleDrive:PickerApiKey`), the Cloud project number
(`GoogleDrive:AppId`) and the consent screen. Until they exist the kind
shows why it cannot be connected.

## API

Administrator only, except the callback: `GET /api/storage`,
`GET|PUT /api/storage/layout`, `POST /api/storage/connections` (start),
`POST .../{id}/picker`, `POST .../{id}/root`, `POST .../{id}/default`,
`POST .../{id}/disconnect`, `POST .../{id}/check`, `GET .../callback`.

## Tests

`Server.Tests/Storage`: `FileStorageTests` (fingerprint conflicts, content
mismatch, lost answer and retry, busy and late attempts, reconciler against
live leases, unreachable files not starving others, a corrupt file refused
while the pass goes on, a released file changed in its storage not served
again, no substitution), `StorageReadingTests` (empty reads,
excess, short and different input), `StorageLayoutTests` (layout, names,
collisions, disconnect guard), `StorageNamingTests` (templates,
sanitization), `StorageConnectionFlowTests` (state, folder choice, crash
after the state is spent), `GoogleDriveStorageTests` (reserved ids, 409,
folders, picker checks). `Client.Tests/Routing/StorageSettingsComponentTests`.
Not run: any real Google or Cloud Storage call, and PostgreSQL.
EOF
awk 'length > 80 && !/\|/ && !/http/' docs/features/file-storage.md