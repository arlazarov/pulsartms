using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class ExecutionStopStorageTests
{
  [Fact]
  public async Task TypedStoragePreservesAcceptedFactsAndTrackedReplacements()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "stop-storage" };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      Trip = new() { Id = Guid.NewGuid() },
    };
    var first = Facts();
    var second = Facts();
    ExecutionStopRows.Replace(leg, [first, second]);
    db.Trucks.Add(truck);
    db.ExecutionLegs.Add(leg);
    await db.SaveChangesAsync();
    first.TruckId = truck.Id;
    second.TruckId = truck.Id;
    first.Sequence = 1;
    second.Sequence = 2;
    var expected = ExecutionSnapshots.Write([first, second]);
    db.ChangeTracker.Clear();

    var stored = await db.ExecutionLegs.SingleAsync(x => x.Id == leg.Id);

    Assert.Equal(
      expected,
      ExecutionSnapshots.Write(ExecutionStopRows.Read(stored))
    );
    var originalRow = stored.Stops.Single(x => x.Id == first.Id);
    first.Notes = "Edited after storage";
    ExecutionStopRows.Replace(stored, [second, first]);
    Assert.Same(originalRow, stored.Stops.Single(x => x.Id == first.Id));
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    stored = await db.ExecutionLegs.SingleAsync(x => x.Id == leg.Id);
    Assert.Equal(
      new[] { second.Id, first.Id },
      ExecutionStopRows.Read(stored).Select(x => x.Id)
    );
    Assert.Equal(
      "Edited after storage",
      ExecutionStopRows.Read(stored)[1].Notes
    );

    ExecutionStopRows.Replace(stored, [second]);
    await db.SaveChangesAsync();
    Assert.Equal(1, await db.ExecutionLegStops.CountAsync());

    var continuation = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = stored.TripId,
      TruckId = truck.Id,
      Stops = ExecutionStopRows.Capture([second]),
    };
    db.ExecutionLegs.Add(continuation);
    await db.SaveChangesAsync();
    Assert.Equal(2, await db.ExecutionLegStops.CountAsync());
    db.ExecutionLegs.Remove(stored);
    await db.SaveChangesAsync();
    Assert.Equal(
      continuation.Id,
      (await db.ExecutionLegStops.SingleAsync()).ExecutionLegId
    );
  }

  private static DispatchStop Facts()
  {
    var at = new DateTime(2026, 9, 15, 12, 13, 14, DateTimeKind.Utc);
    return new()
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      Sequence = 2,
      Job = "Delivery",
      ManualAction = "Drop",
      StateAfter = "Bobtail",
      OperationRevision = 9,
      OperationRecordedAt = at,
      OperationRecordedBy = Guid.NewGuid(),
      Name = "Repeated destination",
      Address = "10 Test Road",
      City = "Toronto",
      Province = "ON",
      Country = "CA",
      ZipCode = "M1A 1A1",
      Latitude = 43.600000m,
      Longitude = -79.500000m,
      SourceAddressJson = "{\"address\":\"Source location\"}",
      AddressVerifiedAt = at,
      AddressRetryAfter = at.AddDays(1),
      ScheduledDate = new(2026, 9, 16),
      ScheduledTime = new(1, 2, 3),
      ScheduledDate2 = new(2026, 9, 17),
      ScheduledTime2 = new(2, 3, 4),
      IsWindow = true,
      AppointmentTimeZoneId = "America/Toronto",
      DriverName = "Primary display",
      CoDriverName = "Team display",
      TrailerNumber = "Trailer display",
      CarrierName = "Carrier display",
      StopNo = "Stop reference",
      Notes = "Accepted notes",
      Commodity = "Cargo description",
      Weight = 45000.10m,
      WeightUnit = "lb",
      Pieces = 250.00m,
      Pallets = 30.0m,
      Temperature = "-18",
      TemperatureUnit = "C",
      ArrivedAt = at,
      PickedUpAt = at.AddMinutes(1),
      DeliveredAt = at.AddMinutes(2),
      DepartedAt = at.AddMinutes(3),
      ManualCompletedAt = null,
      CompletionOverride = false,
      ExecutionCompleted = true,
      ManualCompletionRecordedAt = at.AddMinutes(4),
      ManualCompletionRevision = 7,
      ManualCompletedBy = Guid.NewGuid(),
      ManualCompletedByName = "Historical actor",
    };
  }
}
