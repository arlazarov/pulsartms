using System.Collections.Immutable;
using Application.Features.Execution.Models;
using Application.Features.Execution.Queries;
using Application.Features.Routing.Models;
using Domain.Entities.Dispatch;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Services;

internal sealed record ExecutionWorkBatch(
  IReadOnlyList<TruckWorkSelection> Rows,
  TruckExecutionLoads Native,
  IReadOnlyDictionary<Guid, TruckWorkResources> Resources
);

public static class ExecutionWorkReader
{
  public static async Task<IReadOnlyList<TruckWorkSelection>> ReadAsync(
    IAppDbContext dbContext,
    DateOnly date,
    Guid? truckId,
    bool includePlanned,
    bool includeOverdue,
    CancellationToken cancellationToken
  ) =>
    (
      await ReadBatchAsync(
        dbContext,
        date,
        truckId,
        includePlanned,
        includeOverdue,
        cancellationToken
      )
    ).Rows;

  internal static async Task<ExecutionWorkBatch> ReadBatchAsync(
    IAppDbContext dbContext,
    DateOnly date,
    Guid? truckId,
    bool includePlanned,
    bool includeOverdue,
    CancellationToken cancellationToken,
    bool includeInactive = false,
    IReadOnlyCollection<Guid>? truckIds = null
  )
  {
    var requested = truckId.HasValue ? [truckId.Value] : truckIds;
    var fleet = await dbContext
      .Trucks.AsNoTracking()
      .Select(x => new
      {
        x.Id,
        x.UnitNumber,
        x.IsActive,
        x.DriverId,
        x.TrailerId,
        x.ConfigurationRevision,
        HasNativeExecution = dbContext.LoadExecutionLegs.Any(),
        DriverName = x.Driver != null ? x.Driver.Name : "",
        TrailerNumber = x.Trailer != null ? x.Trailer.UnitNumber : "",
      })
      .ToListAsync(cancellationToken);
    var byId = fleet.ToDictionary(x => x.Id);
    var byNumber = fleet
      .GroupBy(x => x.UnitNumber.Trim(), StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        x => x.Key,
        x => x.First(),
        StringComparer.OrdinalIgnoreCase
      );
    var query = dbContext
      .Dispatches.AsNoTracking()
      .Where(x =>
        dbContext.DispatchSourceLinks.Any(link =>
          link.DispatchId == x.Id && link.ExecutionReviewReason != null
        )
        || x.Status == "assigned"
        || x.Status == "in_transit"
        || includePlanned && (x.Status == "planned" || x.Status == "unassigned")
      );
    if (requested is not null)
    {
      var knownIds = fleet.Select(x => x.Id).ToArray();
      var numbers = byNumber
        .Where(x => requested.Contains(x.Value.Id))
        .Select(x => x.Key.ToUpperInvariant())
        .ToArray();
      query = query.Where(x =>
        x.PlanningTruckId.HasValue
          && requested.Contains(x.PlanningTruckId.Value)
        || x.TruckId.HasValue && requested.Contains(x.TruckId.Value)
        || (
          (!x.TruckId.HasValue || !knownIds.Contains(x.TruckId.Value))
          && numbers.Contains(x.TruckNumber.Trim().ToUpper())
        )
        || x.Stops.Any(s =>
          s.TruckId.HasValue && requested.Contains(s.TruckId.Value)
          || (
            (!s.TruckId.HasValue || !knownIds.Contains(s.TruckId.Value))
            && numbers.Contains(s.TruckNumber.Trim().ToUpper())
          )
        )
      );
    }
    var sourceRows = await query
      .Select(x => new
      {
        RequiresReview = dbContext.DispatchSourceLinks.Any(link =>
          link.DispatchId == x.Id && link.ExecutionReviewReason != null
        ),
        Load = new Load
        {
          Id = x.Id,
          TruckId = x.PlanningTruckId ?? x.TruckId,
          TruckNumber =
            x.PlanningTruck != null
              ? x.PlanningTruck.UnitNumber
              : x.TruckNumber,
          DriverName = x.DriverName,
          DriverId = x.DriverId,
          PlanningFromStopId = x.PlanningFromStopId,
          RouteChoiceRevision = x.RouteChoiceRevision,
          TrailerNumber = x.TrailerNumber,
          LoadNumber = x.LoadNumber,
          OrderNumber = x.OrderNumber,
          CustomerName = x.CustomerName,
          Status = x.Status,
          ShipDate = x.ShipDate,
          DeliveryDate = x.DeliveryDate,
          Stops = x
            .Stops.OrderBy(s => s.Sequence)
            .Select(s => new DispatchStop
            {
              Id = s.Id,
              Sequence = s.Sequence,
              TruckId = s.TruckId,
              TruckNumber = s.TruckNumber,
              DriverName = s.DriverName,
              TrailerNumber = s.TrailerNumber,
              Job = s.Job,
              City = s.City,
              Name = s.Name,
              ScheduledDate = s.ScheduledDate,
              ScheduledTime = s.ScheduledTime,
              PickedUpAt = s.PickedUpAt,
              DeliveredAt = s.DeliveredAt,
              DepartedAt = s.DepartedAt,
              ManualCompletedAt = s.ManualCompletedAt,
              CompletionOverride = s.CompletionOverride,
            })
            .ToList(),
        },
      })
      .ToListAsync(cancellationToken);
    var pendingReview = sourceRows
      .Where(x => x.RequiresReview)
      .Select(x => x.Load.Id)
      .ToHashSet();
    var loads = sourceRows.Select(x => CaptureSource(x.Load)).ToList();
    var native = fleet.Any(x => x.HasNativeExecution)
      ? await ExecutionLoads.ReadAsync(
        dbContext,
        truckId,
        loads.Select(x => x.Work.Id).ToArray(),
        cancellationToken,
        truckIds: requested
      )
      : new TruckExecutionLoads([], new HashSet<Guid>());
    loads = loads
      .Where(x => !native.OwnedDispatchIds.Contains(x.Work.Id))
      .Concat(native.Loads)
      .ToList();
    var rows = fleet.ToDictionary(
      x => x.Id.ToString(),
      x => new SelectionRow
      {
        Key = x.Id.ToString(),
        TruckId = x.Id,
        TruckNumber = x.UnitNumber,
        DriverName = x.DriverName,
        TrailerNumber = x.TrailerNumber,
      }
    );
    var active = fleet
      .Where(x => x.IsActive)
      .Select(x => x.Id.ToString())
      .ToHashSet();

    foreach (
      var snapshot in loads
        .Where(x =>
          pendingReview.Contains(x.Work.Id)
          || IsCurrentOrUpcoming(x.Work, date, includeOverdue)
        )
        .OrderBy(x => Order(x.Work))
    )
    {
      var load = snapshot.Work;
      var details = snapshot.Details;
      var assignments = load.ExecutionLegId.HasValue
        ? [(load.TruckId, load.TruckNumber)]
        : load
          .Stops.Select(x => (x.TruckId, x.TruckNumber))
          .Prepend((load.TruckId, load.TruckNumber))
          .Where(x =>
            x.TruckId.HasValue || !string.IsNullOrWhiteSpace(x.TruckNumber)
          )
          .ToList();
      if (assignments.Count == 0)
        assignments.Add((null, ""));
      var added = new HashSet<string>();
      foreach (var (id, number) in assignments)
      {
        var truck = id.HasValue ? byId.GetValueOrDefault(id.Value) : null;
        truck ??= string.IsNullOrWhiteSpace(number)
          ? null
          : byNumber.GetValueOrDefault(number.Trim());
        var key =
          truck?.Id.ToString()
          ?? id?.ToString()
          ?? (
            string.IsNullOrWhiteSpace(number)
              ? "unassigned"
              : "number:" + number.Trim().ToUpperInvariant()
          );
        if (
          requested is not null
          && (
            !(truck?.Id ?? id).HasValue
            || !requested.Contains((truck?.Id ?? id)!.Value)
          )
        )
          continue;
        if (!added.Add(key))
          continue;
        if (!rows.TryGetValue(key, out var row))
        {
          row = new()
          {
            Key = key,
            TruckId = truck?.Id ?? id,
            TruckNumber = number.Trim(),
          };
          rows.Add(key, row);
        }
        row.Loads.Add(Reference(snapshot));
        if (load.ExecutionStatus == "active")
        {
          row.DriverName = details.DriverName;
          row.TrailerNumber = details.TrailerNumber;
          continue;
        }
        var stop = load.Stops.FirstOrDefault(x =>
          x.TruckId == row.TruckId && row.TruckId.HasValue
          || !string.IsNullOrWhiteSpace(row.TruckNumber)
            && x.TruckNumber.Equals(
              row.TruckNumber,
              StringComparison.OrdinalIgnoreCase
            )
        );
        var labels = stop is null
          ? null
          : details.Stops.GetValueOrDefault(stop.Id);
        if (string.IsNullOrWhiteSpace(row.DriverName))
          row.DriverName = !string.IsNullOrWhiteSpace(labels?.DriverName)
            ? labels.DriverName
            : details.DriverName;
        if (string.IsNullOrWhiteSpace(row.TrailerNumber))
          row.TrailerNumber = !string.IsNullOrWhiteSpace(labels?.TrailerNumber)
            ? labels.TrailerNumber
            : details.TrailerNumber;
      }
    }

    return new(
      rows.Values.Where(x =>
          (
            requested is null
            || x.TruckId.HasValue && requested.Contains(x.TruckId.Value)
          ) && (includeInactive || active.Contains(x.Key) || x.Loads.Count > 0)
        )
        .Select(x => new TruckWorkSelection(
          x.Key,
          x.TruckId,
          x.TruckNumber,
          x.DriverName,
          x.TrailerNumber,
          x.Loads.ToImmutableArray()
        ))
        .ToArray(),
      native,
      fleet
        .Where(x => requested?.Contains(x.Id) == true)
        .ToDictionary(
          x => x.Id,
          x => new TruckWorkResources(
            x.UnitNumber,
            x.IsActive,
            x.DriverId,
            x.TrailerId,
            x.ConfigurationRevision
          )
        )
    );
  }

  private static bool IsCurrentOrUpcoming(
    RouteWorkSnapshot load,
    DateOnly date,
    bool includeOverdue
  )
  {
    if (load.ExecutionLegId.HasValue)
      return load.ExecutionStatus is "active" or "planned";
    var final = load.Stops.LastOrDefault(x =>
      x.StateAfter != "No truck"
      && (
        x.Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
        || x.Job.Equals("Delivery", StringComparison.OrdinalIgnoreCase)
      )
    );
    if (
      final?.CompletionOverride == true
      || final?.CompletionOverride != false
        && (
          final?.DeliveredAt is not null
          || final?.DepartedAt is not null
          || final?.ManualCompletedAt is not null
            && load.Stops.Where(s => s.StateAfter != "No truck")
              .All(s => s.IsCompleted)
        )
    )
      return false;
    // A missed appointment or UTC midnight does not complete an active load.
    if (HasStarted(load))
      return true;
    var end =
      load.DeliveryDate
      ?? load.Stops.LastOrDefault()?.ScheduledDate
      ?? load.ShipDate;
    return includeOverdue || end is null || end >= date;
  }

  private sealed class SelectionRow
  {
    public required string Key { get; init; }
    public Guid? TruckId { get; init; }
    public string TruckNumber { get; init; } = "";
    public string DriverName { get; set; } = "";
    public string TrailerNumber { get; set; } = "";
    public List<WorkLoadReference> Loads { get; } = [];
  }

  private static ExecutionLoadSnapshot CaptureSource(Load source)
  {
    var snapshot = ExecutionLoadProjection.Capture(source);
    var work = snapshot.Work;
    var start = work.PlanningFromStopId.HasValue
      ? work.Stops.SingleOrDefault(s => s.Id == work.PlanningFromStopId)
      : work.Stops.FirstOrDefault(s =>
        s.TruckId.HasValue
        || !string.IsNullOrWhiteSpace(s.TruckNumber)
        || s.ManualStateAfter is not null and not "No truck"
      );
    var stops = StopOperation
      .Resolve(
        work.Stops,
        work.PlanningFromStopId,
        (stop, job, state) => stop with { Job = job, StateAfter = state }
      )
      .Select(stop =>
        start is not null && stop.Sequence < start.Sequence
        || stop.StateAfter == "No truck"
          ? stop with
          {
            Job = stop.ManualAction is null ? "Driver start" : stop.Job,
            StateAfter = "No truck",
          }
          : stop
      )
      .ToImmutableArray();
    return snapshot with
    {
      Work = work with
      {
        TruckNumber = string.IsNullOrWhiteSpace(work.TruckNumber)
          ? start?.TruckNumber ?? ""
          : work.TruckNumber,
        Stops = stops,
      },
    };
  }

  private static WorkLoadReference Reference(ExecutionLoadSnapshot snapshot)
  {
    var load = snapshot.Work;
    var details = snapshot.Details;
    return new(
      load.Id,
      load.ExecutionLegId,
      load.AssignmentRevision,
      load.ExecutionStatus,
      load.LoadNumber,
      details.OrderNumber,
      details.CustomerName,
      details.DriverName,
      load.DriverId,
      Order(load),
      load.Stops.Select(stop => new WorkVisitReference(
          stop.Id,
          stop.City,
          stop.Name
        ))
        .ToImmutableArray()
    );
  }

  private static bool HasStarted(RouteWorkSnapshot load) =>
    load.ExecutionLegId.HasValue
      ? load.ExecutionStatus == "active"
      : load.Status.Equals("in_transit", StringComparison.OrdinalIgnoreCase)
        || load.Stops.Any(stop =>
          stop.PickedUpAt.HasValue || stop.ManualCompletedAt.HasValue
        );

  private static WorkOrderKey Order(RouteWorkSnapshot load)
  {
    var first = load.Stops.FirstOrDefault(stop =>
      stop.StateAfter != "No truck"
    );
    var start = (
      first?.ScheduledDate ?? load.ShipDate ?? DateOnly.MaxValue
    ).ToDateTime(first?.ScheduledTime ?? TimeOnly.MinValue);
    return new(
      load.ExecutionStatus == "active" ? WorkActivity.ActiveExecution
        : HasStarted(load) ? WorkActivity.Started
        : WorkActivity.Upcoming,
      start,
      load.LoadNumber
    );
  }
}
