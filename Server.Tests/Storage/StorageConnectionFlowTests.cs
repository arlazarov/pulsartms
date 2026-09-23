using Application.Interfaces;
using Application.Storage;
using Domain.Entities;
using Domain.Entities.Storage;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Storage;

// Connecting a company's own Drive: the callback arrives without a session,
// so only a state bound to the company, the connection and a nonce, used
// once and within ten minutes, can complete it.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class StorageConnectionFlowTests
{
  [Fact]
  public async Task ACallbackWithTheRightStateConnectsOnceAndKeepsOnlyAProtectedGrant()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var consent = new FakeAuthorization();
    var start = await BeginAsync(f, consent);
    var state = State(start);

    var forged = await Flow(f.Db, consent)
      .Handle(
        new CompleteStorageConnectionCommand(
          state[..^4] + "AAAA",
          "good",
          null
        ),
        default
      );
    Assert.Equal("invalid", forged.Response);
    Assert.Equal(0, consent.Exchanges);

    var done = await Flow(f.Db, consent)
      .Handle(
        new CompleteStorageConnectionCommand(state, "good", null),
        default
      );
    Assert.Equal("connected", done.Response);
    Assert.Equal(64, consent.Verifier!.Length);
    f.Db.ChangeTracker.Clear();
    var connection = await f.Db.StorageConnections.AsNoTracking().SingleAsync();
    // Access only: nothing is created and files go nowhere until a folder
    // is picked.
    Assert.Equal(
      (StorageConnectionStates.NeedsRoot, (string?)null, false),
      (connection.State, connection.Root, connection.IsDefault)
    );
    Assert.DoesNotContain("refresh-secret", connection.ProtectedSecret);
    Assert.Contains(
      "refresh-secret",
      FileStorageTests.Secrets.Unprotect(
        Company.Amf,
        connection.Id,
        connection.ProtectedSecret!
      )
    );

    var replay = await Flow(f.Db, consent)
      .Handle(
        new CompleteStorageConnectionCommand(state, "good", null),
        default
      );
    Assert.Equal("invalid", replay.Response);
    Assert.Equal(1, consent.Exchanges);
  }

  [Fact]
  public async Task FilesGoOnlyIntoAPickedFolderThisAccountCanWriteTo()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var consent = new FakeAuthorization();
    var state = State(await BeginAsync(f, consent));
    await Flow(f.Db, consent)
      .Handle(
        new CompleteStorageConnectionCommand(state, "good", null),
        default
      );
    f.Db.ChangeTracker.Clear();
    var connection = await f.Db.StorageConnections.AsNoTracking().SingleAsync();
    var roots = Roots(f.Db);

    var session = await roots.Handle(
      new GetStoragePickerCommand(connection.Id),
      default
    );
    Assert.Equal("access", session.Response!.AccessToken);
    var refused = await roots.Handle(
      new ChooseStorageRootCommand(
        connection.Id,
        "someone-elses-folder",
        connection.Revision
      ),
      default
    );
    Assert.Equal(409, refused.StatusCode);
    f.Db.ChangeTracker.Clear();
    var stale = await Roots(f.Db)
      .Handle(
        new ChooseStorageRootCommand(
          connection.Id,
          "shared-folder-id-1",
          connection.Revision - 1
        ),
        default
      );
    Assert.Equal(409, stale.StatusCode);
    f.Db.ChangeTracker.Clear();
    var chosen = await Roots(f.Db)
      .Handle(
        new ChooseStorageRootCommand(
          connection.Id,
          "shared-folder-id-1",
          connection.Revision
        ),
        default
      );

    Assert.True(chosen.Success, string.Join(";", chosen.Errors ?? []));
    Assert.Equal(
      (StorageConnectionStates.Connected, "Loads"),
      (chosen.Response!.State, chosen.Response.RootName)
    );
  }

  // The process died after the state was spent, before the exchange
  // answered. The connection is left visibly failed, never a pending row
  // without a secret that nobody sees.
  [Fact]
  public async Task ACrashAfterTheStateIsSpentLeavesAVisibleFailedConnection()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var consent = new FakeAuthorization { Crash = true };
    var state = State(await BeginAsync(f, consent));

    await Assert.ThrowsAsync<HttpRequestException>(
      () =>
        Flow(f.Db, consent)
          .Handle(
            new CompleteStorageConnectionCommand(state, "good", null),
            default
          )
    );

    f.Db.ChangeTracker.Clear();
    var connection = await f.Db.StorageConnections.AsNoTracking().SingleAsync();
    Assert.Equal(
      (StorageConnectionStates.Failed, (string?)null),
      (connection.State, connection.ProtectedSecret)
    );
    Assert.Contains("Connect again", connection.LastError);
    var listed = (
      await new StorageConnectionHandlers(
        f.Db,
        FileStorageTests.Store(f.Db),
        [],
        [consent],
        [new FakePicker()],
        TimeProvider.System
      ).Handle(new GetStorageSettingsQuery(), default)
    )
      .Response!
      .Connections;
    Assert.Equal(connection.Id, Assert.Single(listed).Id);
  }

  [Fact]
  public async Task ARefusedOrExpiredConsentFailsWithoutAGrant()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var consent = new FakeAuthorization();
    var refused = State(await BeginAsync(f, consent));
    Assert.Equal(
      "failed",
      (
        await Flow(f.Db, consent)
          .Handle(
            new CompleteStorageConnectionCommand(
              refused,
              null,
              "access_denied"
            ),
            default
          )
      ).Response
    );

    var late = State(await BeginAsync(f, consent));
    await f
      .Db.StorageConnections.Where(x =>
        x.State == StorageConnectionStates.Pending
      )
      .ExecuteUpdateAsync(x =>
        x.SetProperty(c => c.PendingUntil, DateTime.UtcNow.AddMinutes(-1))
      );
    f.Db.ChangeTracker.Clear();
    Assert.Equal(
      "failed",
      (
        await Flow(f.Db, consent)
          .Handle(
            new CompleteStorageConnectionCommand(late, "good", null),
            default
          )
      ).Response
    );

    Assert.Equal(0, consent.Exchanges);
    Assert.All(
      await f.Db.StorageConnections.AsNoTracking().ToListAsync(),
      x =>
        Assert.Equal(
          (StorageConnectionStates.Failed, (string?)null),
          (x.State, x.ProtectedSecret)
        )
    );
  }

  [Fact]
  public async Task AnotherCompanysStateCannotCompleteThisCompanysConnection()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var consent = new FakeAuthorization();
    var state = State(await BeginAsync(f, consent));
    var other = Guid.NewGuid();
    f.Db.Companies.Add(
      new Company
      {
        Id = other,
        Key = "other",
        Name = "Other",
        IsActive = true,
      }
    );
    await f.Db.SaveChangesAsync();

    var parts = state.Split('.');
    var swapped = $"{other:N}.{parts[1]}.{parts[2]}";
    var result = await Flow(f.Db, consent, new TestCompany())
      .Handle(
        new CompleteStorageConnectionCommand(swapped, "good", null),
        default
      );

    Assert.Equal("invalid", result.Response);
    Assert.Equal(0, consent.Exchanges);
  }

  [Fact]
  public async Task AKindWithoutAClientRegistrationCannotStart()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var result = await Flow(
        f.Db,
        new FakeAuthorization { IsConfigured = false }
      )
      .Handle(
        new BeginStorageConnectionCommand(StorageKinds.GoogleDrive, null),
        default
      );
    Assert.Equal(409, result.StatusCode);
    Assert.Equal(
      400,
      (
        await Flow(f.Db, new FakeAuthorization())
          .Handle(
            new BeginStorageConnectionCommand(StorageKinds.Dropbox, null),
            default
          )
      ).StatusCode
    );
  }

  private static async Task<StorageConnectionStart> BeginAsync(
    DispatchSyncFixture f,
    FakeAuthorization consent
  )
  {
    if (!await f.Db.Users.AnyAsync())
    {
      f.Db.Users.Add(
        new User
        {
          Id = Guid.NewGuid(),
          IdentityUserId = "admin",
          Name = "Admin",
          Email = "admin@example.invalid",
        }
      );
      if (!await f.Db.Companies.AnyAsync(x => x.Id == Company.Amf))
        f.Db.Companies.Add(
          new Company
          {
            Id = Company.Amf,
            Key = "amfcarrier",
            Name = "AMF",
            IsActive = true,
          }
        );
      await f.Db.SaveChangesAsync();
    }
    var start = await Flow(f.Db, consent)
      .Handle(
        new BeginStorageConnectionCommand(StorageKinds.GoogleDrive, "Drive"),
        default
      );
    Assert.True(start.Success, string.Join(";", start.Errors ?? []));
    f.Db.ChangeTracker.Clear();
    return start.Response!;
  }

  private static string State(StorageConnectionStart start) =>
    Uri.UnescapeDataString(
      new Uri(start.AuthorizationUrl).Query.Split('&')[0]["?state=".Length..]
    );

  private static StorageRoots Roots(AppDbContext db) =>
    new(
      db,
      FileStorageTests.Store(db),
      [new FakePicker()],
      TimeProvider.System
    );

  private static StorageConnectionFlow Flow(
    AppDbContext db,
    FakeAuthorization consent,
    ICurrentCompany? company = null
  ) =>
    new(
      db,
      [consent],
      FileStorageTests.Secrets,
      company ?? new TestCompany(),
      new Caller(),
      TimeProvider.System
    );

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => "admin";
  }
}
