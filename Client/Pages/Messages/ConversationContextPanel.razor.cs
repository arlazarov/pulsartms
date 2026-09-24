using Client.Models.DTO.Messaging;
using Client.Models.DTO.Mileage;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Messages;

// Beside a conversation: the driver, whom a dispatcher can choose when the
// number matched nobody or the wrong person, their hours of service from
// the fleet's shared snapshot (said plainly when there are none), and the
// truck and loads they are on. Several trucks are listed as they are; no
// load is picked for the dispatcher.
public partial class ConversationContextPanel : IDisposable
{
  [Parameter, EditorRequired]
  public Guid ConversationId { get; set; }

  [Parameter]
  public long Revision { get; set; }

  [Parameter]
  public ConversationContext? Context { get; set; }

  [Parameter]
  public bool Failed { get; set; }

  [Parameter]
  public EventCallback<Guid> OnChanged { get; set; }

  [Parameter]
  public string Participant { get; set; } = "";

  // Where the trip opens over the conversation, it closes here.
  [Parameter]
  public EventCallback OnClose { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;

  private List<MileageDriverOption>? _drivers;
  private string _driver = "";
  private bool _busy;
  private string? _error;

  // A panel belongs to one conversation (the page keys it by id). When it
  // goes, a read it started is cancelled; a write runs to its end, since
  // cancelling it would leave its outcome unknown, but its answer is not
  // applied anywhere.
  private readonly CancellationTokenSource _lifetime = new();

  // The shared hours control reads the fleet's clock shape.
  private static DriverHosClocks Clocks(ContextHours hours) =>
    new()
    {
      BreakMs = hours.BreakMs,
      DriveMs = hours.DriveMs,
      ShiftMs = hours.ShiftMs,
      CycleMs = hours.CycleMs,
      UpdatedAt = hours.UpdatedAt ?? default,
      CurrentDutyStatus = hours.DutyStatus,
    };

  // How old the clocks are, always said: Samsara's clocks are read on a
  // schedule, not live.
  private static string Age(ContextHours hours)
  {
    if (hours.UpdatedAt is not { } at)
      return "Samsara · time of reading unknown";
    var updated = DateTime.SpecifyKind(at, DateTimeKind.Utc);
    var minutes = (int)Math.Max(0, (DateTime.UtcNow - updated).TotalMinutes);
    return $"Samsara · updated {updated.ToLocalTime():HH:mm}, "
      + (minutes < 1 ? "just now" : $"{minutes} min ago");
  }

  private static string Titled(string status) =>
    status.Length == 0
      ? status
      : char.ToUpperInvariant(status[0]) + status[1..].Replace('_', ' ');

  private string Trucks() =>
    string.Join(
      ", ",
      Context!.Trucks.Select(x =>
        x.Role == "assigned" ? x.Number : $"{x.Number} ({x.Role})"
      )
    );

  private async Task ChooseAsync()
  {
    _busy = true;
    _error = null;
    var result = await Api.GetAsync<MileageFleetList<MileageDriverOption>>(
      "api/fleet/drivers",
      _lifetime.Token
    );
    if (_lifetime.IsCancellationRequested)
      return;
    _busy = false;
    if (!result.Success || result.Response is not { } list)
    {
      _error = result.ErrorMessage;
      return;
    }
    _drivers = [.. list.Items.Where(x => x.IsActive).OrderBy(x => x.Name)];
    _driver = Context?.DriverId?.ToString() ?? "";
  }

  private async Task LinkAsync()
  {
    _busy = true;
    _error = null;
    var result = await Api.PutAsync<DriverRequest, long>(
      $"api/messaging/conversations/{ConversationId}/driver",
      new(Guid.TryParse(_driver, out var id) ? id : null, Revision)
    );
    if (_lifetime.IsCancellationRequested)
      return;
    _busy = false;
    if (!result.Success)
    {
      _error = result.ErrorMessage;
      return;
    }
    _drivers = null;
    await OnChanged.InvokeAsync(ConversationId);
  }

  public void Dispose()
  {
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
