using System.Text.Json;
using Application.Features.Dispatch.Queries;
using Domain.Entities;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class HistoryVerification
{
  public static async Task ReadFinancialsAsync(
    IServiceProvider services,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<AppDbContext>();
    if (db.ServingCompany != Company.Amf)
      throw new InvalidOperationException("Unexpected company.");
    var result = await services
      .GetRequiredService<ISender>()
      .Send(new GetDispatchQuery(Status: "completed", PageSize: 100), ct);
    if (!result.Success || result.Response is null)
      throw new InvalidOperationException("Completed read failed.");
    var page = result.Response;
    var ids = page.Items.Select(x => x.Id).ToArray();
    var links = await db
      .LoadExecutionLegs.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId))
      .Select(x => new { x.DispatchId, x.ExecutionLeg.Status })
      .ToArrayAsync(ct);
    var native = links.Select(x => x.DispatchId).ToHashSet();
    var saved = await db
      .DispatchDeadheads.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId))
      .Select(x => new
      {
        x.DispatchId,
        x.ExecutionLegId,
        x.Miles,
      })
      .ToArrayAsync(ct);
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          totalCompleted = page.TotalCount,
          checkedPageRows = page.Items.Count,
          native = page
            .Items.Where(x => native.Contains(x.Id))
            .Select(x => new
            {
              x.LoadNumber,
              x.EmptyMilesStatus,
              x.LoadedMiles,
              x.EmptyMiles,
              x.TotalMiles,
              x.TotalRatePerMile,
              saved = saved
                .Where(y => y.DispatchId == x.Id)
                .Select(y => new
                {
                  native = y.ExecutionLegId.HasValue,
                  y.Miles,
                })
                .ToArray(),
              execution = links
                .Where(y => y.DispatchId == x.Id)
                .Select(y => y.Status)
                .ToArray(),
            }),
        }
      )
    );
  }

  public static async Task ReadAsync(
    IServiceProvider services,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<AppDbContext>();
    if (db.ServingCompany != Company.Amf)
      throw new InvalidOperationException("Unexpected company.");
    var retired = new[] { "11", "11001", "11002", "11003", "11004" };
    var examples = new[] { 1016, 1059, 1133, 1172 };
    var scope = db
      .DispatchSourceLinks.AsNoTracking()
      .Where(x => x.Provider == "torqueai");
    var states = await scope
      .Where(x => retired.Contains(x.Dispatch.TruckNumber))
      .GroupBy(x => new { x.Dispatch.TruckNumber, x.Dispatch.Status })
      .Select(x => new
      {
        x.Key.TruckNumber,
        x.Key.Status,
        Count = x.Count(),
      })
      .ToArrayAsync(ct);
    var exampleStates = await scope
      .Where(x => examples.Contains(x.Dispatch.LoadNumber))
      .Select(x => new
      {
        x.DispatchId,
        x.Dispatch.LoadNumber,
        x.Dispatch.Status,
      })
      .ToArrayAsync(ct);
    var board = await services
      .GetRequiredService<ISender>()
      .Send(
        new GetDispatchBoardQuery(
          PageSize: 100,
          IncludeOverdue: true,
          IncludePlanned: true,
          IncludeHos: false,
          IncludeFinancials: false,
          IncludeEta: false,
          IdentitiesOnly: true
        ),
        ct
      );
    if (!board.Success || board.Response is null)
      throw new InvalidOperationException("Board verification failed.");
    var page = board.Response;
    if (page.TotalCount > 100)
      throw new InvalidOperationException("Board exceeds bounded coverage.");
    var shown = page
      .Items.SelectMany(x => x.Dispatches)
      .Select(x => x.Id)
      .ToHashSet();
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          retiredTrucks = states,
          examples = exampleStates.Select(x => new
          {
            x.LoadNumber,
            x.Status,
            inCurrentBoard = shown.Contains(x.DispatchId),
          }),
          boardTrucksChecked = page.TotalCount,
          remainingSent = await scope.CountAsync(
            x => x.Dispatch.Status == "sent",
            ct
          ),
        }
      )
    );
  }
}
