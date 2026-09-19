using System.Security.Cryptography;

namespace Application.Features.Dispatch.Documents;

public static class DispatchDocumentRules
{
  public const int MaximumBytes = 5 * 1024 * 1024;
  public const int MaximumCount = 50;

  public static string? ContentType(byte[]? content)
  {
    if (content is null || content.Length is < 8 or > MaximumBytes)
      return null;
    var start = content.AsSpan();
    if (start.StartsWith("%PDF-"u8))
      return "application/pdf";
    if (start.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
      return "image/png";
    if (start.StartsWith(new byte[] { 255, 216, 255 }))
      return "image/jpeg";
    return null;
  }

  public static string FileName(string? name, string contentType)
  {
    var leaf = (name ?? "").Replace('\\', '/').Split('/').Last();
    leaf = new string(leaf.Where(c => !char.IsControl(c)).ToArray()).Trim();
    var extension = contentType switch
    {
      "application/pdf" => ".pdf",
      "image/png" => ".png",
      _ => ".jpg",
    };
    var dot = leaf.LastIndexOf('.');
    if (dot >= 0)
      leaf = leaf[..dot];
    if (string.IsNullOrWhiteSpace(leaf))
      leaf = "document";
    return leaf[..Math.Min(leaf.Length, 170)] + extension;
  }

  public static bool ValidKind(string? kind) =>
    kind is "rc" or "bol" or "pod" or "other";

  public static string Hash(byte[] content) =>
    Convert.ToHexString(SHA256.HashData(content));
}
