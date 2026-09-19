using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchStopMapTests
{
  [Fact]
  public async Task SavedRoadIsReadOnceAcrossSelectionAndHiddenForUnsavedDraft()
  {
    var load = new DispatchResponse { Id = Guid.NewGuid() };
    var first = new DispatchWorkspaceStop
    {
      Id = Guid.NewGuid(),
      Latitude = 40,
      Longitude = -75,
    };
    var second = new DispatchWorkspaceStop
    {
      Id = Guid.NewGuid(),
      Latitude = 41,
      Longitude = -74,
    };
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.EndsWith(
          $"/{load.Id}/planning/map",
          request.RequestUri!.AbsolutePath
        );
        reads++;
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = JsonContent.Create(
              new
              {
                success = true,
                response = new DispatchMapRoute(
                  load.Id,
                  [
                    new(
                      first.Id,
                      second.Id,
                      Enumerable
                        .Range(0, 20000)
                        .Select(i => new RoutePoint(40 + i * 0.00001, -75))
                        .ToList()
                    ),
                  ],
                  0
                ),
              }
            ),
          }
        );
      }
    );
    context.Services.AddSingleton<IConfiguration>(
      new ConfigurationBuilder()
        .AddInMemoryCollection(
          new Dictionary<string, string?> { ["GoogleMaps:ApiKey"] = "fixture" }
        )
        .Build()
    );
    var module = context.JSInterop.SetupModule(
      "./js/generated/dispatch/dispatch.js"
    );
    var roadPublished = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    module
      .SetupVoid(
        "showStopMap",
        invocation =>
        {
          if (invocation.Arguments[4] is List<RoutePoint>[] { Length: 1 })
            roadPublished.TrySetResult();
          return true;
        }
      )
      .SetVoidResult();
    module.SetupVoid("selectStopMap", _ => true).SetVoidResult();
    module.SetupVoid("disposeStopMap", _ => true).SetVoidResult();
    var component = context.Render<DispatchStopMap>(p =>
      p.Add(x => x.Load, load).Add(x => x.Stops, [first, second])
    );
    await roadPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Single(
      (List<RoutePoint>[])
        module.Invocations.Last(x => x.Identifier == "showStopMap").Arguments[
          4
        ]!
    );
    var publications = module.Invocations.Count(x =>
      x.Identifier == "showStopMap"
    );
    component.Render(p => p.Add(x => x.SelectedStopId, second.Id));
    Assert.Equal(
      publications,
      module.Invocations.Count(x => x.Identifier == "showStopMap")
    );
    Assert.Equal(
      second.Id,
      module.Invocations.Last(x => x.Identifier == "selectStopMap").Arguments[1]
    );
    Assert.Equal(1, reads);
    component.Render(p => p.Add(x => x.DraftChanged, true));
    Assert.Empty(
      (List<RoutePoint>[])
        module.Invocations.Last(x => x.Identifier == "showStopMap").Arguments[
          4
        ]!
    );
    Assert.Equal(1, reads);
    component.Render(p => p.Add(x => x.DraftChanged, false));
    Assert.Equal(2, reads);
  }

  [Fact]
  public void MissingKeyKeepsServerMileageAndDoesNotLoadProvider()
  {
    using var context = new ClientComponentContext(
      (_, _) => throw new InvalidOperationException("No request expected")
    );
    context.Services.AddSingleton<IConfiguration>(
      new ConfigurationBuilder().Build()
    );
    var truck = Guid.NewGuid();
    var component = context.Render<DispatchStopMap>(p =>
      p.Add(x => x.TruckId, truck)
        .Add(
          x => x.Load,
          new DispatchResponse { TotalMiles = 123, LoadedMiles = 100 }
        )
    );
    Assert.Contains("Map is not configured", component.Markup);
    Assert.Contains("123", component.Find("dl").TextContent);
    Assert.Equal(
      $"/fleet/map?truckId={truck}",
      component.Find("a").GetAttribute("href")
    );
    Assert.Empty(context.JSInterop.Invocations);
  }

  [Fact]
  public async Task UnchangedRenderDoesNotRepublishAndSelectionDoesNotReplaceMap()
  {
    using var context = new ClientComponentContext(
      (_, _) => throw new InvalidOperationException("No request expected")
    );
    context.Services.AddSingleton<IConfiguration>(
      new ConfigurationBuilder()
        .AddInMemoryCollection(
          new Dictionary<string, string?> { ["GoogleMaps:ApiKey"] = "fixture" }
        )
        .Build()
    );
    var module = context.JSInterop.SetupModule(
      "./js/generated/dispatch/dispatch.js"
    );
    module.SetupVoid("showStopMap", _ => true).SetVoidResult();
    module.SetupVoid("selectStopMap", _ => true).SetVoidResult();
    module.SetupVoid("disposeStopMap", _ => true).SetVoidResult();
    var first = new DispatchWorkspaceStop
    {
      Id = Guid.NewGuid(),
      Latitude = 40,
      Longitude = -75,
    };
    var second = new DispatchWorkspaceStop
    {
      Id = Guid.NewGuid(),
      Latitude = 41,
      Longitude = -74,
    };
    var stops = new List<DispatchWorkspaceStop> { first, second };
    var component = context.Render<DispatchStopMap>(p =>
      p.Add(x => x.Stops, stops).Add(x => x.SelectedStopId, first.Id)
    );
    var canvas = module
      .Invocations.Single(x => x.Identifier == "showStopMap")
      .Arguments[0];
    component.Render();
    Assert.Single(module.Invocations, x => x.Identifier == "showStopMap");
    component.Render(p => p.Add(x => x.SelectedStopId, second.Id));
    Assert.Equal(
      1,
      module.Invocations.Count(x => x.Identifier == "showStopMap")
    );
    Assert.Equal(
      canvas,
      module.Invocations.Last(x => x.Identifier == "selectStopMap").Arguments[0]
    );
    await component.Instance.DisposeAsync();
    Assert.Single(module.Invocations, x => x.Identifier == "disposeStopMap");
  }
}
