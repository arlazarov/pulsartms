using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Pages.FleetMap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Client.Tests.Fleet;

// The trip chain names each load as the Dispatch board does,
// from the server's placement; it never places a load itself.
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class FleetTripChainTests
{
  private static readonly TruckLocationMapDto Truck = new()
  {
    TruckId = Guid.NewGuid(),
    UnitNumber = "54777",
  };

  [Fact]
  public void LoadsReadTheServerPhaseInTheBoardsOrder()
  {
    using var context = Context();
    var current = Load(1409, "current");
    var next = Load(1410, "next");
    var stale = Load(1411, "stale");
    var unknown = Load(1412, "unknown");
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck)
        .Add(x => x.Loads, [current, next, stale, unknown])
        .Add(x => x.CurrentId, current.Id)
    );
    Assert.Equal(
      ["Current", "Next", "Needs refresh"],
      component
        .FindAll(".fleet-trip-chain__phase")
        .Select(x => x.TextContent.Trim())
    );
    var links = component.FindAll(".fleet-trip-chain__link");
    Assert.Contains("is-current", links[0].ClassName);
    Assert.Contains("is-next", links[1].ClassName);
    // Neither a stale nor an unplaced load borrows a place's colour.
    Assert.Contains("is-unplaced", links[2].ClassName);
    Assert.Contains("is-unplaced", links[3].ClassName);
  }

  // A trip card is chosen whole; its stops are small markers, and the
  // chain has no controls of its own (the owner, September 27).
  [Fact]
  public void EveryTripIsShownAndChosenWithoutStopChipsOrControls()
  {
    using var context = Context();
    var loads = Enumerable
      .Range(0, 7)
      .Select(i => Load(1400 + i, i == 0 ? "current" : "upcoming", 2))
      .ToList();
    DispatchResponse? trip = null;
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck)
        .Add(x => x.Loads, loads)
        .Add(x => x.CurrentId, loads[0].Id)
        .Add(x => x.SelectedTrip, loads[3])
        .Add(x => x.Selected, load => trip = load)
    );
    Assert.Equal(7, component.FindAll(".fleet-trip-chain__link").Count);
    // Each card marks its stops, P and D1 / D2, as markers, not controls.
    Assert.Equal(
      ["P", "D1", "D2"],
      component
        .FindAll(".fleet-trip-chain__link")[0]
        .QuerySelectorAll(".fleet-trip-chain__stop")
        .Select(x => x.TextContent.Trim())
    );
    Assert.Empty(
      component.FindAll(".fleet-trip-chain button:not(.fleet-trip-chain__trip)")
    );
    Assert.DoesNotContain("All trips", component.Markup);
    Assert.DoesNotContain("Show next loads", component.Markup);
    Assert.Contains(
      "is-selected",
      component.FindAll(".fleet-trip-chain__link")[3].ClassName
    );
    component.FindAll(".fleet-trip-chain__trip")[5].Click();
    Assert.Same(loads[5], trip);
  }

  // A stop is checked when the server says it is done; a stop the truck
  // only drove past is not.
  [Fact]
  public void AStopIsCheckedOnlyWhenTheServerSaysItIsDone()
  {
    using var context = Context();
    var load = Load(1409, "current");
    load.Stops[0].IsCompleted = true;
    load.Stops[1].StateAfter = "Passed";
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck).Add(x => x.Loads, [load])
    );
    var stops = component.FindAll(".fleet-trip-chain__stop");
    Assert.Contains("is-done", stops[0].ClassName);
    Assert.NotNull(stops[0].QuerySelector("svg"));
    Assert.Equal("P, Pickup, completed", stops[0].GetAttribute("aria-label"));
    Assert.DoesNotContain("is-done", stops[1].ClassName);
    Assert.Null(stops[1].QuerySelector("svg"));
  }

  // The strip's wheel handler follows the list: a new list (another truck)
  // releases the old handler and its reference, and leaving releases the
  // last one.
  [Fact]
  public async Task TheWheelHandlerIsReleasedOnRebindAndOnDisposal()
  {
    using var context = new BunitContext();
    var module = context.JSInterop.SetupModule(
      "./js/generated/shared/horizontalWheel.js"
    );
    var handler = module.SetupModule("bindHorizontalWheel", _ => true);
    handler.SetupVoid("dispose").SetVoidResult();
    var loads = new List<DispatchResponse> { Load(1409, "current") };
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck).Add(x => x.Loads, loads)
    );
    Assert.Single(module.Invocations["bindHorizontalWheel"]);

    // Another truck: the list goes while it loads and comes back anew.
    component.Render(p => p.Add(x => x.Loading, true));
    component.Render(p => p.Add(x => x.Loading, false));
    Assert.Equal(2, module.Invocations["bindHorizontalWheel"].Count);
    Assert.Single(handler.Invocations["dispose"]);

    await component.Instance.DisposeAsync();
    Assert.Equal(2, handler.Invocations["dispose"].Count);
  }

  // Leaving while the module is still being imported: the import that
  // arrives afterwards is released at once and binds nothing.
  [Fact]
  public async Task AnImportThatArrivesAfterDisposalIsReleased()
  {
    using var context = new BunitContext();
    var runtime = new HeldImportRuntime();
    context.Services.AddSingleton<IJSRuntime>(runtime);
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck).Add(x => x.Loads, [Load(1409, "current")])
    );
    await component.Instance.DisposeAsync();
    var late = new CountingModule();
    runtime.Import.SetResult(late);
    await late.Released.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(1, late.Disposals);
    Assert.Empty(late.Calls);
  }

  private sealed class HeldImportRuntime : IJSRuntime
  {
    public TaskCompletionSource<IJSObjectReference> Import { get; } = new();

    public ValueTask<TValue> InvokeAsync<TValue>(
      string identifier,
      object?[]? args
    ) =>
      identifier == "import"
        ? new(Import.Task.ContinueWith(t => (TValue)t.Result))
        : ValueTask.FromResult(default(TValue)!);

    public ValueTask<TValue> InvokeAsync<TValue>(
      string identifier,
      CancellationToken cancellationToken,
      object?[]? args
    ) => InvokeAsync<TValue>(identifier, args);
  }

  private sealed class CountingModule : IJSObjectReference
  {
    public int Disposals;
    public List<string> Calls { get; } = [];
    public TaskCompletionSource Released { get; } = new();

    public ValueTask<TValue> InvokeAsync<TValue>(
      string identifier,
      object?[]? args
    )
    {
      Calls.Add(identifier);
      return ValueTask.FromResult(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(
      string identifier,
      CancellationToken cancellationToken,
      object?[]? args
    ) => InvokeAsync<TValue>(identifier, args);

    public ValueTask DisposeAsync()
    {
      Disposals++;
      Released.TrySetResult();
      return ValueTask.CompletedTask;
    }
  }

  [Fact]
  public void TheServersConflictIsSaidOnTheLoad()
  {
    using var context = Context();
    var passed = Load(1408, "earlier");
    passed.WorkConflict = "route_passed_not_delivered";
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck).Add(x => x.Loads, [passed])
    );
    Assert.Equal(
      "Route passed · not delivered",
      component.Find(".fleet-trip-chain__conflict").TextContent.Trim()
    );
    Assert.Empty(component.FindAll(".fleet-trip-chain__phase"));
  }

  [Fact]
  public void AFailedReadSaysSoInsteadOfAnEmptyChain()
  {
    using var context = Context();
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck).Add(x => x.Failed, true)
    );
    Assert.Contains("could not be read", component.Markup);
    Assert.Empty(component.FindAll(".fleet-trip-chain__trip"));
  }

  // The strip binds its wheel through a module; the other tests do not
  // look at it.
  private static BunitContext Context()
  {
    var context = new BunitContext();
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context
      .JSInterop.SetupModule("./js/generated/shared/horizontalWheel.js")
      .Mode = JSRuntimeMode.Loose;
    return context;
  }

  private static DispatchResponse Load(
    int number,
    string phase,
    int deliveries = 1
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = number,
      WorkPhase = phase,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pickup",
          City = "Nashville",
          Province = "TN",
        },
        .. Enumerable
          .Range(0, deliveries)
          .Select(i => new DispatchStopResponse
          {
            Id = Guid.NewGuid(),
            Sequence = i + 2,
            Job = "Delivery",
            City = "Knoxville",
            Province = "TN",
          }),
      ],
    };
}
