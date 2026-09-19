using System.Net;
using Bunit;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchRefreshRetentionTests
{
  [Fact]
  public async Task DelayedBoardFailureAndPendingRecoveryKeepMountedCardsUntilAtomicReplacement()
  {
    using var fixture = new DispatchRefreshFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () =>
        Assert.Equal(2, component.FindAll(".arrival-estimate__ontime").Count)
    );
    var card = component.FindComponent<DispatchLoadCard>().Instance;
    var previous = component.FindComponent<DispatchLoadCard>().Markup;
    fixture.DeferBoard = true;
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(61))
    );
    var delayed = await fixture.NextRequest();
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(2))
    );
    component.FindComponent<DispatchLoadCard>().Render();
    Assert.Equal(previous, component.FindComponent<DispatchLoadCard>().Markup);
    delayed.SetResult(new(HttpStatusCode.ServiceUnavailable));
    component.WaitForAssertion(
      () =>
        Assert.True(
          component.FindComponent<DispatchLoadCard>().Instance.Refreshing
        )
    );
    Assert.Same(card, component.FindComponent<DispatchLoadCard>().Instance);
    Assert.Equal(previous, component.FindComponent<DispatchLoadCard>().Markup);

    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(10))
    );
    var recovering = await fixture.NextRequest();
    fixture.Load.Eta = fixture.Forecast() with
    {
      Stops = [],
      RouteUpdatePending = true,
    };
    recovering.SetResult(fixture.Board());
    component.WaitForAssertion(
      () =>
        Assert.True(
          component
            .FindComponent<DispatchLoadCard>()
            .Instance.Load.Eta!.RouteUpdatePending
        )
    );
    Assert.Same(card, component.FindComponent<DispatchLoadCard>().Instance);
    Assert.Equal(previous, component.FindComponent<DispatchLoadCard>().Markup);

    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(61))
    );
    var replacement = await fixture.NextRequest();
    fixture.Load.Eta = fixture.Forecast(5);
    replacement.SetResult(fixture.Board());
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".arrival-estimate__late").Count)
    );
    Assert.Empty(
      component.FindAll(
        ".arrival-estimate__ontime, .dispatch-load__eta-missing"
      )
    );
    Assert.Same(card, component.FindComponent<DispatchLoadCard>().Instance);
    Assert.DoesNotContain("Updating", component.Markup);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task NullForecastAfterItsDeadlineRetainsQuietlyButExplicitUnavailabilityDoesNot(
    bool unavailable
  )
  {
    using var fixture = new DispatchRefreshFixture();
    var original = fixture.Load.Eta!;
    var component = fixture.Render();
    component.WaitForAssertion(
      () =>
        Assert.Equal(2, component.FindAll(".arrival-estimate__ontime").Count)
    );
    var previous = component.FindComponent<DispatchLoadCard>().Markup;
    fixture.DeferBoard = true;
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(61))
    );
    var pending = await fixture.NextRequest();
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(2))
    );
    fixture.Load.Eta = unavailable
      ? original with
      {
        CalculatedAt = original.CalculatedAt.AddSeconds(-1),
        Stops = [],
        RouteUpdatePending = false,
        UnavailableReason = "Position unavailable",
      }
      : null;
    pending.SetResult(fixture.Board());
    component.WaitForAssertion(
      () =>
        Assert.False(
          component.FindComponent<DispatchLoadCard>().Instance.Refreshing
        )
    );

    if (unavailable)
    {
      component.WaitForAssertion(() =>
      {
        Assert.Empty(component.FindAll(".arrival-estimate__ontime"));
        Assert.False(
          component
            .FindComponent<DispatchLoadCard>()
            .Instance.Load.Eta!.RouteUpdatePending
        );
      });
      return;
    }
    Assert.Equal(previous, component.FindComponent<DispatchLoadCard>().Markup);
    var retained = component
      .FindComponent<DispatchLoadCard>()
      .Instance.Load.Eta!;
    Assert.True(retained.RouteUpdatePending);
    Assert.Equal(original.ValidUntil, retained.ValidUntil);
    Assert.False(original.RouteUpdatePending);
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(15))
    );
    component.FindComponent<DispatchLoadCard>().Render();
    Assert.Empty(component.FindAll(".arrival-estimate__ontime"));
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized)]
  [InlineData(HttpStatusCode.Forbidden)]
  public async Task AccessDeniedClearsThePreviousBoardInsteadOfRetainingProtectedInformation(
    HttpStatusCode status
  )
  {
    using var fixture = new DispatchRefreshFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Single(component.FindComponents<DispatchLoadCard>())
    );
    fixture.DeferBoard = true;
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(61))
    );
    (await fixture.NextRequest()).SetResult(new(status));
    component.WaitForAssertion(
      () => Assert.Empty(component.FindComponents<DispatchLoadCard>())
    );
    Assert.Single(component.FindAll("[role=alert]"));
  }

  [Fact]
  public async Task FailedChangedSearchDoesNotShowThePreviousBoardAsTheNewSearchResult()
  {
    using var fixture = new DispatchRefreshFixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Single(component.FindComponents<DispatchLoadCard>())
    );
    fixture.DeferBoard = true;
    var search = component
      .Find("#dispatch-search")
      .InputAsync(new ChangeEventArgs { Value = "another truck" });
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMilliseconds(300))
    );
    (await fixture.NextRequest()).SetResult(
      new(HttpStatusCode.ServiceUnavailable)
    );
    await search;
    component.WaitForAssertion(
      () => Assert.Empty(component.FindComponents<DispatchLoadCard>())
    );
    Assert.Single(component.FindAll("[role=alert]"));
  }
}
