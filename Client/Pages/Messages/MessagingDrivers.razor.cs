using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Messages;

// Below the conversations: the drivers with a WhatsApp number of their own
// and no conversation yet on the company's number, in the dispatcher's
// driver group and under the same search, by name. Choosing one asks the
// server for the driver's conversation, which it opens or creates once;
// nothing is sent. While one choice is on its way the others wait, and a
// late answer for an earlier list never replaces a newer one.
public partial class MessagingDrivers : IDisposable
{
  [Parameter]
  public string Search { get; set; } = "";

  [Parameter]
  public EventCallback<Guid> OnOpened { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private ChosenDriverGroup DriverGroup { get; set; } = default!;

  private MessagingDriversView? _view;
  private List<MessagingDriver>? _drivers;
  private string? _searched,
    _error;
  private bool _loadingMore,
    _opening,
    _disposed;
  private int _read;
  private readonly CancellationTokenSource _lifetime = new();

  private bool CanOpen => !_opening && _view is { Configured: true };

  protected override void OnInitialized() =>
    DriverGroup.Changed += OnDriverGroupChanged;

  protected override async Task OnParametersSetAsync()
  {
    if (_searched == Search)
      return;
    _searched = Search;
    await LoadAsync(null);
  }

  private async Task LoadMoreAsync()
  {
    if (_view?.Next is not { } next || _loadingMore)
      return;
    _loadingMore = true;
    await LoadAsync(next);
    _loadingMore = false;
  }

  private async Task LoadAsync(MessagingDriverCursor? after)
  {
    var generation = ++_read;
    var result = await Api.GetAsync<MessagingDriversView>(
      "api/messaging/drivers?withoutConversation=true"
        + (Search.Length > 0 ? $"&search={Uri.EscapeDataString(Search)}" : "")
        + (
          after is null
            ? ""
            : $"&afterName={Uri.EscapeDataString(after.Name)}"
              + $"&afterId={after.Id}"
        ),
      _lifetime.Token
    );
    if (_disposed || generation != _read)
      return;
    if (!result.Success || result.Response is not { } view)
    {
      _error = result.ErrorMessage;
      return;
    }
    _error = null;
    _view = view;
    _drivers = after is null
      ? [.. view.Drivers]
      : [.. _drivers ?? [], .. view.Drivers];
  }

  private async Task OpenAsync(MessagingDriver driver)
  {
    if (!CanOpen || driver.Number is null)
      return;
    if (driver.ConversationId is { } known)
    {
      await OnOpened.InvokeAsync(known);
      return;
    }
    _opening = true;
    _error = null;
    var result = await Api.PostAsync<object, Guid>(
      $"api/messaging/drivers/{driver.Id}/conversation",
      new { },
      _lifetime.Token
    );
    _opening = false;
    if (_disposed)
      return;
    if (!result.Success)
    {
      _error = result.ErrorMessage;
      return;
    }
    // The driver now has a conversation, listed above.
    _drivers?.Remove(driver);
    await OnOpened.InvokeAsync(result.Response);
  }

  private void OnDriverGroupChanged() =>
    _ = InvokeAsync(async () =>
    {
      if (_disposed)
        return;
      await LoadAsync(null);
      StateHasChanged();
    });

  // Where a chat would go, and from which contact; a phone is not known to
  // be on WhatsApp until the driver writes.
  private static string Where(MessagingDriver driver) =>
    driver switch
    {
      { Number: null } => "WhatsApp number is not valid",
      { Source: "phone" } => $"{driver.Number} (phone) · no messages yet",
      _ => $"{driver.Number} · no messages yet",
    };

  private static string Initials(string name) =>
    string.Concat(
      name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2)
        .Select(x => char.ToUpperInvariant(x[0]))
    );

  public void Dispose()
  {
    _disposed = true;
    DriverGroup.Changed -= OnDriverGroupChanged;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
