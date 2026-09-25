using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Services;
using Client.Shared.Dispatch.TruckAssignmentEditor;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchDetailsRenderingTests
{
  [Theory]
  [InlineData("assigned")]
  [InlineData("completed")]
  public async Task SwitchRefreshRetainsDistinctSiblingSections(string status)
  {
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1382,
      Status = status,
    };
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        if (request.RequestUri!.AbsolutePath.EndsWith("/activity"))
          return Task.FromResult(Activity(load.Id));
        Assert.Equal(
          $"/api/dispatch/{load.Id}/workspace",
          request.RequestUri!.AbsolutePath
        );
        reads++;
        return Task.FromResult(Workspace(load));
      }
    );
    var auth = context.AddAuthorization();
    auth.SetAuthorized("Dispatcher");
    auth.SetRoles("Dispatch");
    var page = context.Render<DispatchDetails>(p => p.Add(x => x.Id, load.Id));
    page.WaitForAssertion(
      () => Assert.Single(page.FindComponents<DispatchSwitchSection>())
    );
    var mileage = page.FindComponent<DispatchMileageBreakdown>().Instance;
    var switches = page.FindComponent<DispatchSwitchSection>().Instance;
    Assert.Empty(page.FindComponents<TruckAssignmentEditor>());

    for (var i = 0; i < 2; i++)
    {
      load.OrderNumber = $"Updated-{i}";
      await page.InvokeAsync(() => switches.Changed.InvokeAsync());
      Assert.Contains(load.OrderNumber, page.Markup);
      Assert.Same(
        mileage,
        page.FindComponent<DispatchMileageBreakdown>().Instance
      );
      Assert.Same(
        switches,
        page.FindComponent<DispatchSwitchSection>().Instance
      );
      Assert.Empty(page.FindComponents<TruckAssignmentEditor>());
    }
    Assert.Equal(3, reads);
  }

  [Fact]
  public void AnotherLoadReplacesEveryLoadScopedSection()
  {
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        var id = Guid.Parse(request.RequestUri!.Segments[^2].Trim('/'));
        if (request.RequestUri.AbsolutePath.EndsWith("/activity"))
          return Task.FromResult(Activity(id));
        return Task.FromResult(
          Workspace(new DispatchResponse { Id = id, Status = "assigned" })
        );
      }
    );
    var auth = context.AddAuthorization();
    auth.SetAuthorized("Dispatcher");
    auth.SetRoles("Dispatch");
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, Guid.NewGuid())
    );
    page.WaitForAssertion(
      () => Assert.Single(page.FindComponents<DispatchSwitchSection>())
    );
    var mileage = page.FindComponent<DispatchMileageBreakdown>().Instance;
    var switches = page.FindComponent<DispatchSwitchSection>().Instance;
    var stops = page.FindComponent<DispatchStopWorkspace>().Instance;
    Assert.Empty(page.FindComponents<TruckAssignmentEditor>());

    var next = Guid.NewGuid();
    page.Render(p => p.Add(x => x.Id, next));
    page.WaitForAssertion(
      () =>
        Assert.Equal(
          next,
          page.FindComponent<DispatchSwitchSection>().Instance.DispatchId
        )
    );
    var nextMileage = page.FindComponent<DispatchMileageBreakdown>().Instance;
    var nextSwitches = page.FindComponent<DispatchSwitchSection>().Instance;
    var nextStops = page.FindComponent<DispatchStopWorkspace>().Instance;
    Assert.NotSame(mileage, nextMileage);
    Assert.NotSame(switches, nextSwitches);
    Assert.NotSame(stops, nextStops);
    Assert.Equal(next, nextMileage.DispatchId);
    Assert.Equal(next, nextSwitches.DispatchId);
    Assert.Equal(next, nextStops.Load!.Id);
    Assert.Empty(page.FindComponents<TruckAssignmentEditor>());
  }

  // The load page's Back goes to the page that opened it, named for it;
  // a crafted or missing origin goes to Dispatch, never elsewhere.
  [Theory]
  [InlineData(
    "/fleet/map?truckId=1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b",
    "← Back to map"
  )]
  [InlineData("/dispatch?scope=completed&q=11006&page=2", "← Back to Dispatch")]
  [InlineData(
    "/messages/1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b",
    "← Back to conversation"
  )]
  [InlineData("https://example.com/", "← Back to Dispatch")]
  [InlineData("//example.com/fleet/map", "← Back to Dispatch")]
  [InlineData(null, "← Back to Dispatch")]
  public void BackReturnsToTheOriginOrToDispatch(string? from, string label)
  {
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1441,
      Status = "assigned",
    };
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath.EndsWith("/activity")
            ? Activity(load.Id)
            : Workspace(load)
        )
    );
    var auth = context.AddAuthorization();
    auth.SetAuthorized("Dispatcher");
    auth.SetRoles("Dispatch");
    context
      .Services.GetRequiredService<NavigationManager>()
      .NavigateTo(
        $"/dispatch/{load.Id}"
          + (from is null ? "" : $"?from={Uri.EscapeDataString(from)}")
      );

    var page = context.Render<DispatchDetails>(p => p.Add(x => x.Id, load.Id));

    var back = page.Find(".dispatch-details__navigation > a");
    Assert.Equal(label, back.TextContent.Trim());
    Assert.Equal(
      ReturnNavigation.Resolve(from).Href,
      back.GetAttribute("href")
    );
  }

  private static HttpResponseMessage Workspace(DispatchResponse load) =>
    MileageComponentResponses.Ok(
      new DispatchWorkspaceResponse
      {
        Load = load,
        CanEdit = true,
        Metadata = new() { OrderNumber = load.OrderNumber },
      }
    );

  private static HttpResponseMessage Activity(Guid id) =>
    MileageComponentResponses.Ok(
      new DispatchActivityPage(id, 0, [], null, 0, [], null)
    );
}
