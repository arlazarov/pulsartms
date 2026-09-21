using Application.Features.Dispatch.Models;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Queries;

internal static class DispatchWorkProjection
{
  public static TruckDispatchBoardResponse ToBoardRow(
    TruckWorkSelection selection
  ) =>
    new()
    {
      Key = selection.Key,
      TruckId = selection.TruckId,
      TruckNumber = selection.TruckNumber,
      DriverName = selection.DriverName,
      TrailerNumber = selection.TrailerNumber,
      Dispatches = selection
        .Loads.Select(load => new DispatchResponse
        {
          Id = load.Id,
          ExecutionLegId = load.ExecutionLegId,
          AssignmentRevision = load.AssignmentRevision,
          ExecutionStatus = load.ExecutionStatus,
          LoadNumber = load.LoadNumber,
          OrderNumber = load.OrderNumber,
          CustomerName = load.CustomerName,
          DriverName = load.DriverName,
          Stops = load
            .Visits.Select(visit => new DispatchStopResponse
            {
              Id = visit.Id,
              City = visit.City,
              Name = visit.Name,
            })
            .ToList(),
        })
        .ToList(),
    };
}
