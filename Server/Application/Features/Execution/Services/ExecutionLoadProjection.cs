using System.Collections.Immutable;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using Domain.Rules.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Services;

public static class ExecutionLoadProjection
{
  public static ExecutionLoadSnapshot Capture(Load source) =>
    new(RouteWorkProjection.Capture(source), Details(source, source.Stops));

  public static ExecutionLoadSnapshot Capture(
    Load source,
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> stops
  ) =>
    new(
      RouteWorkProjection.Capture(source, leg, stops),
      Details(source, stops) with
      {
        DriverName = "",
        TrailerNumber = "",
      }
    );

  private static ExecutionLoadDetails Details(
    Load source,
    IReadOnlyList<DispatchStop> stops
  ) =>
    new(
      source.OrderNumber,
      source.OrderDate,
      source.InvoiceDate,
      source.CustomerName,
      source.LastSyncedAt,
      source.DriverName,
      source.TrailerNumber,
      stops.ToImmutableDictionary(
        x => x.Id,
        x => new ExecutionStopDetails(
          x.DriverName,
          x.CoDriverName,
          x.TrailerNumber,
          x.StopNo,
          x.Weight,
          x.WeightUnit,
          x.Pieces,
          x.Pallets,
          x.Temperature,
          x.TemperatureUnit,
          x.OperationRecordedAt,
          x.ManualCompletedByName,
          x.ManualCompletionRecordedAt
        )
      )
    );
}
