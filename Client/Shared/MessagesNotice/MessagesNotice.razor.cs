using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.MessagesNotice;

// The unread count beside Messages in the navigation, on every page. It
// keeps the shared notice subscription while it is shown.
public partial class MessagesNotice : IAsyncDisposable
{
  [Inject]
  private MessagingNotices Notices { get; set; } = default!;

  private bool _disposed;

  protected override async Task OnInitializedAsync()
  {
    Notices.Changed += OnChanged;
    await Notices.JoinAsync();
  }

  private void OnChanged() =>
    _ = InvokeAsync(() =>
    {
      if (!_disposed)
        StateHasChanged();
    });

  private static string Text(UnreadCount count) =>
    count.More ? $"{count.Conversations}+" : count.Conversations.ToString();

  private static string Label(UnreadCount count) =>
    $"{Text(count)} unread "
    + (
      count.Conversations == 1 && !count.More ? "conversation" : "conversations"
    );

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    Notices.Changed -= OnChanged;
    await Notices.LeaveAsync();
  }
}
