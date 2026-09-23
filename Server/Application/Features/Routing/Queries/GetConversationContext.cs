using Application.Features.Execution.Services;
using Application.Models;
using Application.Reference;

namespace Application.Features.Routing.Queries;

// Who the conversation is with and what they are driving. The trucks come
// from the driver's planned and active execution legs, as driver or as
// co-driver; only when there are none, from the fleet's assignment of the
// truck. One truck: its current and upcoming loads as the Dispatch board
// reads them (ExecutionWorkReader), offered as places to file a file.
// Several trucks: all are shown and no loads are offered, since choosing
// one would be a guess. Read when a conversation is opened, not per
// message.
public sealed record GetConversationContextQuery(Guid Id)
  : IRequest<RequestResponse<ConversationContext>>;

public static class ContextStates
{
  public const string Unmatched = "unmatched";
  public const string NoTruck = "no-truck";
  public const string OneTruck = "one-truck";
  public const string SeveralTrucks = "several-trucks";
}

public sealed record ConversationContext(
  Guid? DriverId,
  string? DriverName,
  string State,
  IReadOnlyList<ContextTruck> Trucks,
  IReadOnlyList<ContextLoad> Loads
);

// Role: driver or co-driver on an execution leg, or assigned in the fleet.
public sealed record ContextTruck(Guid Id, string Number, string Role);

public sealed record ContextLoad(
  Guid Id,
  int LoadNumber,
  string CustomerName,
  string? Status,
  IReadOnlyList<string> Places
);

public sealed class ConversationContextHandler(
  IAppDbContext db,
  ICurrentUser caller,
  FleetNames names,
  ActiveTransfers transfers,
  TimeProvider clock
)
  : IRequestHandler<
    GetConversationContextQuery,
    RequestResponse<ConversationContext>
  >
{
  public const int MaximumLoads = 5;
  private static readonly string[] Live = ["planned", "active"];

  public async Task<RequestResponse<ConversationContext>> Handle(
    GetConversationContextQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is null)
      return RequestResponse<ConversationContext>.Fail("Access denied.", 403);
    var conversation = await db
      .Conversations.AsNoTracking()
      .Where(x => x.Id == request.Id)
      .Select(x => new
      {
        x.DriverId,
        DriverName = db
          .Drivers.Where(d => d.Id == x.DriverId)
          .Select(d => d.Name)
          .FirstOrDefault(),
      })
      .SingleOrDefaultAsync(ct);
    if (conversation is null)
      return RequestResponse<ConversationContext>.Fail(
        "Conversation not found.",
        404
      );
    if (conversation.DriverId is not { } driver)
      return Ok(ContextStates.Unmatched, [], []);
    var trucks = await TrucksAsync(driver, ct);
    if (trucks.Count != 1)
      return Ok(
        trucks.Count == 0 ? ContextStates.NoTruck : ContextStates.SeveralTrucks,
        trucks,
        []
      );
    var work = await ExecutionWorkReader.ReadAsync(
      db,
      DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
      names,
      transfers,
      trucks[0].Id,
      includePlanned: true,
      includeOverdue: false,
      ct
    );
    return Ok(
      ContextStates.OneTruck,
      trucks,
      [
        .. work.SelectMany(x => x.Loads)
          .Take(MaximumLoads)
          .Select(x => new ContextLoad(
            x.Id,
            x.LoadNumber,
            x.CustomerName,
            x.ExecutionStatus,
            [.. x.Visits.Select(v => v.City).Where(c => c.Length > 0)]
          )),
      ]
    );

    RequestResponse<ConversationContext> Ok(
      string state,
      IReadOnlyList<ContextTruck> trucks,
      IReadOnlyList<ContextLoad> loads
    ) =>
      RequestResponse<ConversationContext>.Ok(
        new(
          conversation.DriverId,
          conversation.DriverName,
          state,
          trucks,
          loads
        )
      );
  }

  private async Task<IReadOnlyList<ContextTruck>> TrucksAsync(
    Guid driver,
    CancellationToken ct
  )
  {
    var legs = await db
      .ExecutionLegs.AsNoTracking()
      .Where(x =>
        Live.Contains(x.Status)
        && (x.DriverId == driver || x.CoDriverId == driver)
      )
      .Select(x => new
      {
        x.TruckId,
        Role = x.DriverId == driver ? "driver" : "co-driver",
        Number = db
          .Trucks.Where(t => t.Id == x.TruckId)
          .Select(t => t.UnitNumber)
          .FirstOrDefault(),
      })
      .ToListAsync(ct);
    if (legs.Count > 0)
      return
      [
        .. legs.GroupBy(x => x.TruckId)
          .Select(x => new ContextTruck(
            x.Key,
            x.First().Number ?? "",
            x.Any(l => l.Role == "driver") ? "driver" : "co-driver"
          ))
          .OrderBy(x => x.Number),
      ];
    return await db
      .Trucks.AsNoTracking()
      .Where(x => x.DriverId == driver)
      .OrderBy(x => x.UnitNumber)
      .Select(x => new ContextTruck(x.Id, x.UnitNumber, "assigned"))
      .ToListAsync(ct);
  }
}
