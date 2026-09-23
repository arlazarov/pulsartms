using Domain.Entities.Storage;

namespace Application.Storage;

// Releases a quarantined file only when its first bytes say it is the kind
// it was declared to be, and that kind is one PulsR shows and sends: PDF,
// JPEG, PNG, WebP, OGG, MP3, AAC and MP4. Anything else is refused and never
// served. It reads a few bytes, never the whole file. A file whose storage
// cannot be read now stays quarantined, to be checked again.
public sealed class StoredFileCheck(FileStore files)
{
  public const int Probe = 16;
  private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
  private static readonly byte[] Png =
  [
    0x89,
    0x50,
    0x4E,
    0x47,
    0x0D,
    0x0A,
    0x1A,
    0x0A,
  ];

  // The file's state after the check; a file already checked keeps its
  // answer.
  public async Task<string> CheckAsync(Guid fileId, CancellationToken ct)
  {
    var state = await files.StateAsync(fileId, ct);
    if (state is not StoredFileStates.Quarantined)
      return state ?? StoredFileStates.Missing;
    (StoredFile File, Stream Content)? opened;
    try
    {
      opened = await files.OpenAsync(fileId, quarantined: true, ct);
    }
    catch (StorageUnavailableException)
    {
      return StoredFileStates.Quarantined;
    }
    if (opened is not { } found)
      return StoredFileStates.Quarantined;
    var head = new byte[Probe];
    var read = 0;
    await using (found.Content)
      while (read < Probe)
      {
        var count = await found.Content.ReadAsync(head.AsMemory(read), ct);
        if (count == 0)
          break;
        read += count;
      }
    var outcome = Matches(found.File.ContentType, head.AsSpan(0, read))
      ? StoredFileStates.Available
      : StoredFileStates.Rejected;
    await files.SetStateAsync(
      fileId,
      StoredFileStates.Quarantined,
      outcome,
      ct
    );
    return outcome;
  }

  // The kind the bytes show, when it is one PulsR accepts.
  public static string? Detect(ReadOnlySpan<byte> head)
  {
    if (head.StartsWith("%PDF-"u8))
      return "application/pdf";
    if (head.StartsWith(Jpeg))
      return "image/jpeg";
    if (head.StartsWith(Png))
      return "image/png";
    if (
      head.Length >= 12
      && head.StartsWith("RIFF"u8)
      && head[8..12].SequenceEqual("WEBP"u8)
    )
      return "image/webp";
    if (head.StartsWith("OggS"u8))
      return "audio/ogg";
    if (head.Length >= 8 && head[4..8].SequenceEqual("ftyp"u8))
      return "video/mp4";
    if (
      head.StartsWith("ID3"u8)
      || head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE6) == 0xE2
    )
      return "audio/mpeg";
    if (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xF6) == 0xF0)
      return "audio/aac";
    return null;
  }

  // The declared type must be the detected kind; MP4 containers may be
  // declared as audio or video.
  public static bool Matches(string declared, ReadOnlySpan<byte> head)
  {
    var type = declared.Split(';')[0].Trim().ToLowerInvariant();
    return Detect(head) switch
    {
      null => false,
      "video/mp4" => type is "video/mp4" or "audio/mp4",
      var detected => type == detected
        || type == "image/jpg" && detected == "image/jpeg",
    };
  }
}
