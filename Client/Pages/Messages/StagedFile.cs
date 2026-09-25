using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Forms;

namespace Client.Pages.Messages;

// A file picked or dropped for one conversation, held until it is sent.
// Its bytes are read once, when it is staged, and sent from that copy. Its
// retry key and caption are fixed when it is first sent, so sending it
// again after an answer that never came is the same message, not another.
public sealed class StagedFile
{
  public const long MaximumBytes = 16 * 1024 * 1024;
  public const int MaximumStaged = 5;
  public const int MaximumCaption = 1024;

  public Guid Id { get; } = Guid.NewGuid();
  public required Guid ConversationId { get; init; }
  public required string Name { get; init; }
  public required string ContentType { get; init; }
  public required byte[] Bytes { get; init; }

  // A small data-URL thumbnail, for photos only.
  public string? Preview { get; init; }
  public Guid Key { get; } = Guid.NewGuid();
  public string? Caption { get; set; }
  public StagedState State { get; set; }
  public string? Error { get; set; }

  // What the server takes over WhatsApp, by kind: photos up to 5 MB, other
  // files up to the upload limit.
  public static long Limit(string type) =>
    type.StartsWith("image/", StringComparison.Ordinal)
      ? 5L * 1024 * 1024
      : MaximumBytes;

  public static bool Accepts(string type) =>
    type
      is "application/pdf"
        or "image/jpeg"
        or "image/png"
        or "image/webp"
        or "video/mp4"
    || type.StartsWith("audio/", StringComparison.Ordinal);

  public static async Task<StagedFile> ReadAsync(
    IBrowserFile file,
    Guid conversation,
    CancellationToken ct
  )
  {
    var bytes = new byte[file.Size];
    await using (var stream = file.OpenReadStream(MaximumBytes, ct))
      await stream.ReadExactlyAsync(bytes, ct);
    return new()
    {
      ConversationId = conversation,
      Name = file.Name,
      ContentType = file.ContentType,
      Bytes = bytes,
      Preview = file.ContentType.StartsWith("image/", StringComparison.Ordinal)
        ? await ThumbnailAsync(file, ct)
        : null,
    };
  }

  // Resized by the browser; a photo it cannot draw is staged without one.
  private static async Task<string?> ThumbnailAsync(
    IBrowserFile file,
    CancellationToken ct
  )
  {
    try
    {
      var small = await file.RequestImageFileAsync("image/jpeg", 160, 160);
      var bytes = new byte[small.Size];
      await using var stream = small.OpenReadStream(MaximumBytes, ct);
      await stream.ReadExactlyAsync(bytes, ct);
      return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return null;
    }
  }

  // The upload as the server takes it: the file, its hash, its retry key
  // and caption, and the newest message the dispatcher had on screen.
  public MultipartFormDataContent Form(Guid? lastSeen, bool confirm)
  {
    var content = new ByteArrayContent(Bytes);
    content.Headers.ContentType = new(ContentType);
    var form = new MultipartFormDataContent
    {
      { content, "file", Name },
      {
        new StringContent(Convert.ToHexStringLower(SHA256.HashData(Bytes))),
        "sha256"
      },
      { new StringContent(Key.ToString()), "idempotencyKey" },
      { new StringContent(Caption ?? ""), "caption" },
      { new StringContent(confirm ? "true" : "false"), "confirm" },
    };
    if (lastSeen is { } seen)
      form.Add(new StringContent(seen.ToString()), "lastSeenMessageId");
    return form;
  }

  public string Size =>
    Bytes.Length >= 1024 * 1024
      ? $"{Bytes.Length / (1024.0 * 1024):0.#} MB"
      : $"{Math.Max(1, Bytes.Length / 1024)} KB";

  public string Kind =>
    ContentType == "application/pdf" ? "PDF"
    : ContentType.StartsWith("audio/", StringComparison.Ordinal) ? "Audio"
    : ContentType == "video/mp4" ? "Video"
    : "Photo";
}

public enum StagedState
{
  Staged,
  Sending,
  Failed,
}
