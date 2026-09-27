using Client.Models.DTO.Messaging;
using Client.Models.DTO.Mileage;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Messages;

// Beside a conversation: the driver, whom a dispatcher can choose when the
// number matched nobody or the wrong person; their hours of service from
// the fleet's shared snapshot, said plainly when there are none and dated
// when they are old; and the truck and loads they are on. Several trucks
// are listed as they are; no load is picked for the dispatcher.
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

  // The status start is the server's reading of the HOS history, never
  // the time the clocks were fetched, and counts only for the status the
  // clocks show. The duration runs to now; the shared summary drops it once
  // the clocks are older than it trusts a status for.
  private DriverDutyStatus? Duty(ContextHours hours) =>
    hours.DutyStatus is { Length: > 0 } status
      ? new(
        status,
        Context?.Duty?.Status == status ? Context.Duty.StartedAt : null,
        null,
        DateTimeOffset.UtcNow
      )
      {
        Jurisdiction = Context?.Duty?.Jurisdiction,
      }
      : null;

  // Said only when it matters: the clocks refresh every minute, so a
  // routine reading needs no timestamp beside it.
  private static string? Staleness(ContextHours hours)
  {
    if (hours.UpdatedAt is not { } at)
      return "When these hours were read is unknown.";
    var updated = DateTime.SpecifyKind(at, DateTimeKind.Utc);
    var minutes = (int)Math.Max(0, (DateTime.UtcNow - updated).TotalMinutes);
    return minutes <= StaleMinutes
      ? null
      : $"Hours as of {updated.ToLocalTime():HH:mm}, {minutes} min ago.";
  }

  private const int StaleMinutes = 3;

  // A load opens with the way back to this conversation, where the reply
  // being written is kept (Messages leaves it with ReturnPlaces).
  // The driver's current load as the server placed it; never simply the
  // first one listed.
  private ContextLoad? Current =>
    Context?.Loads.FirstOrDefault(x => x.Phase == "current");

  private IReadOnlyList<ContextLoad> Others =>
    Context is null ? [] : [.. Context.Loads.Where(x => x != Current)];

  private string OthersTitle =>
    Others.All(x => x.Phase is "next" or "upcoming")
      ? Others.Count > 1
        ? "Next loads"
        : "Next load"
      : "Other loads";

  private string LoadHref(Guid load) =>
    ReturnNavigation.Load(load, ReturnNavigation.Conversation(ConversationId));

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
