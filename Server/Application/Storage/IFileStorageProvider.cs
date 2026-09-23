namespace Application.Storage;

// One storage kind's adapter. It stores and returns bytes in the connection
// it is given and knows nothing of messages or loads. An object's key is
// reserved before its upload and recorded with it, and storing under a key
// that already holds an object does not create a second one: a retry, or two
// attempts at once, end with one object. Errors never carry provider
// response bodies, tokens or file contents.
public interface IFileStorageProvider
{
  string Kind { get; }

  // Whether this server can use the kind at all (credentials, bucket).
  bool IsAvailable { get; }

  // The largest object this adapter accepts, in bytes.
  long MaximumSize { get; }

  // A key for this file that no other object can take.
  Task<string> ReserveKeyAsync(
    StorageTarget target,
    Guid fileId,
    CancellationToken ct
  );

  // Streams the content under the reserved key. When the key already holds
  // an object this stores nothing and succeeds. The provider must complete
  // an object only once it has received the full declared length.
  Task PutAsync(
    StorageTarget target,
    string key,
    StorageUpload upload,
    CancellationToken ct
  );

  Task<bool> ExistsAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  );

  // Null when the object no longer exists in the connection. The caller
  // disposes the stream.
  Task<Stream?> OpenAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  );

  // Removing an object that is already gone is not an error.
  Task DeleteAsync(StorageTarget target, string key, CancellationToken ct);

  // Checks that the connection still works, without changing anything.
  Task<bool> CheckAsync(StorageTarget target, CancellationToken ct);
}

// Secret is the connection's unprotected secret, for this call only.
public sealed record StorageTarget(
  Guid Company,
  Guid Connection,
  string? Root,
  string? Secret
);

// Content is read once, to exactly Length bytes. Folder is the readable path
// under the connection's root; a provider without folders keeps it as
// metadata.
public sealed record StorageUpload(
  Guid FileId,
  string Name,
  IReadOnlyList<string> Folder,
  string ContentType,
  long Length,
  Stream Content
);

public sealed class StorageUnavailableException(string reason)
  : Exception(reason);
