using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchWorkspaceMapLinkTests
{
  [Fact]
  public async Task CurrentNativeTruckOwnsMapAndMileageWhileHistoryIsSelected()
  {
    var data = Workspace();
    var current = data.Load.Stops[^1].TruckId;
    var imported = data.Load.TruckId;
    using var context = Context(data);
    var component = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          $"/fleet/map?truckId={current}",
          MapLink(component)?.GetAttribute("href")
        )
    );
    Assert.Equal(
      current,
      component
        .FindComponent<DispatchMileageBreakdown>()
        .Instance.DefaultTruckId
    );

    await component
      .Find($"[data-stop-id='{data.Stops[0].Id}'] .stop-workspace__select")
      .ClickAsync(new MouseEventArgs());

    Assert.Equal(
      $"/fleet/map?truckId={current}",
      MapLink(component)?.GetAttribute("href")
    );
    Assert.Equal(imported, data.Load.TruckId);
    Assert.Equal(
      current,
      component
        .FindComponent<DispatchMileageBreakdown>()
        .Instance.DefaultTruckId
    );
    await component
      .Find($"[data-stop-id='{data.Stops[0].Id}'] .stop-workspace__select")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      $"/fleet/map?truckId={current}",
      MapLink(component)?.GetAttribute("href")
    );
  }

  [Fact]
  public void CompletedNativeLoadUsesLastRecordedTruckNotImportedHeader()
  {
    var data = Workspace();
    foreach (var stop in data.Load.Stops)
      stop.ExecutionCompleted = true;
    using var context = Context(data);
    var component = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );

    component.WaitForAssertion(
      () =>
        Assert.Equal(
          $"/fleet/map?truckId={data.Load.Stops[^1].TruckId}",
          MapLink(component)?.GetAttribute("href")
        )
    );
  }

  [Theory]
  [InlineData("drop", "confirmed", false)]
  [InlineData("drop", "confirmed", true)]
  [InlineData("release", "confirmed", false)]
  [InlineData("release", "confirmed", true)]
  [InlineData("drop", "planned", false)]
  [InlineData("release", "planned", false)]
  public void OnlyConfirmedReleaseExcludesTheOldLegWithMissingPickupActuals(
    string action,
    string status,
    bool incomingCompleted
  )
  {
    var data = WithTransfer(action, status);
    data.Load.Stops[^1].ExecutionCompleted = incomingCompleted;
    var expected =
      status == "confirmed" ? data.Load.Stops[^1].TruckId : data.Load.TruckId;
    using var context = Context(data);
    var component = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );

    component.WaitForAssertion(
      () =>
        Assert.Equal(
          $"/fleet/map?truckId={expected}",
          MapLink(component)?.GetAttribute("href")
        )
    );
    Assert.Equal(
      expected,
      component
        .FindComponent<DispatchMileageBreakdown>()
        .Instance.DefaultTruckId
    );
    Assert.False(data.Load.Stops[0].IsCompleted);
    Assert.Null(data.Stops[1].Transfer!.ActualAt);
  }

  [Fact]
  public void NoRemainingNativeLegDoesNotRestoreAReleasedTruckAsFallback()
  {
    var data = WithTransfer("drop", "confirmed");
    data.Stops[^1].Transfer = new()
    {
      SwitchId = Guid.NewGuid(),
      Kind = "resource_handoff",
      Action = "release",
      Status = "confirmed",
    };
    using var context = Context(data);
    var component = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );

    component.WaitForElement(".dispatch-details__title");
    Assert.Null(MapLink(component));
    Assert.Null(
      component
        .FindComponent<DispatchMileageBreakdown>()
        .Instance.DefaultTruckId
    );
  }

  [Fact]
  public void MissingNativeTruckDoesNotFallBackToAnOldSourceAssignment()
  {
    var data = Workspace();
    data.Load.Stops[^1].TruckId = null;
    using var context = Context(data);
    var component = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );

    component.WaitForElement(".dispatch-details__title");
    Assert.Null(MapLink(component));
    Assert.Null(
      component
        .FindComponent<DispatchMileageBreakdown>()
        .Instance.DefaultTruckId
    );
  }

  [Fact]
  public void LegacyWorkspaceRetainsItsExistingHeaderAssignment()
  {
    var data = Workspace();
    foreach (var stop in data.Stops)
      stop.ExecutionLegId = null;
    using var context = Context(data);
    var component = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );

    component.WaitForAssertion(
      () =>
        Assert.Equal(
          $"/fleet/map?truckId={data.Load.TruckId}",
          MapLink(component)?.GetAttribute("href")
        )
    );
  }

  private static IElement? MapLink(IRenderedComponent<DispatchDetails> page) =>
    page.FindAll(".dispatch-details__summary a")
      .SingleOrDefault(link => link.TextContent == "Show truck on map");

  private static ClientComponentContext Context(DispatchWorkspaceResponse data)
  {
    var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath.EndsWith("/documents")
            ? MileageComponentResponses.Ok(new List<DispatchDocumentInfo>())
          : request.RequestUri!.AbsolutePath.EndsWith("/activity")
            ? MileageComponentResponses.Ok(
              new DispatchActivityPage(data.Load.Id, 0, [], null, 0, [], null)
            )
          : MileageComponentResponses.Ok(data)
        )
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var auth = context.AddAuthorization();
    auth.SetAuthorized("Dispatcher");
    auth.SetRoles("Dispatch");
    return context;
  }

  private static DispatchWorkspaceResponse WithTransfer(
    string action,
    string status
  )
  {
    var data = Workspace();
    var id = Guid.NewGuid();
    var transferId = Guid.NewGuid();
    var kind = action == "drop" ? "drop_hook" : "resource_handoff";
    data.Load.Stops[0].ExecutionCompleted = false;
    data.Stops.Insert(
      1,
      new()
      {
        Id = id,
        ExecutionLegId = data.Stops[0].ExecutionLegId,
        Transfer = new()
        {
          SwitchId = transferId,
          Kind = kind,
          Action = action,
          Status = status,
        },
      }
    );
    data.Load.Stops.Insert(
      1,
      new()
      {
        Id = id,
        TruckId = data.Load.TruckId,
        ExecutionCompleted = status == "confirmed",
      }
    );
    data.Stops[2].Transfer = new()
    {
      SwitchId = transferId,
      Kind = kind,
      Action = action == "drop" ? "hook" : "receive",
      Status = status,
    };
    data.Load.Stops[2].ExecutionCompleted = status == "confirmed";
    for (var index = 0; index < data.Stops.Count; index++)
    {
      data.Stops[index].Sequence = index + 1;
      data.Load.Stops[index].Sequence = index + 1;
    }
    return data;
  }

  private static DispatchWorkspaceResponse Workspace()
  {
    var outgoing = Guid.NewGuid();
    var incoming = Guid.NewGuid();
    var oldLeg = Guid.NewGuid();
    var newLeg = Guid.NewGuid();
    var stops = Enumerable
      .Range(0, 3)
      .Select(index => new DispatchWorkspaceStop
      {
        Id = Guid.NewGuid(),
        Sequence = index + 1,
        ExecutionLegId = index == 0 ? oldLeg : newLeg,
        TruckNumber = index == 0 ? "11005" : "54777",
        Name =
          index == 0 ? "Pickup"
          : index == 1 ? "Hook"
          : "Delivery",
        Job =
          index == 0 ? "Pick Up"
          : index == 1 ? "Hook"
          : "Delivery",
        CanEdit = index == 2,
        CanMove = index == 2,
        CanRemove = index == 2,
      })
      .ToList();
    return new()
    {
      CanEdit = true,
      Stops = stops,
      Load = new()
      {
        Id = Guid.NewGuid(),
        TruckId = outgoing,
        TruckNumber = "11005",
        Status = "in_transit",
        Stops = stops
          .Select(
            (stop, index) =>
              new DispatchStopResponse
              {
                Id = stop.Id,
                Sequence = stop.Sequence,
                TruckId = index == 0 ? outgoing : incoming,
                ExecutionCompleted = index < 2,
              }
          )
          .ToList(),
      },
    };
  }
}
