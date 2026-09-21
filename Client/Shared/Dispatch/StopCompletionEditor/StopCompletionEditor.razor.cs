using System.Globalization;
using System.Net;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Shared.Dispatch.StopCompletionEditor;

public partial class StopCompletionEditor
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Parameter, EditorRequired]
  public Guid DispatchId { get; set; }

  [Parameter, EditorRequired]
  public DispatchStopResponse Stop { get; set; } = default!;

  [Parameter]
  public bool CompletedLoad { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }
  private readonly CancellationTokenSource _lifetime = new();
  private bool _editing,
    _undo,
    _saving,
    _disposed;
  private DateTime _date;
  private string _time = "",
    _identity = "";
  private long _revision;
  private Guid _stopId,
    _dispatchId;
  private string? _error;
  private bool ProviderCompleted =>
    (Stop.DepartedAt ?? Stop.DeliveredAt ?? Stop.PickedUpAt).HasValue;

  protected override void OnParametersSet()
  {
    if (_editing && (DispatchId != _dispatchId || Stop.Id != _stopId))
      Cancel();
  }

  private void Begin(bool undo)
  {
    if (_saving)
      return;
    var now = Clock.GetLocalNow();
    _date = now.Date;
    _time = now.ToString("hh:mm tt", CultureInfo.InvariantCulture);
    _dispatchId = DispatchId;
    _stopId = Stop.Id;
    _revision = Stop.ManualCompletionRevision;
    _identity = Stop.CompletionIdentity;
    _editing = true;
    _undo = undo;
    _error = null;
  }

  private void Cancel()
  {
    _editing = false;
    _error = null;
  }

  private async Task SaveAsync()
  {
    if (_saving || !_editing)
      return;
    DateTimeOffset? completed = null;
    if (!_undo)
    {
      if (
        !TimeOnly.TryParseExact(
          _time.Trim(),
          "hh:mm tt",
          CultureInfo.InvariantCulture,
          DateTimeStyles.AllowWhiteSpaces,
          out var time
        )
      )
      {
        _error = "Enter a time such as 02:00 PM.";
        return;
      }
      var local = DateTime.SpecifyKind(
        _date.Date + time.ToTimeSpan(),
        DateTimeKind.Unspecified
      );
      if (
        TimeZoneInfo.Local.IsInvalidTime(local)
        || TimeZoneInfo.Local.IsAmbiguousTime(local)
      )
      {
        _error =
          "This local time is ambiguous because of a clock change. Choose an unambiguous time.";
        return;
      }
      completed = new DateTimeOffset(
        local,
        TimeZoneInfo.Local.GetUtcOffset(local)
      );
      if (completed > Clock.GetUtcNow())
      {
        _error = "Completion time cannot be in the future.";
        return;
      }
    }
    _saving = true;
    _error = null;
    var dispatchId = _dispatchId;
    var stopId = _stopId;
    try
    {
      var result = await Api.PutAsync<
        StopCompletionUpdate,
        StopCompletionState
      >(
        $"api/dispatch/{dispatchId}/stops/{stopId}/completion",
        new(completed, _revision, _identity),
        _lifetime.Token
      );
      if (_disposed || DispatchId != dispatchId || Stop.Id != stopId)
        return;
      if (!result.Success || result.Response is not { } state)
      {
        await JS.InvokeVoidAsync(
          "console.error",
          "Stop completion failed",
          dispatchId,
          stopId,
          result.HttpStatusCode
        );
        _error = result.HttpStatusCode switch
        {
          HttpStatusCode.Unauthorized =>
            "Your session has expired. Please sign in again.",
          HttpStatusCode.Forbidden =>
            "You do not have permission to confirm this stop.",
          HttpStatusCode.Conflict =>
            "This stop changed. Close and reopen the load before retrying.",
          HttpStatusCode.BadRequest =>
            "Could not save the confirmation. Check the actual time and try again.",
          _ => "Could not save the confirmation. Please try again.",
        };
        return;
      }
      if (state.Revision < Stop.ManualCompletionRevision)
      {
        _editing = false;
        return;
      }
      Stop.ManualCompletedAt = state.CompletedAt;
      Stop.ManualCompletedBy = state.CompletedBy;
      Stop.ManualCompletedByName = state.CompletedByName;
      Stop.ManualCompletionRecordedAt = state.RecordedAt;
      Stop.ManualCompletionRevision = state.Revision;
      Stop.IsCompleted = state.IsCompleted;
      _editing = false;
      await Changed.InvokeAsync();
    }
    finally
    {
      _saving = false;
    }
  }

  private static string Timestamp(DateTime value) =>
    value
      .ToLocalTime()
      .ToString("MMM d · hh:mm tt", CultureInfo.InvariantCulture);

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
