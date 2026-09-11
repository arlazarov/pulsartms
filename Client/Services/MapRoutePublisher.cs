using System.Text.Json;
using Client.Models.DTO.Planning;
using Microsoft.JSInterop;

namespace Client.Services;

internal sealed class MapRoutePublisher
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private IJSObjectReference? _target;
  private (Guid Id, int Version, Guid Truck)? _geometry;
  private int _version;

  public async Task PublishAsync(IJSObjectReference target, RoutePlanningState? state, bool fit,
    Func<bool> isCurrent, CancellationToken cancellationToken)
  {
    var version = ++_version;
    var identity = state?.Plan is { } plan ? (plan.Id, plan.Version, plan.TruckId) : ((Guid, int, Guid)?)null;
    var omitGeometry = identity.HasValue && ReferenceEquals(target, _target) && identity == _geometry;
    bool Current() => version == _version && !cancellationToken.IsCancellationRequested && isCurrent();

    async Task<bool> SendAsync(bool omit)
    {
      using var buffer = new ResponsiveWriteStream();
      await JsonSerializer.SerializeAsync(buffer, Payload(state?.Plan, omit), Json, cancellationToken);
      if (!Current()) return false;
      return await target.InvokeAsync<bool>("setRouteBytes", cancellationToken, buffer.ToArray(), state?.Progress, fit);
    }

    var applied = await SendAsync(omitGeometry);
    // A recreated renderer can lose its geometry while the interop handle survives.
    if (!applied && omitGeometry && Current()) applied = await SendAsync(false);
    if (!applied || !Current()) return;
    _target = target;
    _geometry = identity;
  }

  private static Dictionary<string, object?>? Payload(RoutePlan? plan, bool omitGeometry)
  {
    if (plan is null) return null;
    var payload = new Dictionary<string, object?>
    {
      ["id"] = plan.Id, ["dispatchId"] = plan.DispatchId, ["version"] = plan.Version, ["truckId"] = plan.TruckId,
      ["geometryOmitted"] = omitGeometry, ["fromCurrentPosition"] = plan.FromCurrentPosition,
      ["stops"] = plan.Stops, ["referenceStops"] = plan.ReferenceStops,
      ["tracking"] = plan.Tracking, ["fuelPlan"] = plan.FuelPlan,
      ["tankGallons"] = plan.Profile.TankGallons
    };
    if (!omitGeometry)
    {
      payload["route"] = new { plan.Route.Legs };
      payload["referenceRoute"] = plan.ReferenceRoute is { } reference ? new { reference.Legs } : null;
    }
    return payload;
  }
}
