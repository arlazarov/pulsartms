using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Pages.FleetMap;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class FleetMapPreferencesTests
{
  [Fact]
  public void SavedChoicesApplyBeforeTelemetryAndOrdinaryStationsLoad()
  {
    using var fixture = new Fixture();
    fixture.Values[fixture.Key] = """
      {"useIfta":false,"showFuelStations":true,
       "showTraffic":false,"showNextLoads":true}
      """;
    var cut = fixture.Render();
    Ready(cut);
    Assert.DoesNotContain(cut.Markup, "IFTA");
    Assert.True(Toggle(cut, "Fuel Stations").HasAttribute("checked"));
    Assert.False(Toggle(cut, "Traffic").HasAttribute("checked"));
    Assert.True(Toggle(cut, "Next loads").HasAttribute("checked"));
    var calls = fixture.Js.Calls.ToArray();
    var options = JsonSerializer.SerializeToElement(
      Assert.Single(calls, call => call.Name == "setOptions").Args![0]
    );
    // A useIfta left in storage by an older visit no longer speaks for the
    // map: the fleet's planning setting does.
    Assert.True(options.GetProperty("useIfta").GetBoolean());
    Assert.True(options.GetProperty("stationsVisible").GetBoolean());
    Assert.False(options.GetProperty("trafficVisible").GetBoolean());
    var next = Array.FindIndex(calls, c => c.Name == "setNextLoadsVisible");
    var trucks = Array.FindIndex(calls, c => c.Name == "setTrucks");
    Assert.True(next >= 0 && trucks > next);
    Assert.Equal(true, calls[next].Args![0]);
    cut.WaitForAssertion(() => Assert.Equal(1, fixture.StationReads));
    Assert.DoesNotContain(calls, c => c.Name == "localStorage.setItem");
  }

  [Theory]
  [InlineData(null)]
  [InlineData("invalid json")]
  [InlineData("null")]
  [InlineData("[]")]
  [InlineData("{\"showTraffic\":\"false\"}")]
  public void MissingOrInvalidStorageKeepsDefaults(string? json)
  {
    using var fixture = new Fixture();
    if (json is not null)
      fixture.Values[fixture.Key] = json;
    var cut = fixture.Render();
    Ready(cut);
    Assert.True(Toggle(cut, "Traffic").HasAttribute("checked"));
    foreach (var label in new[] { "Fuel Stations", "Next loads" })
      Assert.False(Toggle(cut, label).HasAttribute("checked"));
    Assert.Equal(0, fixture.StationReads);
  }

  [Fact]
  public void PartialPreferencesKeepDefaultsForMissingFields()
  {
    using var fixture = new Fixture();
    fixture.Values[fixture.Key] = "{\"showNextLoads\":true}";
    var cut = fixture.Render();
    Ready(cut);
    Assert.True(Toggle(cut, "Traffic").HasAttribute("checked"));
    Assert.True(Toggle(cut, "Next loads").HasAttribute("checked"));
    Assert.False(Toggle(cut, "Fuel Stations").HasAttribute("checked"));
  }

  [Fact]
  public async Task ChoicesSurviveReopeningAndRemainAccountScoped()
  {
    var user = Guid.NewGuid();
    var values = new Dictionary<string, string>();
    using (var first = new Fixture(user, values))
    {
      var cut = first.Render();
      Ready(cut);
      foreach (var label in new[] { "Fuel Stations", "Next loads" })
        await ChangeAsync(cut, label, true);
      await ChangeAsync(cut, "Traffic", false);
      Assert.Equal(
        3,
        first.Js.Calls.Count(call => call.Name == "localStorage.setItem")
      );
      using var json = JsonDocument.Parse(values[first.Key]);
      Assert.Equal(3, json.RootElement.EnumerateObject().Count());
      Assert.False(json.RootElement.GetProperty("showTraffic").GetBoolean());
    }
    using (var returning = new Fixture(user, values))
    {
      var cut = returning.Render();
      Ready(cut);
      Assert.False(Toggle(cut, "Traffic").HasAttribute("checked"));
      foreach (var label in new[] { "Fuel Stations", "Next loads" })
        Assert.True(Toggle(cut, label).HasAttribute("checked"));
    }
    using var another = new Fixture(Guid.NewGuid(), values);
    var other = another.Render();
    Ready(other);
    Assert.True(Toggle(other, "Traffic").HasAttribute("checked"));
    Assert.False(Toggle(other, "Next loads").HasAttribute("checked"));
    Assert.Single(values);
  }

  [Fact]
  public async Task BlockedStorageDoesNotPreventMapOrToggleInteraction()
  {
    using var fixture = new Fixture { BlockStorage = true };
    var cut = fixture.Render();
    Ready(cut);
    await ChangeAsync(cut, "Traffic", false);
    Assert.False(Toggle(cut, "Traffic").HasAttribute("checked"));
    Assert.Contains(
      fixture.Js.Calls,
      call => call.Name == "setTrafficVisible" && Equals(call.Args![0], false)
    );
    Assert.Empty(cut.FindAll(".fleet-map-page__message[role='alert']"));
  }

  [Fact]
  public async Task PendingRestoreDisablesControlsButDoesNotDelayHos()
  {
    using var fixture = new Fixture
    {
      PendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var cut = fixture.Render();
    cut.WaitForAssertion(() => Assert.Equal(1, fixture.HosReads));
    Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
    var controls = cut.Find(".fleet-map-layer-controls");
    Assert.Equal("true", controls.GetAttribute("aria-busy"));
    Assert.Equal("true", controls.GetAttribute("aria-hidden"));
    Assert.DoesNotContain(fixture.Js.Calls, c => c.Name == "createFleetMap");
    fixture.PendingRead.SetResult("{\"showTraffic\":false}");
    Ready(cut);
    Assert.False(Toggle(cut, "Traffic").HasAttribute("checked"));
    controls = cut.Find(".fleet-map-layer-controls");
    Assert.Equal("false", controls.GetAttribute("aria-busy"));
    Assert.Equal("false", controls.GetAttribute("aria-hidden"));
    await cut.Instance.DisposeAsync();
  }

  [Fact]
  public async Task LeavingDuringRestoreCannotCreateALateMap()
  {
    using var fixture = new Fixture
    {
      PendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var cut = fixture.Render();
    cut.WaitForAssertion(
      () =>
        Assert.Contains(fixture.Js.Calls, c => c.Name == "localStorage.getItem")
    );
    var dispose = cut.Instance.DisposeAsync().AsTask();
    fixture.PendingRead.SetResult("{\"showNextLoads\":true}");
    await dispose;
    Assert.DoesNotContain(fixture.Js.Calls, c => c.Name == "createFleetMap");
    Assert.DoesNotContain(
      fixture.Js.Calls,
      c => c.Name == "localStorage.setItem"
    );
  }

  private static void Ready(IRenderedComponent<FleetMap> cut) =>
    cut.WaitForAssertion(
      () => Assert.False(cut.Find("fieldset").HasAttribute("disabled"))
    );

  private static Task ChangeAsync(
    IRenderedComponent<FleetMap> cut,
    string label,
    bool value
  ) =>
    cut.InvokeAsync(
      () =>
        Toggle(cut, label).ChangeAsync(new ChangeEventArgs { Value = value })
    );

  private static IElement Toggle(
    IRenderedComponent<FleetMap> cut,
    string text
  ) =>
    cut.FindAll(".fleet-map-toggle")
      .Single(label => label.TextContent.Trim() == text)
      .QuerySelector("input")!;

  private sealed class Fixture : IDisposable
  {
    private readonly ClientComponentContext context;
    private readonly Guid user;
    public MapInteropStub Js { get; } = new();
    public Dictionary<string, string> Values { get; }
    public string Key => $"pulsartms.fleet-map.preferences.{user:D}";
    public bool BlockStorage { get; init; }
    public TaskCompletionSource<string?>? PendingRead { get; init; }
    public int StationReads;
    public int HosReads;

    public Fixture(Guid? user = null, Dictionary<string, string>? values = null)
    {
      this.user = user ?? Guid.NewGuid();
      Values = values ?? [];
      context = new ClientComponentContext(
        (request, _) =>
        {
          var path = request.RequestUri!.AbsolutePath;
          object response = Array.Empty<object>();
          if (path == "/api/fleet/hos")
          {
            HosReads++;
            response = new Dictionary<string, object>();
          }
          if (path == "/api/fuel/stations")
            StationReads++;
          if (path == "/api/fleet/locations")
            response = new
            {
              trucks = Array.Empty<object>(),
              points = response,
            };
          return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = JsonContent.Create(new { success = true, response }),
            }
          );
        }
      );
      Js.Respond = async (name, args) =>
      {
        if (name is "import" or "createFleetMap" or "observeVisibility")
          return Js;
        if (name == "isVisible")
          return true;
        if (!name.StartsWith("localStorage.", StringComparison.Ordinal))
          return null;
        if (BlockStorage)
          throw new JSException("Storage unavailable");
        if (name == "localStorage.getItem")
          return PendingRead is null
            ? Values.GetValueOrDefault((string)args![0]!)
            : await PendingRead.Task;
        if (name == "localStorage.setItem")
          Values[(string)args![0]!] = (string)args[1]!;
        return null;
      };
      context.Services.AddSingleton<IJSRuntime>(Js);
      context.Services.AddSingleton<IConfiguration>(
        new ConfigurationBuilder().Build()
      );
    }

    public IRenderedComponent<FleetMap> Render()
    {
      var state = Task.FromResult(
        new AuthenticationState(
          new ClaimsPrincipal(
            new ClaimsIdentity(
              [new Claim(ClaimTypes.NameIdentifier, user.ToString())],
              "Test"
            )
          )
        )
      );
      return context
        .Render<CascadingValue<Task<AuthenticationState>>>(p =>
          p.Add(x => x.Value, state).AddChildContent<FleetMap>()
        )
        .FindComponent<FleetMap>();
    }

    public void Dispose() => context.Dispose();
  }
}
