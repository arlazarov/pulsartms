using Application.Caching;
using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Commands;
using Application.Features.Execution.Queries;
using Application.Features.Routing.Background;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Support;

internal sealed class StopCompletionFixture : IAsyncDisposable
{
  private readonly SqliteConnection connection = new("Data Source=:memory:");
  private readonly List<PlanningTestServices> additionalPlanning = [];
  public AppDbContext Db { get; private set; } = null!;
  public ReadCache Reads { get; } = TestCache.Create();
  public RoutePreparationQueue Queue { get; } = TestCache.Preparation();
  public ManualTimeProvider Clock { get; } = new();
  public PlanningTestServices Planning { get; private set; } = null!;

  public CreateDispatchHandler Creator(string? role = "Dispatch") =>
    new(Db, new Caller(true), new Roles(role), Clock, Reads, Queue);

  public SetTruckAssignmentHandler AssignmentHandler(
    string? role = "Dispatch"
  ) =>
    new(
      Db,
      new Caller(true),
      new Roles(role),
      Clock,
      Reads,
      Queue,
      NullLogger<SetTruckAssignmentHandler>.Instance
    );

  public SetStopOperationHandler OperationHandler(string? role = "Dispatch") =>
    new(
      Db,
      new Caller(true),
      new Roles(role),
      Clock,
      Reads,
      Queue,
      NullLogger<SetStopOperationHandler>.Instance
    );

  public CorrectDispatchStopHandler CorrectionHandler(
    string? role = "Dispatch"
  ) => new(Db, new Caller(true), new Roles(role), Clock, Reads, Queue);

  public PreviewSwitchHandler SwitchPreview() =>
    new(Db, new Caller(true), new Roles("Dispatch"));

  public PlanSwitchHandler SwitchPlanner() =>
    new(
      Db,
      new Caller(true),
      new Roles("Dispatch"),
      Clock,
      Reads,
      Queue,
      NullLogger<PlanSwitchHandler>.Instance
    );

  public AcceptExecutionSourceChangesHandler SourceAcceptance() =>
    new(Db, new Caller(true), new Roles("Dispatch"), Clock, Reads, Queue);

  public GetExecutionSourceReviewHandler SourceReview() =>
    new(Db, new Caller(true), new Roles("Dispatch"), Clock);

  public UpdateDispatchWorkspaceHandler WorkspaceEditor() =>
    new(Db, new Caller(true), new Roles("Dispatch"), Clock, Reads, Queue);

  public CancelSwitchHandler SwitchCancellation() =>
    new(
      Db,
      new Caller(true),
      new Roles("Dispatch"),
      Clock,
      Reads,
      Queue,
      NullLogger<CancelSwitchHandler>.Instance
    );

  public ReleaseSwitchParticipantHandler SwitchRelease() =>
    new(
      Db,
      new Caller(true),
      new Roles("Dispatch"),
      Clock,
      Reads,
      Queue,
      NullLogger<ReleaseSwitchParticipantHandler>.Instance
    );

  public ReceiveSwitchParticipantHandler SwitchReceipt() =>
    new(
      Db,
      new Caller(true),
      new Roles("Dispatch"),
      Clock,
      Reads,
      Queue,
      NullLogger<ReceiveSwitchParticipantHandler>.Instance
    );

  public DispatchEntity Load { get; } =
    new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1383,
      Status = "assigned",
      Stops = Enumerable
        .Range(1, 5)
        .Select(i => new DispatchStop
        {
          Id = Guid.NewGuid(),
          Sequence = i,
          Job = i == 5 ? "Drop Off" : "Pick Up",
          Name = i == 2 ? "Other facility" : "Same facility",
          Address = i == 2 ? "2 Other Road" : "1 Main Road",
          City = "Fixture",
          Country = "US",
        })
        .ToList(),
    };
  public User Actor { get; } =
    new()
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "operator",
      Name = "Fixture operator",
      Email = "fixture@example.invalid",
    };

  public SetStopCompletionHandler Handler(
    string? role = "Dispatch",
    bool authenticated = true,
    IAppDbContext? db = null
  )
  {
    var planning = Planning;
    if (db is not null && db != Db)
    {
      planning = new(db, reads: Reads);
      additionalPlanning.Add(planning);
    }
    return new(
      db ?? Db,
      new Caller(authenticated),
      new Roles(role),
      Clock,
      Reads,
      Queue,
      planning.Routes
    );
  }

  public SetStopCompletionCommand Command(
    int index,
    DateTimeOffset? at,
    long? revision = null
  )
  {
    var s = Load.Stops[index];
    return new(
      Load.Id,
      s.Id,
      new(
        at,
        revision ?? s.ManualCompletionRevision,
        StopCompletionIdentity.Create(
          s.Id,
          s.Sequence,
          s.Job,
          s.Address,
          s.City,
          s.Province,
          s.Country,
          s.Name,
          s.TruckId,
          s.ScheduledDate,
          s.ScheduledTime
        )
      )
    );
  }

  public static async Task<StopCompletionFixture> CreateAsync()
  {
    var fixture = new StopCompletionFixture();
    await fixture.connection.OpenAsync();
    fixture.Db = new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(fixture.connection)
        .Options
    );
    await fixture.Db.Database.EnsureCreatedAsync();
    fixture.Db.Users.Add(fixture.Actor);
    fixture.Db.Dispatches.Add(fixture.Load);
    await fixture.Db.SaveChangesAsync();
    fixture.Planning = new(fixture.Db, reads: fixture.Reads);
    return fixture;
  }

  public async ValueTask DisposeAsync()
  {
    foreach (var planning in additionalPlanning)
      planning.Dispose();
    Planning.Dispose();
    Reads.Dispose();
    await Db.DisposeAsync();
    await connection.DisposeAsync();
  }

  private sealed record Caller(bool IsAuthenticated) : ICurrentUser
  {
    public string? IdentityUserId => "operator";
  }

  private sealed class Roles(string? role) : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string role,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
