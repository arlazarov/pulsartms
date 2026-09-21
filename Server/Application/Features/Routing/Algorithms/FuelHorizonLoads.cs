using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Routing.Algorithms;

// Which of a truck's loads a fuel plan looks ahead over: the one being
// driven, and those after it as far as the next delivery that ends a run.
public static class FuelHorizonLoads
{
  public static List<T> SelectLoads<T>(RoutePlan plan, IReadOnlyList<T> loads)
    where T : IWorkFacts
  {
    if (plan.ExecutionLegId.HasValue)
    {
      var current = loads
        .Where(x =>
          x.Id == plan.DispatchId
          && x.TruckId == plan.TruckId
          && x.ExecutionLegId == plan.ExecutionLegId
          && x.AssignmentRevision == plan.AssignmentRevision
          && PlanningWorkPolicy.CanUseGps(x)
          && !x.AwaitingReceipt
          && !x.Stops.Any(stop =>
            !stop.DriverOnly
            && stop.TruckId.HasValue
            && stop.TruckId != plan.TruckId
          )
        )
        .ToList();
      if (current.Count != 1)
        throw new RoutePlanningException(
          "The selected execution changed. Reload before finding fuel."
        );
      if (!EndsAtDelivery(current[0]))
        return current;
      var ids = new HashSet<Guid> { plan.DispatchId };
      var index = loads.ToList().IndexOf(current[0]);
      foreach (var next in loads.Skip(index + 1))
      {
        if (next.ExecutionLegId.HasValue)
        {
          if (
            next.Id == plan.DispatchId
            && next.ExecutionStatus == "planned"
            && next.TruckId == plan.TruckId
          )
            continue;
          // Work already accepted into execution ahead of this load is
          // still this truck's work, and the tank does not know the
          // difference. Where a run ends is one question, asked in one
          // place - here and in the arrival forecast alike.
          if (!PlanningWorkPolicy.ContinuesTheRun(next, plan.TruckId))
            break;
        }
        if (
          next.TruckId != plan.TruckId
          || next.AwaitingReceipt
          || next.Stops.Any(stop =>
            !stop.DriverOnly
            && stop.TruckId.HasValue
            && stop.TruckId != plan.TruckId
          )
          || !ids.Add(next.Id)
        )
          throw new RoutePlanningException(
            "A future dispatch assignment changed. Reload before finding fuel."
          );
        current.Add(next);
        if (!EndsAtDelivery(next))
          break;
      }
      return current;
    }
    var root = loads.Where(x => x.Id == plan.DispatchId).ToArray();
    if (
      root.Length != 1
      || root[0].ExecutionLegId.HasValue
      || root[0].TruckId != plan.TruckId
      || root[0].AssignmentRevision != plan.AssignmentRevision
    )
      throw new RoutePlanningException(
        "The selected assignment changed. Reload before finding fuel."
      );
    var start = loads.ToList().IndexOf(root[0]);
    var remaining = loads.Skip(start).ToList();
    if (
      remaining.Any(x => x.ExecutionLegId.HasValue)
      || remaining.Select(x => x.Id).Distinct().Count() != remaining.Count
      || remaining.Any(x =>
        x.TruckId != plan.TruckId
        || x.AwaitingReceipt
        || x.Stops.Any(stop =>
          !stop.DriverOnly
          && stop.TruckId.HasValue
          && stop.TruckId != plan.TruckId
        )
      )
    )
      throw new RoutePlanningException(
        "This itinerary crosses an execution transfer. Select its current execution before finding fuel."
      );
    return remaining;
  }

  internal static bool EndsAtDelivery(IWorkFacts load)
  {
    var last = load
      .Stops.Where(stop => !stop.DriverOnly)
      .OrderBy(stop => stop.Sequence)
      .LastOrDefault();
    var operation = last?.ManualAction ?? last?.Job;
    return string.Equals(
        operation,
        "Drop Off",
        StringComparison.OrdinalIgnoreCase
      )
      || string.Equals(
        operation,
        "Delivery",
        StringComparison.OrdinalIgnoreCase
      );
  }
}
