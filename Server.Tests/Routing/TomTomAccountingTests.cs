using System.Net;
using Domain.Entities.Dispatch;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TomTomAccountingTests
{
  [Theory]
  [InlineData("{\"routes\":[{}]}")]
  [InlineData("{\"routes\":{}}")]
  [InlineData(
    "{\"routes\":[{\"summary\":{\"lengthInMeters\":\"wrong\",\"travelTimeInSeconds\":60}}]}"
  )]
  [InlineData("[]")]
  public async Task MalformedSuccessKeepsAccountingAndCooldown(string body)
  {
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent(body),
          }
        )
    );
    var failure = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    Assert.DoesNotContain(body, failure.Message);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    var saved = await fixture.Db.RoutingApiCalls.SingleAsync();
    Assert.Equal(failure.Message, saved.ErrorMessage);
    Assert.True(saved.ExpiresAt > DateTime.UtcNow);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync(42)
    );
    Assert.Equal(1, fixture.Calls);
    Assert.Equal(1, await fixture.Db.RoutingApiCalls.CountAsync());
  }

  [Fact]
  public async Task CancellationAfterDispatchPreservesReservationAndCancellationSemantics()
  {
    using var cancellation = new CancellationTokenSource();
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, ct) =>
      {
        cancellation.Cancel();
        return Task.FromCanceled<HttpResponseMessage>(ct);
      }
    );
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => fixture.CalculateAsync(ct: cancellation.Token)
    );
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    Assert.Equal(1, await fixture.Db.RoutingApiCalls.CountAsync());
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    Assert.Equal(1, fixture.Calls);
  }

  [Fact]
  public async Task ReservationIsCommittedBeforeExternalIoAndSurvivesAnUnfinalizedAttempt()
  {
    TomTomProviderFixture? current = null;
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      async (_, ct) =>
      {
        Assert.Null(current!.Db.Database.CurrentTransaction);
        await using var observer = current.CreateObserver();
        Assert.Equal(1, await observer.RoutingApiCalls.CountAsync(ct));
        throw new SimulatedProcessInterruption();
      }
    );
    current = fixture;
    await Assert.ThrowsAsync<SimulatedProcessInterruption>(
      () => fixture.CalculateAsync()
    );
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    await using var recovered = fixture.CreateObserver();
    var attempt = await recovered.RoutingApiCalls.SingleAsync();
    Assert.Null(attempt.ResultJson);
    Assert.True(attempt.ExpiresAt > DateTime.UtcNow);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    Assert.Equal(1, fixture.Calls);
  }

  private sealed class SimulatedProcessInterruption : Exception;
}
