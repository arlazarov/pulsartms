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

// What is pulled from outside and how often: reference data, dispatches,
// telemetry and positions, each on its own clock so a slow one does not
// hold up the rest.
public sealed partial class FleetSynchronizationOperation
{
  private async Task DataLoopAsync(
    TaskCompletionSource catalogsReady,
    CancellationToken ct
  )
  {
    while (true)
    {
      await RunJobAsync(
        "catalog",
        config.CatalogSeconds,
        async (services, token) =>
        {
          var result = await services
            .GetRequiredService<ISender>()
            .Send(new SyncFleetCommand(), token);
          if (!result.Success)
            throw new InvalidOperationException(
              "Fleet catalog synchronization failed."
            );
        },
        ct
      );
      await RunJobAsync(
        "assignments",
        config.AssignmentsSeconds,
        async (services, token) =>
        {
          var result = await services
            .GetRequiredService<ISender>()
            .Send(new SyncAssignmentsCommand(), token);
          if (!result.Success)
            throw new InvalidOperationException(
              "Fleet assignment synchronization failed."
            );
        },
        ct
      );
      catalogsReady.TrySetResult();
      await RunJobAsync(
        "fuel-exchange-rate",
        3600,
        async (services, token) =>
        {
          await services
            .GetRequiredService<FuelExchangeRateService>()
            .RefreshAsync(token);
        },
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
  }

  private async Task DispatchLoopAsync(Task catalogsReady, CancellationToken ct)
  {
    await catalogsReady.WaitAsync(ct);
    while (true)
    {
      await RunJobAsync(
        "dispatch",
        config.DispatchSeconds,
        async (services, token) =>
        {
          var result = await services
            .GetRequiredService<ISender>()
            .Send(new SyncDispatchesCommand(), token);
          if (!result.Success)
            throw new InvalidOperationException(
              "Dispatch synchronization failed."
            );
          if (result.Response > 0)
            lock (stateGate)
            {
              state.LastPlanningScan = default;
            }
        },
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
  }

  private async Task TelemetryLoopAsync(CancellationToken ct)
  {
    while (true)
    {
      await RunJobAsync(
        "telemetry",
        config.TelemetrySeconds,
        async (services, token) =>
        {
          var provider =
            services.GetRequiredService<IFleetTelemetryFeedProvider>();
          string? cursor;
          lock (stateGate)
            cursor = state.TelemetryCursor;
          var updates = new TelemetryFeedAccumulator(DateTime.UtcNow);
          var cursors = new HashSet<string>();
          while (true)
          {
            var feed = await provider.GetFeedAsync(cursor, token);
            updates.Append(feed);
            if (
              feed.HasNextPage
              && (feed.Cursor == cursor || !cursors.Add(feed.Cursor))
            )
              throw new InvalidOperationException(
                "Samsara returned a repeated feed cursor."
              );
            cursor = feed.Cursor;
            if (!feed.HasNextPage)
              break;
            await Task.Delay(250, token);
          }
          await PublishAsync(
            updates.Points.ToArray(),
            token,
            feed: updates,
            cursor: cursor
          );
        },
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
  }

  private async Task LocationLoopAsync(CancellationToken ct)
  {
    if (!config.HighFrequencyLocations)
    {
      await Task.Delay(Timeout.Infinite, ct);
      return;
    }
    while (true)
    {
      await RunJobAsync(
        "locations",
        config.TelemetrySeconds,
        (_, token) => PublishAsync([], token, true),
        ct
      );
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
  }
}
