using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Shared.Dispatch.TruckAssignmentEditor;
using Client.Tests.Support;

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
