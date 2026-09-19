using Application.Features.Shipments;
using Application.Features.Shipments.Models;
using Application.Features.Shipments.Services;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class ShipmentTests
{
  [Fact]
  public async Task PartialDraftsStayIndependentAndRetryDoesNotDuplicate()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var handler = Handler(fixture);
    var draft = new Shipment
    {
      Id = Guid.NewGuid(),
      LoadId = fixture.Load.Id,
      BillOfLading = "001234",
      Shipper = new() { Name = "Legal shipper" },
      Commodities =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Quantity = 5,
          PackageType = "box",
        },
      ],
    };
    var request = new SaveShipment(Guid.NewGuid(), 0, draft);
    var result = await handler.Handle(
      new SaveShipmentCommand(request),
      default
    );
    Assert.True(result.Success, string.Join(" ", result.Errors ?? []));
    Assert.Equal(1, result.Response!.Revision);
    Assert.NotEmpty(ShipmentRules.Missing(result.Response));
    var retry = await handler.Handle(new SaveShipmentCommand(request), default);
    Assert.True(retry.Success);
    Assert.Equal(1, await fixture.Db.Shipments.CountAsync());
    Assert.Equal(1, await fixture.Db.ShipmentSaveReceipts.CountAsync());
    var second = new Shipment { Id = Guid.NewGuid(), LoadId = fixture.Load.Id };
    Assert.True(
      (
        await handler.Handle(
          new SaveShipmentCommand(new(Guid.NewGuid(), 0, second)),
          default
        )
      ).Success
    );
    fixture.Db.ChangeTracker.Clear();
    var read = await handler.Handle(new GetShipments(draft.LoadId), default);
    Assert.Equal(2, read.Response!.Count);
    var saved = read.Response.Single(x => x.Id == draft.Id);
    Assert.Equal("001234", saved.BillOfLading);
    Assert.Equal("Legal shipper", saved.Shipper.Name);
    Assert.Equal("box", Assert.Single(saved.Commodities).PackageType);
    Assert.NotEqual(saved.Shipper.Name, fixture.Load.Stops.First().Name);
  }

  [Fact]
  public async Task StaleOrChangedRetryCannotOverwriteSavedShipment()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var handler = Handler(fixture);
    var draft = new Shipment { Id = Guid.NewGuid(), LoadId = fixture.Load.Id };
    var request = new SaveShipment(Guid.NewGuid(), 0, draft);
    Assert.True(
      (await handler.Handle(new SaveShipmentCommand(request), default)).Success
    );
    draft.BillOfLading = "Changed";
    Assert.Equal(
      409,
      (
        await handler.Handle(new SaveShipmentCommand(request), default)
      ).StatusCode
    );
    Assert.Equal(
      409,
      (
        await handler.Handle(
          new SaveShipmentCommand(request with { RequestId = Guid.NewGuid() }),
          default
        )
      ).StatusCode
    );
  }

  [Fact]
  public async Task UnauthorizedCallerCannotReadOrWrite()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var handler = Handler(fixture, "Driver");
    Assert.Equal(
      403,
      (
        await handler.Handle(new GetShipments(fixture.Load.Id), default)
      ).StatusCode
    );
    Assert.Equal(
      403,
      (
        await handler.Handle(
          new SaveShipmentCommand(
            new(
              Guid.NewGuid(),
              0,
              new() { Id = Guid.NewGuid(), LoadId = fixture.Load.Id }
            )
          ),
          default
        )
      ).StatusCode
    );
  }

  [Theory]
  [InlineData(0, "box")]
  [InlineData(-1, "box")]
  [InlineData(1, "unknown")]
  public async Task InvalidQuantitiesAndPackagesDoNotPersist(
    int count,
    string package
  )
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var result = await Handler(fixture)
      .Handle(
        new SaveShipmentCommand(
          new(
            Guid.NewGuid(),
            0,
            new()
            {
              Id = Guid.NewGuid(),
              LoadId = fixture.Load.Id,
              Commodities =
              [
                new()
                {
                  Id = Guid.NewGuid(),
                  Quantity = count,
                  PackageType = package,
                },
              ],
            }
          )
        ),
        default
      );
    Assert.Equal(400, result.StatusCode);
    Assert.Empty(await fixture.Db.Shipments.ToListAsync());
  }

  private static ShipmentsHandler Handler(
    StopCompletionFixture f,
    string role = "Dispatch"
  ) => new(f.Db, new Caller(), new Roles(role), f.Clock);

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => "operator";
  }

  private sealed class Roles(string role) : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult<string?>(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string value,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
