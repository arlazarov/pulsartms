using Application.Features.Routing.Services.FuelPlanning;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Domain.Models.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelDeliveryChannelTests
{
  [Fact]
  public async Task AReadyChannelNeedsNeitherPhoneNorWhatsAppReplyWindow()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "driver",
      Name = "Driver",
    };
    f.Db.Drivers.Add(driver);
    (await f.Db.Trucks.FindAsync(f.Truck.Id))!.DriverId = driver.Id;
    (await f.Db.Dispatches.FindAsync(f.Load.Id))!.DriverId = driver.Id;
    await f.Db.SaveChangesAsync();
    var delivery = new InAppDelivery();
    var channel = new FuelIssueChannel(
      f.Db,
      f.Planning.PlanningInputs,
      delivery,
      TimeProvider.System
    );
    var recipient = await channel.RecipientAsync(f.Truck.Id, default);
    Assert.Equal(driver.Id, delivery.AskedFor);
    Assert.Equal(FuelIssueChannelStates.Ready, recipient.State);
    Assert.Equal("in-app", channel.Name);
    Assert.Null(recipient.AvailableUntil);
  }

  private sealed class InAppDelivery : IDriverTextDelivery
  {
    public Guid? AskedFor;
    public string Channel => "in-app";

    public Task<DriverTextRecipient> RecipientAsync(
      Guid? driverId,
      CancellationToken ct
    )
    {
      AskedFor = driverId;
      return Task.FromResult(
        new DriverTextRecipient(
          driverId,
          "Driver",
          "in-app-recipient",
          DriverTextAvailability.Ready,
          null
        )
      );
    }

    public Task<DriverTextOutcome> SendAsync(
      DriverMessage message,
      bool sendAgain,
      Func<CancellationToken, Task<bool>> stillWanted,
      CancellationToken ct
    ) => throw new InvalidOperationException("This is a read-only preview.");
  }
}
