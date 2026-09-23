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

  private string Side => Message.Direction == "out" ? "is-out" : "is-in";

  private bool Retryable =>
    Message.Status is "unknown" or "rejected" or "failed" or "withdrawn";

  private static string Pending(AttachmentView file) =>
    file.FailureReason
    ?? (file.State == "pending" ? "being saved" : "being checked");
}
