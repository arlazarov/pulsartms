using System.Linq.Expressions;
using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Queries;

// The Completed tab's filter: LoadCompletion said in a form the database
// can run, over the same stored facts the C# owner reads.
//
// A load in accepted execution - with a leg that is not cancelled - is
// completed when all such legs are: its accepted execution decides, not
// the source's stops or status (a source ahead of accepted execution is a
// conflict for review, not a completion). Cancelled legs were replaced or
// withdrawn and say nothing about the work; a load whose legs are all
// cancelled is read like any other.
// Any other load is completed when closed, or when its cargo is delivered
// and the truck's work on it finished (CargoDelivery, TruckWorkCompletion)
// over its source stops as DispatchProjection.Complete reads them:
// - a stop is driver-only before the truck's start (the planning start
//   stop, else the first stop with a truck or a confirmed state other than
//   "No truck"), and while a confirmed "No truck" state carries on to later
//   stops that have no truck, are not the planning start and confirm
//   nothing else (StopOperation.Resolve);
// - its job is the dispatcher's action, else the imported job;
// - it is done when overridden so, or not overridden and any actual is
//   recorded.
// Parity with the C# owner is tested on SQLite and PostgreSQL
// (CompletedLoadsParityTests).
public static class CompletedLoads
{
  public static Expression<Func<DispatchEntity, bool>> Filter(
    IQueryable<LoadExecutionLeg> links
  ) =>
    (Expression<Func<DispatchEntity, bool>>)new Expander(links).Visit(Written);

  // For loads a page lists from their source rows: whether each one is in
  // accepted execution and, if so, whether all its legs are completed -
  // one batched read for the page, the answer LoadCompletion uses for them.
  public static async Task MarkExecutionAsync(
    IQueryable<LoadExecutionLeg> links,
    IReadOnlyCollection<DispatchResponse> loads,
    CancellationToken ct
  )
  {
    var ids = loads.Select(x => x.Id).Distinct().ToArray();
    if (ids.Length == 0)
      return;
    var finished = await links
      .Where(x => ids.Contains(x.DispatchId))
      .GroupBy(x => x.DispatchId)
      .Select(x => new
      {
        x.Key,
        Finished = x.All(l =>
          l.ExecutionLeg.Status == "completed"
          || l.ExecutionLeg.Status == "cancelled"
        ),
        Owned = x.Any(l => l.ExecutionLeg.Status != "cancelled"),
      })
      .ToDictionaryAsync(x => x.Key, ct);
    foreach (var load in loads)
      load.ExecutionFinished =
        finished.TryGetValue(load.Id, out var legs) && legs.Owned
          ? legs.Finished
          : null;
  }

  private static readonly Expression<Func<DispatchEntity, int?>> Start = x =>
    x.PlanningFromStopId != null
      ? x
        .Stops.Where(p => p.Id == x.PlanningFromStopId)
        .Select(p => (int?)p.Sequence)
        .FirstOrDefault()
      : x
        .Stops.Where(p =>
          p.TruckId != null
          || p.TruckNumber != ""
          || p.ManualStateAfter != null && p.ManualStateAfter != "No truck"
        )
        .OrderBy(p => p.Sequence)
        .Select(p => (int?)p.Sequence)
        .FirstOrDefault();

  // Whether the truck attends a stop: at or after the start, and not in a
  // confirmed "No truck" state, set on it or carried to it.
  private static readonly Expression<
    Func<DispatchEntity, DispatchStop, bool>
  > Attended = (x, s) =>
    (StartOf(x) == null || s.Sequence >= StartOf(x))
    && s.ManualStateAfter != "No truck"
    && !x.Stops.Any(p =>
      p.Sequence < s.Sequence
      && p.ManualStateAfter == "No truck"
      && !x.Stops.Any(q =>
        q.Sequence > p.Sequence
        && q.Sequence <= s.Sequence
        && (
          q.ManualStateAfter != null
          || q.TruckId != null
          || q.TruckNumber != ""
          || q.Id == x.PlanningFromStopId
        )
      )
    );

  private static readonly Expression<Func<DispatchStop, bool>> Done = s =>
    s.CompletionOverride == true
    || s.CompletionOverride == null
      && (
        s.ManualCompletedAt != null
        || s.DepartedAt != null
        || s.DeliveredAt != null
        || s.PickedUpAt != null
      );

  private static readonly Expression<Func<DispatchStop, bool>> Delivery = s =>
    (s.ManualAction ?? s.Job).ToLower() == "drop off"
    || (s.ManualAction ?? s.Job).ToLower() == "delivery";

  private static readonly Expression<Func<DispatchEntity, bool>> Written = x =>
    Owned(x)
      ? LegsFinished(x)
      : x.Status == "completed"
        || x.Stops.Any(final =>
          IsAttended(x, final)
          && IsDelivery(final)
          && !x.Stops.Any(later =>
            later.Sequence > final.Sequence
            && IsAttended(x, later)
            && IsDelivery(later)
          )
          && (
            final.CompletionOverride == true
            || final.CompletionOverride != false
              && (
                final.DeliveredAt != null
                || final.DepartedAt != null
                || final.ManualCompletedAt != null
                  && x.Stops.All(s =>
                    s.Sequence > final.Sequence
                    || !IsAttended(x, s)
                    || IsDone(s)
                  )
              )
          )
          && x.Stops.All(s =>
            s.Sequence <= final.Sequence || !IsAttended(x, s) || IsDone(s)
          )
        );

  // Markers, replaced by the expressions above before translation.
  private static int? StartOf(DispatchEntity x) => throw Marker();

  private static bool IsAttended(DispatchEntity x, DispatchStop s) =>
    throw Marker();

  private static bool IsDone(DispatchStop s) => throw Marker();

  private static bool IsDelivery(DispatchStop s) => throw Marker();

  private static bool Owned(DispatchEntity x) => throw Marker();

  private static bool LegsFinished(DispatchEntity x) => throw Marker();

  private static InvalidOperationException Marker() =>
    new("Replaced before translation.");

  private sealed class Expander(IQueryable<LoadExecutionLeg> links)
    : ExpressionVisitor
  {
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
      if (node.Method.DeclaringType != typeof(CompletedLoads))
        return base.VisitMethodCall(node);
      var arguments = node.Arguments.Select(x => Visit(x)!).ToArray();
      LambdaExpression replacement = node.Method.Name switch
      {
        nameof(StartOf) => Start,
        nameof(IsAttended) => Attended,
        nameof(IsDone) => Done,
        nameof(IsDelivery) => Delivery,
        nameof(Owned) => (Expression<Func<DispatchEntity, bool>>)(
          x =>
            links.Any(l =>
              l.DispatchId == x.Id && l.ExecutionLeg.Status != "cancelled"
            )
        ),
        nameof(LegsFinished) => (Expression<Func<DispatchEntity, bool>>)(
          x =>
            links
              .Where(l =>
                l.DispatchId == x.Id && l.ExecutionLeg.Status != "cancelled"
              )
              .All(l => l.ExecutionLeg.Status == "completed")
        ),
        _ => throw new InvalidOperationException(node.Method.Name),
      };
      // Expanded again: a replacement may itself use a marker.
      return Visit(Inline(replacement, arguments))!;
    }

    private static Expression Inline(
      LambdaExpression lambda,
      IReadOnlyList<Expression> arguments
    )
    {
      var body = lambda.Body;
      for (var i = 0; i < arguments.Count; i++)
        body = new Replace(lambda.Parameters[i], arguments[i]).Visit(body)!;
      return body;
    }
  }

  private sealed class Replace(Expression from, Expression to)
    : ExpressionVisitor
  {
    public override Expression? Visit(Expression? node) =>
      node == from ? to : base.Visit(node);
  }
}
