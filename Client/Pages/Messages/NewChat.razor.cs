using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Messages;

// Starts a chat: the drivers with a WhatsApp number in the dispatcher's
// driver group, by name. Choosing one asks the server for the driver's
// conversation, which it opens or creates once; nothing is sent. While one
// choice is on its way the others wait, and a late answer for an earlier
// list never replaces a newer one.
public partial class NewChat : IDisposable
{
  [Parameter]
  public EventCallback<Guid> OnOpened { get; set; }

  [Parameter]
  public EventCallback OnClose { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private ChosenDriverGroup DriverGroup { get; set; } = default!;

  private MessagingDriversView? _view;
  private List<MessagingDriver>? _drivers;
  private string _search = "",
    _searched = "";
  private string? _error;
  private bool _loadingMore,
    _opening,
    _disposed;
  private int _read;
  private readonly CancellationTokenSource _lifetime = new();

  private bool CanOpen => !_opening && _view is { Configured: true };

  protected override async Task OnInitializedAsync()
  {
    DriverGroup.Changed += OnDriverGroupChanged;
    await LoadAsync(null);
  }

  private async Task SearchAsync()
  {
    _searched = _search.Trim();
    _drivers = null;
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
      "api/messaging/drivers?"
        + (
          _searched.Length > 0
            ? $"search={Uri.EscapeDataString(_searched)}&"
            : ""
        )
        + (
          after is null
            ? ""
            : $"afterName={Uri.EscapeDataString(after.Name)}"
              + $"&afterId={after.Id}"
        ),
      _lifetime.Token
    );
    if (_disposed || generation != _read)
      return;
    if (!result.Success || result.Response is not { } view)
    {
      _error = result.ErrorMessage;
      _drivers ??= [];
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
    if (!CanOpen)
      return;
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
    if (result.Success)
      await OnOpened.InvokeAsync(result.Response);
    else
      _error = result.ErrorMessage;
  }

  private void OnDriverGroupChanged() =>
    _ = InvokeAsync(async () =>
    {
      if (_disposed)
        return;
      _drivers = null;
      await LoadAsync(null);
      StateHasChanged();
    });

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
