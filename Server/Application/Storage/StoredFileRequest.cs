using Microsoft.Extensions.Options;

namespace Application.Storage;

// FileId is the caller's stable identity for this upload and Sha256 (lower
// case hex) the content it promises: a retry with the same id and content
// completes or returns the same file, never a second one; the same id with
// other content is refused.
//
// Name and Folder are how the file reads outside PulsR (see StorageLayouts);
// OriginalName is the name it arrived with, kept for reference.
public sealed record StoredFileRequest(
  Guid FileId,
  string Name,
  string ContentType,
  long Length,
  string Sha256,
  IReadOnlyList<string>? Folder = null,
  string? OriginalName = null,
  Guid? ConnectionId = null
);

// Uploads streaming at once in this process, shared by every scope.
public sealed class StorageUploadGate(IOptions<StorageOptions> options)
{
  public SemaphoreSlim Slots { get; } =
    new(
      options.Value.MaximumConcurrentUploads,
      options.Value.MaximumConcurrentUploads
    );
}
