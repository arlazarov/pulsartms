using System.Data.Common;
using Application.Features.Dispatch.Documents;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Support;

internal sealed class DispatchDocumentFixture : IAsyncDisposable
{
  private readonly SqliteConnection _connection = new("Data Source=:memory:");
  private readonly Queries _queries = new();
  public AppDbContext Db { get; private set; } = null!;
  public List<string> Commands => _queries.Commands;
  public ManualTimeProvider Clock { get; } = new();
  public User Actor { get; } =
    new()
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "document-operator",
      Name = "Document operator",
      Email = "documents@example.invalid",
    };
  public DispatchEntity Load { get; } =
    new() { Id = Guid.NewGuid(), LoadNumber = 1050 };

  public UploadDispatchDocumentHandler Upload(
    string? role = "Dispatch",
    string? identity = "document-operator"
  ) => new(Db, new Caller(identity), new Roles(role), Clock);

  public GetDispatchDocumentsHandler Read(string? role = "Dispatch") =>
    new(Db, new Caller(Actor.IdentityUserId), new Roles(role));

  public DownloadDispatchDocumentHandler Download(string? role = "Dispatch") =>
    new(Db, new Caller(Actor.IdentityUserId), new Roles(role));

  public UploadDispatchDocumentCommand Command() =>
    new(
      Load.Id,
      new(Guid.NewGuid(), "rc", "rate.pdf", "%PDF-1.7\n%%EOF"u8.ToArray())
    );

  public static async Task<DispatchDocumentFixture> CreateAsync()
  {
    var fixture = new DispatchDocumentFixture();
    await fixture._connection.OpenAsync();
    fixture.Db = new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(fixture._connection)
        .AddInterceptors(fixture._queries)
        .Options
    );
    await fixture.Db.Database.EnsureCreatedAsync();
    fixture.Db.Users.Add(fixture.Actor);
    fixture.Db.Dispatches.Add(fixture.Load);
    await fixture.Db.SaveChangesAsync();
    fixture.Commands.Clear();
    return fixture;
  }

  public async ValueTask DisposeAsync()
  {
    await Db.DisposeAsync();
    await _connection.DisposeAsync();
  }

  private sealed record Caller(string? IdentityUserId) : ICurrentUser
  {
    public bool IsAuthenticated => IdentityUserId is not null;
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

  private sealed class Queries : DbCommandInterceptor
  {
    public List<string> Commands { get; } = [];

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      Commands.Add(command.CommandText);
      return ValueTask.FromResult(result);
    }
  }
}
