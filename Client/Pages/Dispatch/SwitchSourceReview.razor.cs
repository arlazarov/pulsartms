using System.Net;
using Client.Models.DTO.Execution;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class SwitchSourceReview : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter]
  public Guid? ExecutionLegId { get; set; }

  [Parameter]
  public string Reason { get; set; } = "";

  [Parameter]
  public bool Locked { get; set; }

  [Parameter]
  public EventCallback<bool> BusyChanged { get; set; }

  [Parameter]
  public EventCallback<bool> EditingChanged { get; set; }

  [Parameter]
  public EventCallback<ExecutionSourceApplyResult> Accepted { get; set; }

  private CancellationTokenSource _identity = new();
  private (Guid, Guid?) _scope;
  private ExecutionSourceReview? _review;
  private AcceptExecutionSourceChangesRequest? _request;
  private bool _opened;
  private bool _busy;
  private bool _saved;
  private bool _conflict;
  private bool _disposed;
  private string? _error;
  private string Path => $"api/dispatch/{DispatchId}/execution/source-review";

  protected override void OnParametersSet()
  {
    var scope = (DispatchId, ExecutionLegId);
    if (scope == _scope)
      return;
    _identity.Cancel();
    _identity.Dispose();
    _identity = new();
    _scope = scope;
    _opened = false;
    _busy = false;
    _saved = false;
    _conflict = false;
    _review = null;
    _request = null;
    _error = null;
  }

  private async Task PreviewAsync()
  {
    if (_disposed || Locked || _busy || _saved || ExecutionLegId is null)
      return;
    _opened = true;
    _busy = true;
    _error = null;
    await EditingChanged.InvokeAsync(true);
    await BusyChanged.InvokeAsync(true);
    var owner = _identity;
    var result = await Api.GetAsync<ExecutionSourceReview>(
      $"{Path}?executionLegId={ExecutionLegId}",
      owner.Token
    );
    if (!Owns(owner))
      return;
    _busy = false;
    await BusyChanged.InvokeAsync(false);
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    var review = result.Response;
    if (
      review.DispatchId != DispatchId
      || review.ExecutionLegId != ExecutionLegId
    )
    {
      _error = "The source preview does not match this load. Refresh it.";
      return;
    }
    _review = review;
  }

  private async Task AcceptAsync()
  {
    if (
      _disposed
      || Locked
      || _busy
      || _saved
      || _conflict
      || _review is not { CanApply: true }
    )
      return;
    _request ??= new(
      _review.ExecutionLegId,
      _review.AssignmentRevision,
      _review.SourceSignature,
      Guid.NewGuid()
    );
    _busy = true;
    _error = null;
    await BusyChanged.InvokeAsync(true);
    var owner = _identity;
    var result = await Api.PostAsync<
      AcceptExecutionSourceChangesRequest,
      ExecutionSourceApplyResult
    >(Path, _request, owner.Token);
    if (!Owns(owner))
      return;
    _busy = false;
    await BusyChanged.InvokeAsync(false);
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      _conflict = result.HttpStatusCode == HttpStatusCode.Conflict;
      return;
    }
    if (
      result.Response.DispatchId != DispatchId
      || result.Response.ExecutionLegId != ExecutionLegId
    )
    {
      _error = "The response does not match this load. Retry the same change.";
      return;
    }
    _saved = true;
    _opened = false;
    await EditingChanged.InvokeAsync(false);
    await Accepted.InvokeAsync(result.Response);
  }

  private async Task CancelAsync()
  {
    if (_busy)
      return;
    _opened = false;
    _review = null;
    _request = null;
    _conflict = false;
    _error = null;
    await EditingChanged.InvokeAsync(false);
  }

  private bool Owns(CancellationTokenSource owner) =>
    !_disposed && owner == _identity && !owner.IsCancellationRequested;

  private static string Schedule(SwitchVisitOption visit) =>
    StopAppointmentDisplay.Format(
      visit.ScheduledDate,
      visit.ScheduledTime,
      visit.IsWindow ? visit.ScheduledDate2 : null,
      visit.IsWindow ? visit.ScheduledTime2 : null
    );

  public void Dispose()
  {
    _disposed = true;
    _identity.Cancel();
    _identity.Dispose();
  }
}
