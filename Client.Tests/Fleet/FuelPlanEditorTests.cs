using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared;
using Client.Shared.Fuel;
using Client.Shared.Fuel.FuelPlanEditor;
using Client.Shared.Fuel.FuelRecalculateButton;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

[Trait("Category", "Fuel")]
[Trait("Kind", "Component")]
public sealed class FuelPlanEditorTests
{
  [Fact]
  public async Task SelectingAnotherStopBlocksQuantityUntilItsServerChoicesArrive()
  {
    using var fixture = new Fixture();
    fixture.Initial = fixture.Initial with
    {
      QuantityChoices = new(0, [new(180, true, [], 0, 0, 0, 0, 0, [])]),
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    fixture.DeferPreview = true;
    var selecting = component.InvokeAsync(
      () =>
        component
          .FindAll(".fuel-plan-editor__stop-select")[1]
          .ClickAsync(new MouseEventArgs())
    );
    var pending = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    component.WaitForAssertion(
      () =>
        Assert.True(
          component.Find("input[type='range']").HasAttribute("disabled")
        )
    );
    Assert.True(
      component.Find("input[type='checkbox']").HasAttribute("disabled")
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='range']")
          .InputAsync(new ChangeEventArgs { Value = "35" })
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    fixture.Clock.Advance(TimeSpan.FromSeconds(1));
    Assert.Equal(2, fixture.Requests.Count);
    Assert.Equal(1, pending.Body.QuantityStopIndex);
    pending.Reply(
      fixture.Preview(pending.Body) with
      {
        QuantityChoices = new(1, [new(100, true, [], 0, 0, 0, 0, 0, [])]),
      }
    );
    await selecting;
    component.WaitForAssertion(
      () =>
        Assert.False(
          component.Find("input[type='range']").HasAttribute("disabled")
        )
    );
    Assert.True(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
  }

  [Fact]
  public async Task PreparedSliderRedistributesServerValuesWithoutAnyPreviewRequest()
  {
    using var fixture = new Fixture();
    var initial = fixture.Initial;
    initial.Stops[0] = initial.Stops[0] with
    {
      BuyGallons = 25,
      FillToTarget = false,
    };
    initial.Stops[1] = initial.Stops[1] with
    {
      BuyGallons = 100,
      FillToTarget = false,
    };
    fixture.Initial = initial with
    {
      QuantityChoices = new(
        0,
        [
          new(
            25,
            false,
            [
              new(13, 25, 38, false, 177.3, 12.34),
              new(43, 100, 143, false, 98.4, 56.78),
            ],
            23,
            125,
            69.12,
            60,
            30,
            []
          ),
          new(
            35,
            false,
            [
              new(13, 35, 48, false, 177.3, 45.67),
              new(53, 90, 143, false, 88.4, 89.01),
            ],
            23,
            125,
            134.68,
            100,
            30,
            []
          ),
          new(
            180,
            true,
            [],
            0,
            0,
            0,
            0,
            0,
            ["The fill limit would be exceeded."]
          ),
        ]
      ),
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='range']")
          .InputAsync(new ChangeEventArgs { Value = "35" })
    );
    fixture.Clock.Advance(TimeSpan.FromSeconds(1));
    Assert.Single(fixture.Requests);
    Assert.Contains(
      "35 US gal",
      component.FindAll(".fuel-plan-editor__stop")[0].TextContent
    );
    Assert.Contains(
      "90 US gal",
      component.FindAll(".fuel-plan-editor__stop")[1].TextContent
    );
    Assert.Contains(
      "$134.68",
      component.Find(".fuel-plan-editor__totals").TextContent
    );
    Assert.Contains(
      "$45.67",
      component.Find(".fuel-plan-editor__cost").TextContent
    );
    Assert.False(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("button.fuel-plan-editor__save")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(() => Assert.NotNull(fixture.Saved));
    Assert.Equal(2, fixture.Requests.Count);
    var saved = fixture.Requests[^1];
    Assert.Equal("PUT", saved.Method);
    Assert.Equal(fixture.OriginalToken, saved.Body.ExpectedCalculatedAt);
    Assert.Equal(
      new[] { 35d, 90 },
      saved.Body.Stops!.Select(stop => stop.BuyGallons)
    );
  }

  [Fact]
  public async Task InvalidPreparedQuantityBlocksSaveWithoutAskingTheServerAgain()
  {
    using var fixture = new Fixture();
    fixture.Initial = fixture.Initial with
    {
      QuantityChoices = new(
        0,
        [
          new(
            35,
            false,
            [new(13, 35, 48, false, 177.3, 1), new(0, 25, 25, false, 200, 2)],
            0,
            60,
            3,
            3,
            0,
            ["The next stop cannot be reached with the required reserve."]
          ),
        ]
      ),
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='range']")
          .InputAsync(new ChangeEventArgs { Value = "35" })
    );
    Assert.Single(fixture.Requests);
    Assert.True(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
    Assert.NotEmpty(component.FindAll("[role='alert']"));
  }

  [Theory]
  [InlineData(35, 145, false)]
  [InlineData(160, 20, true)]
  public async Task PreparedEarlierChangeKeepsTheLaterFullTargetOnDisplayAndSave(
    double firstBuy,
    double secondBuy,
    bool firstFull
  )
  {
    using var fixture = new Fixture();
    var initial = fixture.Initial;
    initial.Stops[0] = initial.Stops[0] with
    {
      BuyGallons = 25,
      FillToTarget = false,
      PurchaseLimitGallons = 160,
    };
    initial.Stops[1] = initial.Stops[1] with
    {
      BuyGallons = 155,
      FillToTarget = true,
      PurchaseLimitGallons = 155,
    };
    fixture.Initial = initial with
    {
      TankGallons = 250,
      FillLimitGallons = 250,
      QuantityChoices = new(
        0,
        [
          new(
            35,
            false,
            [
              new(90, 35, 125, false, 160, 140),
              new(105, 145, 250, true, 145, 725),
            ],
            160,
            180,
            865,
            865,
            0,
            []
          ),
          new(
            160,
            true,
            [
              new(90, 160, 250, true, 160, 640),
              new(230, 20, 250, true, 20, 100),
            ],
            160,
            180,
            740,
            740,
            0,
            []
          ),
        ]
      ),
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );

    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='range']")
          .InputAsync(
            new ChangeEventArgs
            {
              Value = firstBuy.ToString(CultureInfo.InvariantCulture),
            }
          )
    );

    fixture.Clock.Advance(TimeSpan.FromSeconds(1));
    Assert.Single(fixture.Requests);
    Assert.Contains(
      "Full tank",
      component.FindAll(".fuel-plan-editor__stop")[1].TextContent
    );
    Assert.Empty(component.FindAll("[role='alert']"));
    Assert.False(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("button.fuel-plan-editor__save")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(() => Assert.NotNull(fixture.Saved));

    var submitted = fixture.Requests[^1];
    Assert.Equal("PUT", submitted.Method);
    Assert.Equal(2, fixture.Requests.Count);
    Assert.Equal(firstBuy, submitted.Body.Stops![0].BuyGallons);
    Assert.Equal(firstFull, submitted.Body.Stops[0].FillToTarget);
    Assert.Equal(secondBuy, submitted.Body.Stops[1].BuyGallons);
    Assert.True(submitted.Body.Stops[1].FillToTarget);
  }

  [Fact]
  public async Task MobileViewsPreserveTheDraftAndSelectingAStationReturnsToDetails()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    var requests = fixture.Requests.Count;
    var quantity = component.Find("input[type='range']").GetAttribute("value");
    await component.InvokeAsync(
      () =>
        component
          .FindAll(".fuel-plan-editor__views button")[2]
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Contains("is-map", component.Find(".fuel-plan-editor").ClassList);
    Assert.Equal(
      "true",
      component
        .FindAll(".fuel-plan-editor__views button")[2]
        .GetAttribute("aria-pressed")
    );
    Assert.Equal(
      quantity,
      component.Find("input[type='range']").GetAttribute("value")
    );
    await component.InvokeAsync(
      () =>
        component
          .FindAll(".fuel-plan-editor__views button")[0]
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Contains("is-route", component.Find(".fuel-plan-editor").ClassList);
    await component.InvokeAsync(
      () =>
        component
          .FindAll(".fuel-plan-editor__stop-select")[1]
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Contains("is-fuel", component.Find(".fuel-plan-editor").ClassList);
    Assert.Contains(
      "Second station",
      component.Find(".fuel-plan-editor__station-heading").TextContent
    );
    Assert.Equal(requests, fixture.Requests.Count);
    Assert.Equal(0, fixture.Closed);
    Assert.Null(fixture.Saved);
  }

  [Fact]
  public void OpeningLoadsRemainingPlanWithoutSavingAndShowsServerAmounts()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    Assert.Single(fixture.Requests);
    Assert.Null(fixture.Requests[0].Body.Stops);
    Assert.Null(fixture.Requests[0].Body.ExpectedCalculatedAt);
    Assert.Contains("$87.65 USD", component.Markup);
    Assert.Contains("13 US gal", component.Markup);
    Assert.Contains("43 US gal", component.Markup);
    Assert.False(
      component.Find("input[type='range']").HasAttribute("disabled")
    );
    Assert.Equal(
      "5",
      component.Find("input[type='range']").GetAttribute("step")
    );
    Assert.Equal(
      "25",
      component.Find("input[type='range']").GetAttribute("min")
    );
    Assert.Equal(
      "180",
      component.Find("input[type='range']").GetAttribute("value")
    );
    Assert.Equal(
      "Full tank",
      component.Find("input[type='range']").GetAttribute("aria-valuetext")
    );
    Assert.Contains(
      "$33.21 USD",
      component.Find(".fuel-plan-editor__cost").TextContent
    );
    Assert.Equal(
      "1.739 CAD / L",
      component.Find(".fuel-plan-editor__price strong").TextContent
    );
    Assert.Single(
      component.FindAll(".fuel-plan-editor__timeline .fuel-plan-editor__stops")
    );
    Assert.Empty(
      component.FindAll(".fuel-plan-editor__timeline .fuel-plan-editor__totals")
    );
    Assert.Single(
      component.FindAll(".fuel-plan-editor__content .fuel-plan-editor__totals")
    );
    Assert.Equal(
      3,
      component
        .FindAll(
          ".fuel-plan-editor > .fuel-plan-editor__header, .fuel-plan-editor > .fuel-plan-editor__timeline, .fuel-plan-editor > .fuel-plan-editor__footer"
        )
        .Count
    );
    Assert.Empty(component.FindAll(".fuel-plan-editor__map-slot, #fleet-map"));
    Assert.Single(
      component.FindAll(
        ".fuel-plan-editor__content .fuel-plan-editor__station-summary .fleet-fuel-visit__levels"
      )
    );
    Assert.Equal(3, component.FindAll(".fuel-plan-editor__anchor").Count);
    Assert.Empty(component.FindAll("select"));
    Assert.True(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
  }

  [Fact]
  public async Task ConciseAddressErrorStillBlocksSavingAndPreservesOtherFailures()
  {
    const string reason =
      "The stop needs an unambiguous street-level address; no city-center fallback was used.";
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    fixture.PreviewErrors.Add(reason);
    fixture.PreviewErrors.Add("Access denied.");
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    component.WaitForAssertion(() =>
    {
      Assert.Contains(
        "Check the stop address.",
        component.Find("[role='alert']").TextContent
      );
      Assert.Contains(
        "Access denied.",
        component.Find("[role='alert']").TextContent
      );
      Assert.DoesNotContain("city-center fallback", component.Markup);
      Assert.True(
        component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
      );
      Assert.Contains(reason, fixture.PreviewErrors);
      Assert.DoesNotContain(
        fixture.Requests,
        request => request.Method == "PUT"
      );
    });
  }

  [Fact]
  public async Task OpeningFocusesOnceWithoutScrollingAndEscapeCancels()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(() =>
    {
      var focus = Assert.Single(
        fixture.Context.JSInterop.Invocations,
        call => call.Identifier == "Blazor._internal.domWrapper.focus"
      );
      Assert.Equal("Blazor._internal.domWrapper.focus", focus.Identifier);
      Assert.Equal(true, focus.Arguments[1]);
    });
    component.Render();
    await component.InvokeAsync(() =>
    {
      Assert.Single(
        fixture.Context.JSInterop.Invocations,
        call => call.Identifier == "Blazor._internal.domWrapper.focus"
      );
      Assert.Single(
        fixture.Context.JSInterop.Invocations,
        call => call.Identifier == "import"
      );
    });
    await component.InvokeAsync(
      () =>
        component
          .Find(".fuel-plan-editor")
          .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" })
    );
    Assert.Equal(1, fixture.Closed);
    Assert.Null(fixture.Saved);
  }

  [Fact]
  public async Task LegacyPreviewFallbackUsesFiveGallonStepsWithoutAdvancingTheConcurrencyToken()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    component.WaitForAssertion(() => Assert.Equal(2, fixture.Requests.Count));
    fixture.PreviewToken = fixture.OriginalToken.AddMinutes(5);
    var change = component.InvokeAsync(
      () =>
        component
          .Find("input[type='range']")
          .InputAsync(new ChangeEventArgs { Value = "30" })
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "true",
          component.Find(".fleet-fuel-visit__levels").GetAttribute("aria-busy")
        )
    );
    fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
    await change.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(30, fixture.Requests[^1].Body.Stops![0].BuyGallons);
    Assert.False(fixture.Requests[^1].Body.Stops![0].FillToTarget);
    await component.InvokeAsync(
      () =>
        component
          .Find("button.fuel-plan-editor__save")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(() => Assert.NotNull(fixture.Saved));
    Assert.Equal("PUT", fixture.Requests[^1].Method);
    Assert.All(
      fixture.Requests.Skip(1),
      request =>
        Assert.Equal(fixture.OriginalToken, request.Body.ExpectedCalculatedAt)
    );
  }

  [Fact]
  public async Task RapidSliderChangesCancelOlderPreviewAndPublishOnlyNewestResult()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    component.WaitForAssertion(
      () =>
        Assert.False(
          component
            .Find("button.fuel-plan-editor__save")
            .HasAttribute("disabled")
        )
    );
    fixture.DeferPreview = true;
    var firstChange = component.InvokeAsync(
      () =>
        component
          .Find("input[type='range']")
          .InputAsync(new ChangeEventArgs { Value = "25" })
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "25 US gal",
          component.Find("label[for='fuel-edit-quantity']").TextContent
        )
    );
    fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
    var first = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    var secondChange = component.InvokeAsync(
      () =>
        component
          .Find("input[type='range']")
          .InputAsync(new ChangeEventArgs { Value = "40" })
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "40 US gal",
          component.Find("label[for='fuel-edit-quantity']").TextContent
        )
    );
    fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
    var second = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    Assert.True(first.Token.IsCancellationRequested);
    Assert.True(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
    Assert.DoesNotContain("13 US gal", component.Markup);
    second.Reply(fixture.Preview(second.Body, arrival: 77));
    await secondChange;
    component.WaitForAssertion(
      () => Assert.Contains("77 US gal", component.Markup)
    );
    first.Reply(fixture.Preview(first.Body, arrival: 11));
    await firstChange;
    Assert.Contains("77 US gal", component.Markup);
    Assert.DoesNotContain("11 US gal", component.Markup);
    Assert.False(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
  }

  [Fact]
  public async Task DraggingFuelStopPreservesWholeDraftAndPinsOnlyMovedOccurrence()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    var rows = component.FindAll(".fuel-plan-editor__stop");
    var firstKey = rows[0].GetAttribute("data-reorder-key")!;
    var secondKey = rows[1].GetAttribute("data-reorder-key")!;
    await component.InvokeAsync(
      () => component.Instance.OnFuelStopMoved(firstKey, secondKey, true)
    );
    component.WaitForAssertion(() => Assert.Equal(2, fixture.Requests.Count));
    var submitted = fixture.Requests[^1].Body.Stops!;
    Assert.Equal(
      new[] { fixture.StationB, fixture.StationA },
      submitted.Select(stop => stop.StationId)
    );
    Assert.Equal(fixture.BeforeB, submitted[0].BeforeStopId);
    Assert.Equal(fixture.BeforeB, submitted[1].BeforeStopId);
    Assert.True(submitted[1].FillToTarget);
    Assert.Equal(70, submitted[0].BuyGallons);
    Assert.Equal(
      new[] { secondKey, firstKey },
      component
        .FindAll(".fuel-plan-editor__stop")
        .Select(row => row.GetAttribute("data-reorder-key"))
    );
    await component.InvokeAsync(
      () =>
        component.Render(parameters =>
          parameters.Add(
            x => x.Station,
            new FuelEditorStation(
              fixture.StationC,
              "New station",
              null,
              false,
              1
            )
          )
        )
    );
    component.WaitForAssertion(
      () => Assert.Equal(3, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    Assert.Equal(
      new[] { fixture.StationB, fixture.StationA, fixture.StationC },
      fixture.Requests[^1].Body.Stops!.Select(stop => stop.StationId)
    );
    Assert.True(fixture.Requests[^1].Body.Stops![2].FillToTarget);
    Assert.Equal(0, fixture.Requests[^1].Body.Stops![2].BuyGallons);
  }

  [Fact]
  public async Task ClickingExistingStationSelectsWithoutDuplicateWhileAnotherVisitAdds()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component.Render(p =>
          p.Add(
            x => x.Station,
            new FuelEditorStation(
              fixture.StationB,
              "Second station",
              fixture.BeforeB,
              false,
              1
            )
          )
        )
    );
    Assert.Contains(
      "Second station",
      component.Find(".fuel-plan-editor__stop.is-selected").TextContent
    );
    Assert.Equal(fixture.StationB, fixture.Selections[^1]?.StationId);
    Assert.Single(fixture.Requests);
    await component.InvokeAsync(
      () =>
        component.Render(p =>
          p.Add(
            x => x.Station,
            new FuelEditorStation(
              fixture.StationB,
              "Second station",
              null,
              true,
              2
            )
          )
        )
    );
    Assert.Equal(3, component.FindAll(".fuel-plan-editor__stop").Count);
    Assert.Equal(
      2,
      fixture
        .Requests[^1]
        .Body.Stops!.Count(stop => stop.StationId == fixture.StationB)
    );
  }

  [Fact]
  public async Task DraggingAcrossFixedPickupPinsTheFollowingLegAndRejectsAfterFinalDelivery()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () =>
        Assert.Equal(3, component.FindAll(".fuel-plan-editor__anchor").Count)
    );
    var source = component
      .Find(".fuel-plan-editor__stop")
      .GetAttribute("data-reorder-key")!;
    Assert.Empty(
      component.FindAll(".fuel-plan-editor__anchor [data-reorder-handle]")
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStopMoved(
          source,
          $"stop:{fixture.BeforeB}",
          true
        )
    );
    var submitted = fixture.Requests[^1].Body.Stops!;
    Assert.Equal(
      new[] { fixture.StationB, fixture.StationA },
      submitted.Select(stop => stop.StationId)
    );
    Assert.Equal(fixture.BeforeC, submitted[1].BeforeStopId);
    Assert.Contains(
      "After Pickup",
      component.Find(".fuel-plan-editor__position").TextContent
    );
    var count = fixture.Requests.Count;
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStopMoved(
          source,
          $"stop:{fixture.BeforeC}",
          true
        )
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStopMoved(
          $"stop:{fixture.BeforeA}",
          source,
          false
        )
    );
    Assert.Equal(count, fixture.Requests.Count);
    Assert.Equal(
      "false",
      component
        .FindAll(".fuel-plan-editor__anchor")[^1]
        .GetAttribute("data-reorder-after")
    );
  }

  [Fact]
  public async Task KeyboardMovesThroughFixedAnchorsWithoutChangingStationKeysOrLosingSelection()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    var source = component
      .Find(".fuel-plan-editor__stop")
      .GetAttribute("data-reorder-key")!;
    var selector = $"[data-reorder-key='{source}'] [data-reorder-handle]";
    await component.InvokeAsync(
      () =>
        component
          .Find(selector)
          .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" })
    );
    Assert.Equal(
      fixture.BeforeB,
      fixture.Requests[^1].Body.Stops![0].BeforeStopId
    );
    Assert.Equal(
      fixture.StationA,
      fixture.Requests[^1].Body.Stops![0].StationId
    );
    Assert.Contains(
      "After Delivery",
      component.Find(".fuel-plan-editor__position").TextContent
    );
    await component.InvokeAsync(
      () =>
        component
          .Find(selector)
          .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" })
    );
    Assert.Equal(
      fixture.StationA,
      fixture.Requests[^1].Body.Stops![1].StationId
    );
    await component.InvokeAsync(
      () =>
        component
          .Find(selector)
          .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" })
    );
    Assert.Equal(
      fixture.BeforeC,
      fixture.Requests[^1].Body.Stops![1].BeforeStopId
    );
    var count = fixture.Requests.Count;
    await component.InvokeAsync(
      () =>
        component
          .Find(selector)
          .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" })
    );
    Assert.Equal(count, fixture.Requests.Count);
    Assert.Equal(
      source,
      component
        .Find(".fuel-plan-editor__stop.is-selected")
        .GetAttribute("data-reorder-key")
    );
    Assert.Contains(
      "After Pickup",
      component.Find("[aria-live='polite']").TextContent
    );
    Assert.Single(fixture.Selections);
  }

  [Fact]
  public async Task SliderRightEdgeAutomaticallyTogglesFullTankWithoutBlockingTheSlider()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    foreach (
      var (value, full) in new[] { (170, false), (180, true), (30, false) }
    )
    {
      var change = component.InvokeAsync(
        () =>
          component
            .Find("input[type='range']")
            .InputAsync(new ChangeEventArgs { Value = value.ToString() })
      );
      component.WaitForAssertion(
        () =>
          Assert.Equal(
            "true",
            component
              .Find(".fleet-fuel-visit__levels")
              .GetAttribute("aria-busy")
          )
      );
      fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
      await change.WaitAsync(TimeSpan.FromSeconds(5));
      var edit = fixture.Requests[^1].Body.Stops![0];
      Assert.Equal(full, edit.FillToTarget);
      Assert.Equal(full ? 0 : value, edit.BuyGallons);
      Assert.Equal(
        full,
        component.Find("input[type='checkbox']").HasAttribute("checked")
      );
      Assert.False(
        component.Find("input[type='range']").HasAttribute("disabled")
      );
    }
    Assert.Single(fixture.Selections);
  }

  [Fact]
  public async Task PurchaseHeadroomStaysWithItsDraftRowDuringAReorderAndUpdatesFromTheServer()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "180",
          component.Find("input[type='range']").GetAttribute("max")
        )
    );
    var source = component
      .FindAll(".fuel-plan-editor__stop")[0]
      .GetAttribute("data-reorder-key")!;
    var target = component
      .FindAll(".fuel-plan-editor__stop")[1]
      .GetAttribute("data-reorder-key")!;
    fixture.DeferPreview = true;
    var move = component.InvokeAsync(
      () => component.Instance.OnFuelStopMoved(source, target, true)
    );
    var pending = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    await component.InvokeAsync(() => component.Render());
    Assert.Contains(
      "First station",
      component.Find(".fuel-plan-editor__stop.is-selected").TextContent
    );
    Assert.Equal(
      "180",
      component.Find("input[type='range']").GetAttribute("max")
    );
    var next = fixture.Preview(pending.Body);
    next = next with
    {
      Stops = next
        .Stops.Select(edit =>
          edit.StationId == fixture.StationA
            ? edit with
            {
              PurchaseLimitGallons = 67.2,
            }
            : edit
        )
        .ToList(),
    };
    pending.Reply(next);
    await move;
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "70",
          component.Find("input[type='range']").GetAttribute("max")
        )
    );
    await component.InvokeAsync(
      () =>
        component
          .FindAll(".fuel-plan-editor__stop-select")[0]
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Equal(
      "100",
      component.Find("input[type='range']").GetAttribute("max")
    );
    await component.InvokeAsync(
      () =>
        component
          .FindAll(".fuel-plan-editor__stop-select")[1]
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Equal(
      "70",
      component.Find("input[type='range']").GetAttribute("max")
    );
  }

  [Fact]
  public async Task UnresolvedHeadroomDisablesNumericPurchaseWithoutReusingTheOldLimit()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "180",
          component.Find("input[type='range']").GetAttribute("max")
        )
    );
    fixture.DeferPreview = true;
    var change = component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    var pending = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    Assert.False(
      component.Find("input[type='range']").HasAttribute("disabled")
    );
    var unresolved = fixture.Preview(pending.Body);
    unresolved = unresolved with
    {
      ValuesAvailable = false,
      Errors = ["Choose another route position."],
      Stops = unresolved
        .Stops.Select(edit => edit with { PurchaseLimitGallons = null })
        .ToList(),
    };
    pending.Reply(unresolved);
    await change;
    component.WaitForAssertion(
      () =>
        Assert.True(
          component.Find("input[type='range']").HasAttribute("disabled")
        )
    );
    Assert.Equal(
      "180",
      component.Find("input[type='range']").GetAttribute("max")
    );
    Assert.True(
      component.Find("input[type='checkbox']").HasAttribute("disabled")
    );
    Assert.False(
      component
        .Find("[aria-label='Remove selected fuel stop']")
        .HasAttribute("disabled")
    );
    Assert.True(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
    var count = fixture.Requests.Count;
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='range']")
          .InputAsync(new ChangeEventArgs { Value = "170" })
    );
    Assert.Equal(count, fixture.Requests.Count);
  }

  [Fact]
  public async Task SelectingAStationPublishesMapFocusButQuantityChangesDoNotRefocus()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(() =>
    {
      Assert.Single(fixture.Selections);
      Assert.Equal(
        2,
        component.FindAll(".fuel-plan-editor__stop-select").Count
      );
    });
    await component.InvokeAsync(
      () =>
        component
          .FindAll(".fuel-plan-editor__stop-select")[1]
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Equal(
      new[] { fixture.StationA, fixture.StationB },
      fixture.Selections.Select(stop => stop!.StationId)
    );
    Assert.Single(fixture.Requests);
    Assert.Contains(
      "$44.32 USD",
      component.Find(".fuel-plan-editor__cost").TextContent
    );
    fixture.DeferPreview = true;
    var quantity = component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = true })
    );
    var pending = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(2, fixture.Selections.Count);
    pending.Reply(fixture.Preview(pending.Body));
    await quantity;
    component.WaitForAssertion(() =>
    {
      Assert.Equal(
        "false",
        component.Find(".fleet-fuel-visit__levels").GetAttribute("aria-busy")
      );
      Assert.False(
        component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
      );
      Assert.True(
        component.Find("input[type='checkbox']").HasAttribute("checked")
      );
    });
    Assert.Equal(2, fixture.Selections.Count);
    await component.InvokeAsync(
      () =>
        component
          .FindAll(".fuel-plan-editor__stop-select")[1]
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Equal(2, fixture.Selections.Count);
  }

  [Fact]
  public async Task DisposingReleasesReorderHandlersAndRejectsLateMoveCallbacks()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    var source = component
      .Find(".fuel-plan-editor__stop")
      .GetAttribute("data-reorder-key")!;
    await component.InvokeAsync(
      () => component.Instance.DisposeAsync().AsTask()
    );
    Assert.Single(
      fixture.Reorder.Invocations,
      call => call.Identifier == "dispose"
    );
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStopMoved(
          source,
          $"stop:{fixture.BeforeB}",
          true
        )
    );
    Assert.Single(fixture.Requests);
  }

  [Fact]
  public async Task SavingDisablesAllDraftMutationAndRejectsLateDragCallbacks()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    component.WaitForAssertion(
      () =>
        Assert.False(
          component
            .Find("button.fuel-plan-editor__save")
            .HasAttribute("disabled")
        )
    );
    fixture.DeferSave = true;
    var save = component.InvokeAsync(
      () =>
        component
          .Find("button.fuel-plan-editor__save")
          .ClickAsync(new MouseEventArgs())
    );
    var pending = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    component.WaitForAssertion(
      () =>
        Assert.All(
          component.FindAll(
            "[data-reorder-handle], .fuel-plan-editor__stop-select, input"
          ),
          control => Assert.True(control.HasAttribute("disabled"))
        )
    );
    var count = fixture.Requests.Count;
    var source = component
      .Find(".fuel-plan-editor__stop")
      .GetAttribute("data-reorder-key")!;
    await component.InvokeAsync(
      () =>
        component.Instance.OnFuelStopMoved(
          source,
          $"stop:{fixture.BeforeB}",
          true
        )
    );
    Assert.Equal(count, fixture.Requests.Count);
    pending.Reply(fixture.Preview(pending.Body));
    await save;
    Assert.NotNull(fixture.Saved);
    Assert.Equal(fixture.BeforeA, fixture.Saved.Stops[0].BeforeStopId);
  }

  [Fact]
  public async Task RemoveAndCancelNeverSaveOrMutateTheLoadedPlan()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("[aria-label='Remove selected fuel stop']")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(
      () => Assert.Single(component.FindAll(".fuel-plan-editor__stop"))
    );
    await component.InvokeAsync(
      () =>
        component
          .FindAll("button")
          .Single(button => button.TextContent == "Cancel")
          .ClickAsync(new MouseEventArgs())
    );
    Assert.Equal(1, fixture.Closed);
    Assert.Null(fixture.Saved);
    Assert.All(
      fixture.Requests,
      request => Assert.Equal("POST", request.Method)
    );
    Assert.Equal(2, fixture.Initial.Stops.Count);
  }

  [Fact]
  public async Task InvalidServerPreviewDisplaysComputedLevelsAndCanonicalOccurrenceButCannotSave()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    fixture.PreviewErrors = ["Not enough fuel to reach this stop."];
    await component.InvokeAsync(
      () =>
        component.Render(p =>
          p.Add(
            x => x.Station,
            new FuelEditorStation(
              fixture.StationC,
              "New station",
              null,
              false,
              1
            )
          )
        )
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "Not enough fuel",
          component.Find("[role='alert']").TextContent
        )
    );
    Assert.Contains("13 US gal", component.Markup);
    Assert.Contains("43 US gal", component.Markup);
    Assert.True(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
    fixture.PreviewErrors = [];
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    Assert.Equal(
      fixture.BeforeA,
      fixture
        .Requests[^1]
        .Body.Stops!.Single(stop => stop.StationId == fixture.StationC)
        .BeforeStopId
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CloseOrDisposeRejectsPendingPreview(bool dispose)
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    fixture.DeferPreview = true;
    var editing = component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    var pending = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    if (dispose)
      await component.InvokeAsync(component.Instance.Dispose);
    else
      await component.InvokeAsync(
        () =>
          component
            .FindAll("button")
            .Single(button => button.TextContent == "Cancel")
            .ClickAsync(new MouseEventArgs())
      );
    Assert.True(pending.Token.IsCancellationRequested);
    pending.Reply(fixture.Preview(pending.Body, arrival: 199));
    await editing;
    Assert.Null(fixture.Saved);
    if (!dispose)
      Assert.DoesNotContain("199 US gal", component.Markup);
  }

  [Fact]
  public async Task TruckSwitchCannotUseOldResponseOrConcurrencyToken()
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    fixture.DeferPreview = true;
    var editing = component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    var pending = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    var otherDispatch = Guid.NewGuid();
    await component.InvokeAsync(
      () =>
        component.Render(p =>
          p.Add(x => x.TruckId, Guid.NewGuid())
            .Add(x => x.DispatchId, otherDispatch)
        )
    );
    Assert.True(pending.Token.IsCancellationRequested);
    pending.Reply(fixture.Preview(pending.Body, arrival: 199));
    await editing;
    Assert.DoesNotContain("199 US gal", component.Markup);
    Assert.Null(fixture.Requests[^1].Body.Stops);
    Assert.Contains(otherDispatch.ToString(), fixture.Requests[^1].Path);
    Assert.True(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
  }

  [Fact]
  public async Task AutomaticCalculationRemainsAvailableAfterPreviewFailureWithoutRenewingTheOpenedVersion()
  {
    using var fixture = new Fixture { FailInitialPreview = true };
    var component = fixture.Render();
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "Preview unavailable",
          component.Find("[role='alert']").TextContent
        )
    );
    await component.InvokeAsync(
      () =>
        component.Render(p =>
          p.Add(x => x.InitialCalculatedAt, fixture.OriginalToken.AddMinutes(1))
        )
    );
    await component
      .FindAll("button")
      .Single(button => button.TextContent == "Calculate automatically")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(2, fixture.Requests.Count);
    Assert.NotNull(fixture.Reset);
    Assert.Equal(
      fixture.OriginalToken,
      fixture.Requests[^1].Body.ExpectedCalculatedAt
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AutomaticCalculationStartsImmediatelyWithFrozenVersion(
    bool edited
  )
  {
    using var fixture = new Fixture();
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    if (edited)
      await component
        .Find("[aria-label='Remove selected fuel stop']")
        .ClickAsync(new MouseEventArgs());
    await component.InvokeAsync(
      () =>
        component.Render(p =>
          p.Add(x => x.InitialCalculatedAt, fixture.OriginalToken.AddMinutes(1))
        )
    );
    var requests = fixture.Requests.Count;
    await component.InvokeAsync(
      () =>
        component
          .FindAll("button")
          .Single(button => button.TextContent == "Calculate automatically")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(() => Assert.NotNull(fixture.Reset));
    Assert.Equal(requests + 1, fixture.Requests.Count);
    Assert.Empty(
      component.FindAll("[aria-label='Replace fuel plan confirmation']")
    );
    Assert.EndsWith("/fuel/reset", fixture.Requests[^1].Path);
    Assert.Equal(
      fixture.OriginalToken,
      fixture.Requests[^1].Body.ExpectedCalculatedAt
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AutomaticCalculationBlocksDuplicatesAndRetainsFailedDraft(
    bool failure
  )
  {
    using var fixture = new Fixture { DeferReset = true };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component
      .Find("[aria-label='Remove selected fuel stop']")
      .ClickAsync(new MouseEventArgs());
    var draft = component.Find(".fuel-plan-editor__stops").TextContent;
    var requests = fixture.Requests.Count;
    var busy = new List<bool>();
    await component.InvokeAsync(
      () => component.Render(p => p.Add(x => x.BusyChanged, busy.Add))
    );
    var button = () =>
      component
        .FindAll("button")
        .Single(button => button.TextContent == "Calculate automatically");
    var calculating = button().ClickAsync(new MouseEventArgs());
    var pending = await fixture
      .Pending.Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(requests + 1, fixture.Requests.Count);
    Assert.Equal(new[] { true }, busy);
    component.WaitForAssertion(
      () => Assert.True(button().HasAttribute("disabled"))
    );
    Assert.Equal("true", component.Find("section").GetAttribute("aria-busy"));
    Assert.Empty(component.FindAll(".fuel-plan-editor__confirmation"));
    await button().ClickAsync(new MouseEventArgs());
    Assert.Equal(requests + 1, fixture.Requests.Count);

    pending.Completion.SetResult(
      failure
        ? new(HttpStatusCode.ServiceUnavailable)
        {
          Content = JsonContent.Create(
            new
            {
              success = false,
              errors = new[] { "Calculation unavailable" },
            }
          ),
        }
        : Ok(
          new AutomaticPlanningResult(
            fixture.Truck,
            fixture.Dispatch,
            1375,
            null,
            null
          )
        )
    );
    await calculating;

    Assert.Equal(new[] { true, false }, busy);
    Assert.False(button().HasAttribute("disabled"));
    Assert.Equal("false", component.Find("section").GetAttribute("aria-busy"));
    Assert.Equal(requests + 1, fixture.Requests.Count);
    if (failure)
    {
      Assert.Null(fixture.Reset);
      Assert.Contains(
        "Calculation unavailable",
        component.Find("[role='alert']").TextContent
      );
      Assert.Equal(
        draft,
        component.Find(".fuel-plan-editor__stops").TextContent
      );
    }
    else
      Assert.NotNull(fixture.Reset);
  }

  [Fact]
  public async Task SaveConflictLeavesDraftVisibleAndDoesNotReplaceItsExpectedVersion()
  {
    using var fixture = new Fixture { SaveConflict = true };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("[aria-label='Remove selected fuel stop']")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(
      () =>
        Assert.False(
          component
            .Find("button.fuel-plan-editor__save")
            .HasAttribute("disabled")
        )
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("button.fuel-plan-editor__save")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "changed elsewhere",
          component.Find("[role='alert']").TextContent
        )
    );
    Assert.Single(component.FindAll(".fuel-plan-editor__stop"));
    Assert.Null(fixture.Saved);
    Assert.Equal(
      fixture.OriginalToken,
      fixture.Requests[^1].Body.ExpectedCalculatedAt
    );
    Assert.True(
      component.Find("button.fuel-plan-editor__save").HasAttribute("disabled")
    );
  }

  [Fact]
  public async Task ManualCalculateControlOpensEditorWithoutAnAutomaticWrite()
  {
    using var fixture = new Fixture();
    fixture.Context.Services.AddSingleton(
      new PlanningDisplayCache(fixture.Api)
    );
    var edits = 0;
    var component = fixture.Context.Render<FuelRecalculateButton>(p =>
      p.Add(x => x.DispatchId, fixture.Dispatch)
        .Add(x => x.ManuallyEdited, true)
        .Add(x => x.EditRequested, () => edits++)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("button[aria-label='Edit fuel plan']")
          .ClickAsync(new MouseEventArgs())
    );
    component.WaitForAssertion(() => Assert.Equal(1, edits));
    Assert.Empty(fixture.Requests);
  }

  [Fact]
  public async Task UnresolvedSavedStationsRemainDeletableWithoutInventingZeroFuelValues()
  {
    using var fixture = new Fixture
    {
      ValuesAvailable = false,
      PreviewErrors =
      [
        "A station has no current price. Remove it or select another station.",
      ],
    };
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".fuel-plan-editor__stop").Count)
    );
    await component.InvokeAsync(
      () =>
        component
          .Find("input[type='checkbox']")
          .ChangeAsync(new ChangeEventArgs { Value = false })
    );
    component.WaitForAssertion(
      () =>
        Assert.Contains(
          "no current price",
          component.Find("[role='alert']").TextContent
        )
    );
    Assert.DoesNotContain("13 US gal", component.Markup);
    Assert.DoesNotContain(
      "$87.65",
      component.Find(".fuel-plan-editor__totals").TextContent
    );
    Assert.Contains(
      "—",
      component.Find(".fuel-plan-editor__totals").TextContent
    );
    Assert.Contains(
      "100 Test Street",
      component.Find(".fuel-plan-editor__address").TextContent
    );
    Assert.False(
      component
        .Find("[aria-label='Remove selected fuel stop']")
        .HasAttribute("disabled")
    );
  }

  private sealed record Recorded(
    string Method,
    string Path,
    FuelPlanEditRequest Body
  );

  private sealed class PendingRequest(
    FuelPlanEditRequest body,
    CancellationToken token
  )
  {
    public FuelPlanEditRequest Body { get; } = body;
    public CancellationToken Token { get; } = token;
    public TaskCompletionSource<HttpResponseMessage> Completion { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Reply(FuelPlanEditPreview preview) =>
      Completion.SetResult(Ok(preview));
  }

  private sealed class Fixture : IDisposable
  {
    public BunitContext Context { get; } = new();
    public FakeTimeProvider Clock { get; } = new();
    public Guid Truck { get; } = Guid.NewGuid();
    public Guid Dispatch { get; } = Guid.NewGuid();
    public Guid StationA { get; } = Guid.NewGuid();
    public Guid StationB { get; } = Guid.NewGuid();
    public Guid StationC { get; } = Guid.NewGuid();
    public Guid BeforeA { get; } = Guid.NewGuid();
    public Guid BeforeB { get; } = Guid.NewGuid();
    public Guid BeforeC { get; } = Guid.NewGuid();
    public DateTime OriginalToken { get; } =
      new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    public DateTime? PreviewToken { get; set; }
    public List<string> PreviewErrors { get; set; } = [];
    public bool DeferPreview { get; set; }
    public bool FailInitialPreview { get; set; }
    public bool DeferSave { get; set; }
    public bool DeferReset { get; set; }
    public bool SaveConflict { get; set; }
    public bool ValuesAvailable { get; set; } = true;
    public List<Recorded> Requests { get; } = [];
    public List<FuelPlanStop?> Selections { get; } = [];
    public BunitJSModuleInterop ReorderModule { get; }
    public BunitJSModuleInterop Reorder { get; }
    public Channel<PendingRequest> Pending { get; } =
      Channel.CreateUnbounded<PendingRequest>();
    public FuelPlanEditPreview? Saved { get; private set; }
    public AutomaticPlanningResult? Reset { get; private set; }
    public int Closed { get; private set; }
    public FuelPlanEditPreview Initial { get; set; }
    public ApiService Api { get; }
    private readonly HttpClient _http;

    public Fixture()
    {
      Context
        .JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true)
        .SetVoidResult();
      ReorderModule = Context.JSInterop.SetupModule(
        "./js/generated/shared/reorderList.js"
      );
      Reorder = ReorderModule.SetupModule("attachReorderList", _ => true);
      Reorder.SetupVoid("dispose").SetVoidResult();
      Initial = Preview(
        new(
          null,
          [new(StationA, BeforeA, 0, true), new(StationB, BeforeB, 70, false)]
        )
      );
      _http = new(new StubHttpMessageHandler(RespondAsync))
      {
        BaseAddress = new("https://fixture.invalid/"),
      };
      Api = new(_http);
      Context.Services.AddSingleton(Api);
      Context.Services.AddSingleton<TimeProvider>(Clock);
    }

    public IRenderedComponent<FuelPlanEditor> Render() =>
      Context.Render<FuelPlanEditor>(p =>
        p.Add(x => x.TruckId, Truck)
          .Add(x => x.DispatchId, Dispatch)
          .Add(x => x.TruckNumber, "54777")
          .Add(x => x.InitialCalculatedAt, OriginalToken)
          .Add(x => x.Saved, (FuelPlanEditPreview value) => Saved = value)
          .Add(x => x.Reset, (AutomaticPlanningResult value) => Reset = value)
          .Add(
            x => x.StationSelected,
            (FuelPlanStop? value) => Selections.Add(value)
          )
          .Add(x => x.Closed, () => Closed++)
      );

    public FuelPlanEditPreview Preview(
      FuelPlanEditRequest body,
      double arrival = 13
    )
    {
      var edits = body.Stops!.Select(edit =>
          edit with
          {
            BeforeStopId =
              edit.BeforeStopId
              ?? (
                edit.StationId == StationA ? BeforeA
                : edit.StationId == StationB ? BeforeB
                : BeforeC
              ),
            PurchaseLimitGallons = ValuesAvailable
              ? edit.StationId == StationA
                ? 177.3
                : edit.StationId == StationB
                  ? 98.4
                  : 87.6
              : null,
          }
        )
        .ToList();
      var plan = new FuelPlan
      {
        TruckId = Truck,
        PurchaseCostUsd = 87.65,
        PurchaseGallons = 103,
        ArrivalGallons = 23,
        CalculatedAt = PreviewToken ?? OriginalToken,
        ManuallyEdited = true,
        Stops = edits
          .Select(
            (edit, index) =>
              new FuelPlanStop
              {
                Number = index + 1,
                StationId = edit.StationId,
                BeforeStopId = edit.BeforeStopId!.Value,
                DispatchId = Dispatch,
                Name =
                  edit.StationId == StationA ? "First station"
                  : edit.StationId == StationB ? "Second station"
                  : "New station",
                Address = "100 Test Street, Test City, VA",
                ArrivalGallons = arrival,
                PurchaseCostUsd = edit.StationId == StationA ? 33.21 : 44.32,
                YourPrice = 1.739,
                Currency = "CAD",
                Unit = "L",
                BuyGallons = 30,
                DepartureGallons = 43,
                FillToTarget = edit.FillToTarget,
              }
          )
          .ToList(),
      };
      var first = new PlanStop(
        BeforeA,
        "Fort Mill",
        "Delivery Street, Fort Mill, SC",
        1,
        new(1, 1)
      )
      {
        Job = "Drop Off",
      };
      var pickup = new PlanStop(
        BeforeB,
        "Greensboro",
        "Pickup Street, Greensboro, NC",
        2,
        new(2, 2)
      )
      {
        Job = "Pick Up",
      };
      var final = new PlanStop(
        BeforeC,
        "Amsterdam",
        "Final Street, Amsterdam, NY",
        3,
        new(3, 3)
      )
      {
        Job = "Delivery",
      };
      return new(
        plan,
        edits,
        PreviewToken ?? OriginalToken,
        211.3,
        211.3,
        PreviewErrors.ToList(),
        ValuesAvailable,
        [
          new(BeforeA, null, first, Dispatch),
          new(BeforeB, first, pickup, Dispatch),
          new(BeforeC, pickup, final, Dispatch),
        ]
      );
    }

    private async Task<HttpResponseMessage> RespondAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var body = (
        await request.Content!.ReadFromJsonAsync<FuelPlanEditRequest>(ct)
      )!;
      var path = request.RequestUri!.AbsolutePath;
      Requests.Add(new(request.Method.Method, path, body));
      if (path.EndsWith("/reset", StringComparison.Ordinal))
      {
        if (DeferReset)
        {
          var pending = new PendingRequest(body, ct);
          await Pending.Writer.WriteAsync(pending, ct);
          return await pending.Completion.Task;
        }
        return Ok(
          new AutomaticPlanningResult(Truck, Dispatch, 1375, null, null)
        );
      }
      if (body.Stops is null && FailInitialPreview)
        return new(HttpStatusCode.ServiceUnavailable)
        {
          Content = JsonContent.Create(
            new { success = false, errors = new[] { "Preview unavailable" } }
          ),
        };
      if (request.Method == HttpMethod.Put && SaveConflict)
        return new(HttpStatusCode.Conflict)
        {
          Content = JsonContent.Create(
            new
            {
              success = false,
              errors = new[]
              {
                "The plan changed elsewhere. Reopen it before saving.",
              },
            }
          ),
        };
      if (body.Stops is null)
        return Ok(Initial);
      if (
        (DeferPreview && request.Method == HttpMethod.Post)
        || (DeferSave && request.Method == HttpMethod.Put)
      )
      {
        var pending = new PendingRequest(body, ct);
        await Pending.Writer.WriteAsync(pending, ct);
        return await pending.Completion.Task;
      }
      return Ok(Preview(body));
    }

    public void Dispose()
    {
      Context.Dispose();
      _http.Dispose();
    }
  }

  private static HttpResponseMessage Ok<T>(T response) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<T> { Success = true, Response = response }
      ),
    };
}
