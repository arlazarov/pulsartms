using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Synchronization.Services;

// Keeping every truck's plan current as its work and position change.
public sealed partial class FleetSynchronizationOperation
{
  private async Task PlanningLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await RunJobAsync(
        "planning",
        config.PlanningSeconds,
        async (services, token) =>
        {
          var mediator = services.GetRequiredService<ISender>();
          var rows = new List<TruckDispatchBoardResponse>();
          for (var page = 1; ; page++)
          {
            var board = await mediator.Send(
              new GetDispatchBoardQuery(
                Page: page,
                PageSize: 100,
                IncludeHos: false,
                IncludeFinancials: false,
                IncludeEta: false,
                IdentitiesOnly: true
              ),
              token
            );
            if (!board.Success || board.Response is null)
              throw new InvalidOperationException(
                "Dispatch board is unavailable."
              );
            rows.AddRange(
              board.Response.Items.Where(x =>
                x.TruckId.HasValue && x.Dispatches.Count > 0
              )
            );
            if (!board.Response.HasNextPage)
              break;
          }
          List<Guid> selected;
          lock (stateGate)
          {
            var ids = rows.Select(x => x.TruckId!.Value).ToHashSet();
            state.PendingTrucks.RemoveAll(x => !ids.Contains(x));
            if (
              state.PendingTrucks.Count == 0
              || state.LastPlanningScan == default
            )
              foreach (var id in ids)
                if (!state.PendingTrucks.Contains(id))
                  state.PendingTrucks.Add(id);
            state.LastPlanningScan = DateTime.UtcNow;
            selected = state
              .PendingTrucks.Take(config.MaxTrucksPerPlanningCycle)
              .ToList();
          }
          foreach (var id in selected)
          {
            var row = rows.First(x => x.TruckId == id);
            await RunJobAsync(
              $"truck:{id}",
              config.PlanningSeconds,
              async (truckServices, truckToken) =>
              {
                var planner = truckServices.GetRequiredService<ISender>();
                var prepared = await planner.Send(
                  new PrepareTruckPlanningCommand(id),
                  truckToken
                );
                if (!prepared.Success || prepared.Response is null)
                  throw new InvalidOperationException(
                    "Route preparation will retry."
                  );
                var current = prepared.Response;
                if (current.State?.Plan is null && current.DispatchId.HasValue)
                  throw new InvalidOperationException(
                    "Route preparation will retry."
                  );
                var index = row.Dispatches.FindIndex(x =>
                  x.Id == current.DispatchId
                  && x.ExecutionLegId == current.ExecutionLegId
                );
                if (index >= 0)
                {
                  var preparation =
                    truckServices.GetRequiredService<RoutePreparationQueue>();
                  foreach (
                    var next in row
                      .Dispatches.Skip(index + 1)
                      .Where(x => !x.ExecutionLegId.HasValue)
                  )
                    preparation.Request(next.Id, id);
                }
                await RunJobAsync(
                  $"fuel:{id}",
                  config.PlanningSeconds,
                  (fuelServices, fuelToken) =>
                    fuelServices
                      .GetRequiredService<FuelPriceRefreshService>()
                      .RefreshAsync(current, fuelToken),
                  truckToken
                );
                if (index >= 0)
                  foreach (
                    var next in row
                      .Dispatches.Skip(index + 1)
                      .Take(config.UpcomingRoutesPerTruck)
                  )
                  {
                    var upcoming = await planner.Send(
                      new PrepareUpcomingPlanningCommand(
                        next.Id,
                        next.ExecutionLegId,
                        id
                      ),
                      truckToken
                    );
                    if (!upcoming.Success)
                      throw new InvalidOperationException(
                        "Upcoming route preparation will retry."
                      );
                  }
              },
              token
            );
            lock (stateGate)
              state.PendingTrucks.Remove(id);
          }
        },
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
  }
}
