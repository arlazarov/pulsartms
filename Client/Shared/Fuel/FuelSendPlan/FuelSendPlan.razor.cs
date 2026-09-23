using System.Globalization;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Shared.Fuel.FuelSendPlan;

// What a dispatcher hands a driver for this shift, and the one place they
// say they did. Opening it or copying the message records nothing; Mark as
// sent records the stops against the plan version that was shown, and is
// refused if the plan moved in between.
public partial class FuelSendPlan : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Parameter, EditorRequired]
  public Guid DispatchId { get; set; }

  [Parameter]
  public Guid? ExecutionLegId { get; set; }

  [Parameter]
  public string TruckNumber { get; set; } = "";

  [Parameter]
  public EventCallback Closed { get; set; }

  [Parameter]
  public EventCallback Sent { get; set; }

  private FuelIssuePreview? _preview;
  private bool _loading,
    _saving,
    _disposed;
  private string? _error,
    _status;
  private readonly CancellationTokenSource _lifetime = new();

  private string Url =>
    $"api/dispatch/{DispatchId}/planning/fuel/issue"
    + (ExecutionLegId is { } leg ? $"?executionLegId={leg}" : "");

  protected override Task OnInitializedAsync() => LoadAsync();

  private async Task LoadAsync()
  {
    _loading = true;
    _error = null;
    _status = null;
    var result = await Api.GetAsync<FuelIssuePreview>(Url, _lifetime.Token);
    if (_disposed)
      return;
    _loading = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage ?? "The fuel plan could not be opened.";
      return;
    }
    _preview = result.Response;
  }

  private async Task CopyAsync()
  {
    if (_preview is null)
      return;
    try
    {
      await JS.InvokeVoidAsync("navigator.clipboard.writeText", _preview.Message);
      _status = "Message copied. It is not marked as sent.";
    }
    catch (JSException)
    {
      _status = "Could not copy. Please try again.";
    }
  }

  private async Task MarkSentAsync()
  {
    if (_preview is not { } preview || _saving || _disposed)
      return;
    _saving = true;
    _error = null;
    _status = null;
    var result = await Api.PostAsync<FuelIssueSentRequest, FuelIssuePreview>(
      $"api/dispatch/{DispatchId}/planning/fuel/issue/sent",
      new(
        preview.PlanCalculatedAt,
        preview.Lines.Select(x => x.VisitKey).ToList()
      )
      {
        ExecutionLegId = preview.ExecutionLegId,
        AssignmentRevision = preview.AssignmentRevision,
      },
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage ?? "The plan could not be marked as sent.";
      return;
    }
    _preview = result.Response;
    _status = "Marked as sent.";
    await Sent.InvokeAsync();
  }

  private Task CloseAsync() => Closed.InvokeAsync();

  private static bool AllSent(FuelIssuePreview preview) =>
    preview.Lines.All(x => x.Sent && !x.Changed);

  private static string StateText(FuelIssuePreview preview) =>
    preview.IssueState switch
    {
      "ready" => preview.HorizonEndsAt is { } until
        ? "Driver on duty. This shift runs to about "
          + until
            .ToLocalTime()
            .ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)
          + ", including a two-hour station allowance."
        : "Driver on duty.",
      "awaitingDuty" =>
        "Awaiting duty: the driver is off duty or resting. Hold the plan "
          + "until they are back on duty.",
      _ => "Driver hours unknown: the current shift cannot be confirmed.",
    };

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
