namespace Infrastructure.Integrations.Google.Gmail;

public class GmailAttachment
{
  public string MessageId { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public byte[] Content { get; set; } = [];
}
