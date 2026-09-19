using Application.Features.Border;
using Application.Features.Border.Interfaces;
using Application.Features.Border.Models;
using Application.Features.Border.Services;
using Application.Features.Shipments;
using Application.Features.Shipments.Models;
using Application.Interfaces;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class BorderPreparationTests
{
  [Fact]
  public async Task SavedCrossingKeepsShipmentVersionAndEncryptedCrewOnRetry()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var protection = new BorderDataProtection(
      new EphemeralDataProtectionProvider()
    );
    var shipmentHandler = new ShipmentsHandler(
      f.Db,
      new Caller(),
      new Roles("Admin"),
      f.Clock
    );
    var shipment = new Shipment
    {
      Id = Guid.NewGuid(),
      LoadId = f.Load.Id,
      BillOfLading = "ORIGINAL",
      Commodities =
      [
        new()
        {
          Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
          Description = "First",
        },
        new()
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
          Description = "Second",
        },
      ],
    };
    var source = await shipmentHandler.Handle(
      new SaveShipmentCommand(new(Guid.NewGuid(), 0, shipment)),
      default
    );
    Assert.True(source.Success);
    var draft = new BorderCrossing
    {
      Id = Guid.NewGuid(),
      DestinationCountry = "CA",
      Shipments =
      [
        new()
        {
          Id = Guid.NewGuid(),
          ShipmentId = shipment.Id,
          ShipmentRevision = 1,
          ParsNumber = "0001234",
          Procedure = "PARS",
        },
      ],
      Crew =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Role = "passenger",
          Details = new()
          {
            FirstName = "Synthetic",
            LastName = "Passenger",
            Documents =
            [
              new()
              {
                Id = Guid.NewGuid(),
                Type = "passport",
                Number = "TEST-DOCUMENT-ONLY",
              },
            ],
          },
        },
      ],
    };
    var handler = Save(f, protection);
    var request = new SaveBorderCrossing(Guid.NewGuid(), 0, draft);
    var saved = await handler.Handle(new(request), default);
    Assert.True(saved.Success, string.Join(" ", saved.Errors ?? []));
    Assert.Equal(
      "ORIGINAL",
      saved.Response!.Shipments[0].Snapshot!.BillOfLading
    );
    Assert.Equal(
      new[] { "First", "Second" },
      saved
        .Response.Shipments[0]
        .Snapshot!.Commodities.Select(x => x.Description)
    );
    Assert.True((await handler.Handle(new(request), default)).Success);
    Assert.Equal(1, await f.Db.BorderSaveReceipts.CountAsync());
    var stored = await f.Db.BorderCrossings.AsNoTracking().SingleAsync();
    Assert.DoesNotContain(
      "TEST-DOCUMENT-ONLY",
      stored.Crew[0].ProtectedDetails
    );
    Assert.DoesNotContain(
      "TEST-DOCUMENT-ONLY",
      (await f.Db.BorderSaveReceipts.SingleAsync()).ProtectedResponse
    );
    shipment.BillOfLading = "UPDATED";
    Assert.True(
      (
        await shipmentHandler.Handle(
          new SaveShipmentCommand(new(Guid.NewGuid(), 1, shipment)),
          default
        )
      ).Success
    );
    f.Db.ChangeTracker.Clear();
    var query = Query(f, protection);
    var read = await query.Handle(new GetBorderCrossing(draft.Id), default);
    Assert.Equal(
      "ORIGINAL",
      read.Response!.Shipments[0].Snapshot!.BillOfLading
    );
    Assert.Equal(
      "TEST-DOCUMENT-ONLY",
      read.Response.Crew[0].Details.Documents[0].Number
    );
    var check = await query.Handle(
      new CheckBorderCrossing(read.Response),
      default
    );
    Assert.Contains(
      check.Response!,
      x => x.Message.Contains("source shipment changed")
    );
    Assert.Equal(
      409,
      (
        await handler.Handle(
          new(request with { RequestId = Guid.NewGuid() }),
          default
        )
      ).StatusCode
    );
    Assert.Equal(
      403,
      (
        await Query(f, protection, "Dispatch")
          .Handle(new GetBorderCrossing(draft.Id), default)
      ).StatusCode
    );
    Assert.Equal(
      403,
      (
        await Save(f, protection, "Dispatch").Handle(new(request), default)
      ).StatusCode
    );
  }

  [Fact]
  public async Task YardDriverSelectionDoesNotFollowLaterExecutionChanges()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var protection = new BorderDataProtection(
      new EphemeralDataProtectionProvider()
    );
    var a = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "border-a",
      Name = "Pickup driver",
    };
    var b = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "border-b",
      Name = "Border driver",
    };
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "border-truck",
      UnitNumber = "BORDER-DEMO",
    };
    f.Db.Drivers.AddRange(a, b);
    f.Db.Trucks.Add(truck);
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new() { Id = Guid.NewGuid() },
      TruckId = truck.Id,
      DriverId = a.Id,
      Revision = 1,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Load.Id,
          Position = 0,
          Job = "Pick Up",
        },
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Load.Id,
          Position = 1,
          Job = "Delivery",
          HasDriverOverride = true,
          DriverId = b.Id,
        },
      ],
    };
    f.Db.ExecutionLegs.Add(leg);
    await f.Db.SaveChangesAsync();
    var query = Query(f, protection);
    var options = await query.Handle(
      new GetBorderAssignments(f.Load.Id),
      default
    );
    Assert.Equal(a.Id, options.Response![0].DriverId);
    var onward = options.Response[1];
    Assert.Equal(b.Id, onward.DriverId);
    var draft = new BorderCrossing
    {
      Id = Guid.NewGuid(),
      SourceLegId = leg.Id,
      SourceStopId = onward.StopId,
      SourceRevision = 1,
      Crew =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Role = "driver",
          DriverId = onward.DriverId,
        },
      ],
      Equipment =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Kind = "truck",
          TruckId = truck.Id,
        },
      ],
    };
    var saved = await Save(f, protection)
      .Handle(new(new(Guid.NewGuid(), 0, draft)), default);
    Assert.True(saved.Success, string.Join(" ", saved.Errors ?? []));
    leg.Stops[1].DriverId = a.Id;
    leg.Revision++;
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    var read = await query.Handle(new GetBorderCrossing(draft.Id), default);
    Assert.Equal(b.Id, read.Response!.Crew[0].DriverId);
    Assert.Equal("Border driver", read.Response.Crew[0].DisplayName);
    var issues = await query.Handle(
      new CheckBorderCrossing(read.Response),
      default
    );
    Assert.Contains(issues.Response!, x => x.Field == "Assignment");
  }

  private static SaveBorderCrossingHandler Save(
    StopCompletionFixture f,
    IBorderDataProtection p,
    string role = "Admin"
  ) => new(f.Db, new Caller(), new Roles(role), f.Clock, p);

  private static BorderQueries Query(
    StopCompletionFixture f,
    IBorderDataProtection p,
    string role = "Admin"
  ) => new(f.Db, new Caller(), new Roles(role), p);

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
