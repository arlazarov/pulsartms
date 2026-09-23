using Client.Models.DTO.Dispatch.Workspace;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class CancelledWorkClose : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter, EditorRequired]
  public Guid DispatchId { get; set; }

  [Parameter]
  public string LoadStatus { get; set; } = "";

  [Parameter]
  public IReadOnlyList<DispatchAcceptedAssignment> Assignments { get; set; } =
    [];

  [Parameter]
  public EventCallback Closed { get; set; }

  private readonly CancellationTokenSource _lifetime = new();

  // One retry key per held assignment, kept until it is closed, so a
  // repeated press after a lost answer is the same request.
  private readonly Dictionary<Guid, Guid> _keys = [];
  private bool _confirming,
    _busy,
    _disposed;
  private string? _error;

  private IReadOnlyList<DispatchAcceptedAssignment> Held =>
    LoadStatus is "cancelled" or "canceled"
      ? Assignments.Where(x => x.Status == "held").ToList()
      : [];

  private async Task CloseAsync()
  {
    if (_busy || _disposed)
      return;
    _busy = true;
    _error = null;
    foreach (var assignment in Held)
    {
      if (!_keys.TryGetValue(assignment.ExecutionLegId, out var key))
        _keys[assignment.ExecutionLegId] = key = Guid.NewGuid();
      var result = await Api.PostAsync<CloseCancelledExecutionRequest, object>(
        $"api/dispatch/{DispatchId}/execution/source-cancellation",
        new(assignment.ExecutionLegId, assignment.Revision, key),
        _lifetime.Token
      );
      if (_disposed)
        return;
      if (!result.Success)
      {
        _busy = false;
        _error = result.ErrorMessage ?? "The work could not be closed.";
        return;
      }
      _keys.Remove(assignment.ExecutionLegId);
    }
    _busy = false;
    _confirming = false;
    await Closed.InvokeAsync();
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
