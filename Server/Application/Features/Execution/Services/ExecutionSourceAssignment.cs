using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Execution.Services;

internal static class ExecutionSourceAssignment
{
  public static DispatchResourceProposal? Resolve(
    DispatchSourceLink source,
    bool ordinary
  )
  {
    if (!ordinary || string.IsNullOrWhiteSpace(source.AssignmentProposalJson))
      return null;
    var proposal = DispatchWorkspaceData.Read<DispatchAssignmentProposal>(
      source.AssignmentProposalJson
    );
    var header = proposal.Header;
    var visits = proposal
      .Visits.Select(x => new DispatchResourceProposal(
        Empty(x.Resources.Truck) ? header.Truck : x.Resources.Truck,
        Empty(x.Resources.Driver) ? header.Driver : x.Resources.Driver,
        Empty(x.Resources.CoDriver) ? header.CoDriver : x.Resources.CoDriver,
        Empty(x.Resources.Trailer) ? header.Trailer : x.Resources.Trailer,
        Empty(x.Resources.Truck) ? header.TruckId : x.Resources.TruckId,
        Empty(x.Resources.Driver) ? header.DriverId : x.Resources.DriverId,
        Empty(x.Resources.CoDriver)
          ? header.CoDriverId
          : x.Resources.CoDriverId,
        Empty(x.Resources.Trailer) ? header.TrailerId : x.Resources.TrailerId
      ))
      .Distinct()
      .ToArray();
    if (visits.Length != 1)
      return null;
    var resource = visits[0];
    if (
      resource.TruckId is null
      || !Resolved(resource.Driver, resource.DriverId)
      || !Resolved(resource.CoDriver, resource.CoDriverId)
      || !Resolved(resource.Trailer, resource.TrailerId)
      || !Matches(header.Truck, header.TruckId, resource.TruckId)
      || !Matches(header.Driver, header.DriverId, resource.DriverId)
      || !Matches(header.Trailer, header.TrailerId, resource.TrailerId)
    )
      return null;
    return resource;
  }

  public static void Apply(
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> stops,
    DispatchResourceProposal resource,
    string signature
  )
  {
    leg.TruckId = resource.TruckId!.Value;
    leg.DriverId = resource.DriverId;
    leg.CoDriverId = resource.CoDriverId;
    leg.TrailerId = resource.TrailerId;
    leg.SourceAssignmentSignature = signature;
    foreach (var stop in stops)
    {
      stop.TruckId = resource.TruckId;
      stop.TruckNumber = resource.Truck;
      stop.DriverId = resource.DriverId;
      stop.DriverName = resource.Driver;
      stop.CoDriverId = resource.CoDriverId;
      stop.CoDriverName = resource.CoDriver;
      stop.TrailerId = resource.TrailerId;
      stop.TrailerNumber = resource.Trailer;
      stop.HasDriverOverride = false;
    }
  }

  private static bool Empty(string value) => string.IsNullOrWhiteSpace(value);

  private static bool Resolved(string value, Guid? id) =>
    Empty(value) || id.HasValue;

  private static bool Matches(string value, Guid? id, Guid? accepted) =>
    Empty(value) || id.HasValue && id == accepted;
}
