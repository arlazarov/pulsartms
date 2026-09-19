using System.Text.Json;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Fuel.FuelRecalculateButton;

public partial class FuelRecalculateButton : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private PlanningDisplayCache PlanningCache { get; set; } = default!;

  [Inject]
  private ILogger<FuelRecalculateButton> Logger { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter]
  public Guid? ExecutionLegId { get; set; }

  [Parameter]
  public long AssignmentRevision { get; set; }

  [Parameter]
  public bool IconOnly { get; set; }

  [Parameter]
  public bool ManuallyEdited { get; set; }

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public EventCallback EditRequested { get; set; }

  [Parameter]
  public EventCallback<AutomaticPlanningResult> Recalculated { get; set; }

  [Parameter]
  public EventCallback<bool> BusyChanged { get; set; }

  [Parameter]
  public EventCallback Failed { get; set; }
  private CancellationTokenSource? _request;
  private (Guid Dispatch, Guid? Leg, long Revision) _identity;
  private bool _busy;
  private bool _disposed;

  protected override async Task OnParametersSetAsync()
  {
    if (
      _identity == (DispatchId, ExecutionLegId, AssignmentRevision)
      || _disposed
    )
      return;
    _identity = (DispatchId, ExecutionLegId, AssignmentRevision);
    _request?.Cancel();
    _request = null;
    if (!_busy)
      return;
    _busy = false;
    await BusyChanged.InvokeAsync(false);
  }

  private async Task RecalculateAsync()
  {
    if (_busy || _disposed || Disabled || DispatchId == Guid.Empty)
      return;
    if (ManuallyEdited)
    {
      await EditRequested.InvokeAsync();
      return;
    }
    var dispatchId = DispatchId;
    var legId = ExecutionLegId;
    var revision = AssignmentRevision;
    var scopeQuery = legId.HasValue
      ? $"?executionLegId={legId}&assignmentRevision={revision}"
      : "";
    using var request = new CancellationTokenSource();
    _request = request;
    _busy = true;
    try
    {
      await BusyChanged.InvokeAsync(true);
      if (!Owns(request, dispatchId))
        return;
      var response = await Api.PostAsync<object, AutomaticPlanningResult>(
        $"api/dispatch/{dispatchId}/planning/fuel/recalculate{scopeQuery}",
        new { },
        request.Token
      );
      if (!Owns(request, dispatchId))
        return;
      if (
        response.Success
        && response.Response is { } result
        && result.DispatchId == dispatchId
        && result.ExecutionLegId == legId
        && (!legId.HasValue || result.AssignmentRevision == revision)
      )
      {
        PlanningCache.StoreRecalculated(result);
        await Recalculated.InvokeAsync(result);
      }
      else
      {
        var reasons = response
          .Errors?.Where(reason => !string.IsNullOrWhiteSpace(reason))
          .ToArray();
        Logger.LogWarning(
          "Calculate Fuel failed for dispatch {DispatchId}: {Reason}",
          dispatchId,
          reasons is { Length: > 0 }
            ? string.Join("; ", reasons)
            : "The server returned no fuel calculation result."
        );
        await Failed.InvokeAsync();
      }
    }
    catch (Exception ex)
      when (ex
          is HttpRequestException
            or OperationCanceledException
            or JsonException
      )
    {
      if (Owns(request, dispatchId))
      {
        Logger.LogWarning(
          ex,
          "Calculate Fuel failed for dispatch {DispatchId}",
          dispatchId
        );
        await Failed.InvokeAsync();
      }
    }
    finally
    {
      if (Owns(request, dispatchId))
      {
        _request = null;
        _busy = false;
        await BusyChanged.InvokeAsync(false);
      }
    }
  }

  private bool Owns(CancellationTokenSource request, Guid dispatchId) =>
    !_disposed
    && !request.IsCancellationRequested
    && ReferenceEquals(_request, request)
    && DispatchId == dispatchId;

  public void Dispose()
  {
    _disposed = true;
    _request?.Cancel();
    _request = null;
  }
}
