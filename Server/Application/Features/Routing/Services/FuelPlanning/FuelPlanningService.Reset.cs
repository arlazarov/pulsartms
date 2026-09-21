using Application.Diagnostics;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;

namespace Application.Features.Routing.Services.FuelPlanning;

// Returning a manually edited plan to automatic. It is its own act, not a
// kind of edit: it throws the dispatcher's choices away, so it has to be
// asked for by name and against the revision that was on screen.
public sealed partial class FuelPlanningService
{
  public async Task<FuelCalculationResult> ResetAsync(
    Guid dispatchId,
    DateTime? expectedCalculatedAt,
    CancellationToken ct,
    Guid? executionLegId = null,
    long? assignmentRevision = null
  )
  {
    var assignedLoad = await FuelLoadAsync(
      dispatchId,
      executionLegId,
      assignmentRevision,
      ct
    );
    var truckId =
      assignedLoad.TruckId
      ?? throw new RoutePlanningException(
        "A truck assignment is required for fuel planning."
      );
    var gate = TruckGates.For(truckId);
    await GateWait.WaitAsync(gate, "FuelTruck", ct);
    try
    {
      await GateWait.WaitAsync(SearchSlots, "FuelSearch", ct);
      try
      {
        await RequireCurrentAsync(
          dispatchId,
          truckId,
          ct,
          executionLegId,
          assignmentRevision
        );
        var state = await plans.GetAsync(assignedLoad, ct);
        return await BuildCoreAsync(
          dispatchId,
          new(state.Profile)
          {
            ExecutionLegId = executionLegId,
            AssignmentRevision = assignmentRevision,
          },
          ct,
          true,
          expectedCalculatedAt
        );
      }
      finally
      {
        SearchSlots.Release();
      }
    }
    finally
    {
      gate.Release();
    }
  }
}
