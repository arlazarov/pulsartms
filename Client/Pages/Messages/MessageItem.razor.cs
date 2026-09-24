using Client.Models.DTO.Messaging;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Messages;

// One message in a conversation: its text, files, author, time and, for a
// reply, where it is and whether it can be sent again.
public partial class MessageItem
{
  [Parameter, EditorRequired]
  public MessageView Message { get; set; } = default!;

  [Parameter]
  public bool Busy { get; set; }

  [Parameter]
  public EventCallback<AttachmentView> OnOpen { get; set; }

  [Parameter]
  public EventCallback<MessageView> OnRetry { get; set; }

  [Parameter]
  public IReadOnlyList<ContextLoad> Loads { get; set; } = [];

  [Parameter]
  public EventCallback OnFiled { get; set; }

  // The signed-in dispatcher's name; their own replies read "You".
  [Parameter]
  public string? Me { get; set; }

  // Load documents take PDF, PNG or JPEG; the server decides again.
  private static bool Fileable(AttachmentView file) =>
    file.Type is "application/pdf" or "image/png" or "image/jpeg";

  private static string Document(string kind) =>
    kind switch
    {
      "bol" => "Bill of lading",
      "pod" => "Proof of delivery",
      "rc" => "Rate confirmation",
      _ => "Other",
    };

  private string Side => Message.Direction == "out" ? "is-out" : "is-in";

  private bool Failed => Message.Status is "failed" or "rejected";

  private bool Attention =>
    Message.Status is "failed" or "rejected" or "unknown" or "withdrawn";

  private static bool IsImage(AttachmentView file) =>
    file.Type.StartsWith("image/", StringComparison.Ordinal);

  private static string Kind(AttachmentView file) =>
    IsImage(file) ? "Photo"
    : file.Type == "application/pdf" ? "PDF"
    : "File";

  private static string Label(AttachmentView file) =>
    IsImage(file) ? "PHOTO"
    : file.Type == "application/pdf" ? "PDF"
    : "FILE";

  private bool Retryable =>
    Message.Status is "unknown" or "rejected" or "failed" or "withdrawn";

  private static string Pending(AttachmentView file) =>
    file.FailureReason
    ?? (file.State == "pending" ? "being saved" : "being checked");
}
