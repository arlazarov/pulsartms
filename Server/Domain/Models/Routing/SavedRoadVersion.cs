using System.Security.Cryptography;
using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;

namespace Domain.Models.Routing;

public enum SavedRoadKind
{
  Base,
  Connection,
  Plan,
}

public sealed record SavedRoadVersion(
  WorkIdentity Work,
  SavedRoadKind Kind,
  string Signature
)
{
  public string? ProgressSignature { get; init; }

  public static SavedRoadVersion Base(NextLoadRouteVersion value) =>
    new(
      new(value.DispatchId, value.ExecutionLegId),
      SavedRoadKind.Base,
      Hash(
        new
        {
          value.BaseInputHash,
          value.BaseCalculatedAt,
          value.HasBaseGeometry,
        }
      )
    );

  public static SavedRoadVersion Connection(NextLoadRouteVersion value) =>
    new(
      new(value.DispatchId, value.ExecutionLegId),
      SavedRoadKind.Connection,
      Hash(
        new
        {
          value.PreviousDispatchId,
          value.PreviousExecutionLegId,
          value.DeadheadInputHash,
          value.DeadheadCalculatedAt,
          value.EmptyMiles,
          value.HasDeadheadGeometry,
        }
      )
    );

  public static SavedRoadVersion Plan(DispatchRoutePlan row, RoutePlan plan) =>
    Plan(
      new WorkIdentity(row.DispatchId, row.ExecutionLegId),
      new SavedRoutePlanMetadata(
        row.InputHash,
        row.TruckId,
        plan.TruckId,
        plan.Id,
        plan.Version,
        plan.Tracking,
        null
      )
      {
        DispatchId = row.DispatchId,
        ExecutionLegId = row.ExecutionLegId,
        PlanExecutionLegId = plan.ExecutionLegId,
        AssignmentRevision = plan.AssignmentRevision,
        StoredAssignmentRevision = row.AssignmentRevision,
        PlanDispatchId = plan.DispatchId,
        FromCurrentPosition = plan.FromCurrentPosition,
      }
    );

  public static SavedRoadVersion Plan(
    WorkIdentity work,
    SavedRoutePlanMetadata? value
  ) =>
    new(
      work,
      SavedRoadKind.Plan,
      Hash(
        value is null
          ? null
          : new
          {
            value.DispatchId,
            value.InputHash,
            value.TruckId,
            value.ExecutionLegId,
            value.StoredAssignmentRevision,
            value.PlanId,
            value.PlanDispatchId,
            value.PlanTruckId,
            value.PlanExecutionLegId,
            value.AssignmentRevision,
            value.Version,
            value.FromCurrentPosition,
          }
      )
    )
    {
      ProgressSignature = value is null
        ? null
        : TrackingSignature(value.Tracking),
    };

  public static string TrackingSignature(RouteStopTracking tracking) =>
    Hash(
      new
      {
        tracking.PassedStopIds,
        VisitedStops = tracking.VisitedStops.OrderBy(x => x.Key),
        tracking.NextStopId,
        tracking.NextStopLabel,
        tracking.AllStopsPassed,
        tracking.OffRouteSince,
      }
    );

  private static string Hash(object? value) =>
    Convert.ToHexString(
      SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))
    );
}
