using System.Net;
using System.Text.Json;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Client.Pages.Dispatch;

public partial class DispatchCreate : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  private readonly CancellationTokenSource _lifetime = new();
  private readonly CreateDispatchRequest _draft = new()
  {
    IdempotencyKey = Guid.NewGuid(),
    Currency = "USD",
    Stops = [Stop("Pick Up", 1), Stop("Delivery", 2)],
  };
  private DispatchStopWorkspace? _stops;
  private CreateDispatchRequest? _pending;
  private string? _error;
  private string? _pendingNavigation;
  private bool _dirty;
  private bool _saving;
  private bool Locked => _saving || _pending is not null;

  private static DispatchWorkspaceStop Stop(string job, int sequence) =>
    new()
    {
      Id = Guid.NewGuid(),
      Sequence = sequence,
      SegmentKey = "new",
      Job = job,
      IsNew = true,
      CanEdit = true,
      CanMove = true,
      CanRemove = true,
    };

  private void Changed() => _dirty = true;

  private async Task SaveAsync()
  {
    if (_saving)
      return;
    if (_pending is null)
    {
      if (_stops is not null && !_stops.Validate(out _error))
        return;
      _pending = JsonSerializer.Deserialize<CreateDispatchRequest>(
        JsonSerializer.Serialize(_draft)
      )!;
    }
    _saving = true;
    _dirty = true;
    _error = null;
    var result = await Api.PostAsync<
      CreateDispatchRequest,
      DispatchWorkspaceResponse
    >("api/dispatch", _pending, _lifetime.Token);
    if (_lifetime.IsCancellationRequested)
      return;
    _saving = false;
    if (result.Success && result.Response is { } saved)
    {
      _dirty = false;
      _pending = null;
      Navigation.NavigateTo($"/dispatch/{saved.Load.Id}");
      return;
    }
    _error = result.ErrorMessage;
    if (
      result.HttpStatusCode
      is HttpStatusCode.BadRequest
        or HttpStatusCode.Forbidden
        or HttpStatusCode.UnprocessableEntity
    )
      _pending = null;
  }

  private Task<RequestResponseDTO<VerifiedDispatchAddress>> LookupAddressAsync(
    string query,
    CancellationToken ct
  ) =>
    Api.PostAsync<VerifyDispatchAddressRequest, VerifiedDispatchAddress>(
      "api/dispatch/verify-address",
      new() { Address = query },
      ct
    );

  private void BeforeNavigation(LocationChangingContext context)
  {
    if (!_dirty)
      return;
    context.PreventNavigation();
    _pendingNavigation = context.TargetLocation;
  }

  private void Stay() => _pendingNavigation = null;

  private void Leave()
  {
    var target = _pendingNavigation;
    _dirty = false;
    _pendingNavigation = null;
    if (target is not null)
      Navigation.NavigateTo(target);
  }

  public void Dispose()
  {
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
