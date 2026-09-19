using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO.Mileage;
using Client.Pages.Dispatch;
using Client.Tests.Support;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchMileageBreakdownTests
{
  [Theory]
  [InlineData("native-route")]
  [InlineData("samsara-obd")]
  public async Task AutomaticMileageAllowsAllocationButNotDistanceEdits(
    string origin
  )
  {
    var id = Guid.NewGuid();
    var row = MileageComponentResponses.Row(id, editable: true) with
    {
      Origin = origin,
      CanEditDistance = false,
    };
    var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
    var state = MileageComponentResponses.Breakdown(id, row) with
    {
      ActualIsPartial = true,
      PendingObservedIntervals = 2,
      CaptureGaps =
      [
        new(
          Guid.NewGuid(),
          row.TruckId!.Value,
          now.AddHours(-1),
          now,
          "odometer-discontinuity"
        ),
      ],
    };
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        return Task.FromResult(MileageComponentResponses.Ok(state));
      }
    );
    Authorize(context);
    var component = context.Render<DispatchMileageBreakdown>(p =>
      p.Add(x => x.DispatchId, id)
    );
    await Button(component, "Mileage breakdown").ClickAsync(new());
    Assert.Contains("Change allocation", component.Markup);
    Assert.DoesNotContain("Add distance evidence", component.Markup);
    Assert.Contains("Recorded actual subtotal", component.Markup);
    Assert.Contains("Actual mileage coverage gaps", component.Markup);
    Assert.Contains("Odometer Discontinuity", component.Markup);
  }

  [Fact]
  public async Task ReadsOnlyWhenOpenedAndNeverUsesPlanAsActual()
  {
    var id = Guid.NewGuid();
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
          $"/api/dispatch/{id}/mileage-breakdown",
          request.RequestUri!.AbsolutePath
        );
        reads++;
        return Task.FromResult(
          MileageComponentResponses.Ok(
            MileageComponentResponses.Breakdown(
              id,
              MileageComponentResponses.Row(id)
            )
          )
        );
      }
    );
    Authorize(context);
    var component = context.Render<DispatchMileageBreakdown>(p =>
      p.Add(x => x.DispatchId, id)
    );
    Assert.Equal(0, reads);
    await Button(component, "Mileage breakdown").ClickAsync(new());
    component.WaitForElement(".dispatch-mileage__bases");
    var totals = component.FindAll(".dispatch-mileage__basis");
    Assert.Contains("120", totals[0].TextContent);
    Assert.Contains("already included in Empty", totals[0].TextContent);
    Assert.DoesNotContain("120", totals[1].TextContent);
    Assert.Contains("Missing distance evidence", totals[1].TextContent);
    Assert.DoesNotContain("Change allocation", component.Markup);
    await Button(component, "Hide breakdown").ClickAsync(new());
    await Button(component, "Mileage breakdown").ClickAsync(new());
    Assert.Equal(1, reads);
  }

  [Fact]
  public async Task LateReadCannotReplaceAnotherLoadsBreakdown()
  {
    var firstId = Guid.NewGuid();
    var secondId = Guid.NewGuid();
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    CancellationToken firstToken = default;
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        reads++;
        if (request.RequestUri!.AbsolutePath.Contains(firstId.ToString()))
        {
          firstToken = ct;
          return pending.Task;
        }
        return Task.FromResult(
          MileageComponentResponses.Ok(
            MileageComponentResponses.Breakdown(
              secondId,
              MileageComponentResponses.Row(secondId, "home")
            )
          )
        );
      }
    );
    Authorize(context);
    var component = context.Render<DispatchMileageBreakdown>(p =>
      p.Add(x => x.DispatchId, firstId)
    );
    var firstOpen = Button(component, "Mileage breakdown").ClickAsync(new());
    component.WaitForAssertion(() => Assert.Equal(1, reads));
    component.Render(p => p.Add(x => x.DispatchId, secondId));
    Assert.True(firstToken.IsCancellationRequested);
    await Button(component, "Mileage breakdown").ClickAsync(new());
    component.WaitForElement(".dispatch-mileage__movement");
    pending.SetResult(
      MileageComponentResponses.Ok(
        MileageComponentResponses.Breakdown(
          firstId,
          MileageComponentResponses.Row(firstId, "yard-return")
        )
      )
    );
    await firstOpen;
    var movement = component.Find(".dispatch-mileage__movement").TextContent;
    Assert.Contains("Home", movement);
    Assert.DoesNotContain("Yard return", movement);
    Assert.Equal(2, reads);
  }

  [Fact]
  public async Task CancelledExceptionDoesNotWrite()
  {
    var id = Guid.NewGuid();
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        return Task.FromResult(
          MileageComponentResponses.Ok(
            MileageComponentResponses.Breakdown(
              id,
              MileageComponentResponses.Row(id, editable: true)
            )
          )
        );
      }
    );
    Authorize(context);
    var component = context.Render<DispatchMileageBreakdown>(p =>
      p.Add(x => x.DispatchId, id)
    );
    await Button(component, "Mileage breakdown").ClickAsync(new());
    await Button(component, "Change allocation").ClickAsync(new());
    component.Find("select").Change("previous");
    component.Find("textarea").Change("Draft only.");
    await Button(component, "Cancel").ClickAsync(new());
    Assert.Empty(component.FindAll("form"));
  }

  [Fact]
  public async Task AllocationIsSingleWhileBusyAndReloadsServerTotals()
  {
    var id = Guid.NewGuid();
    var row = MileageComponentResponses.Row(id, editable: true);
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var reads = 0;
    var writes = new List<MileageAllocationUpdate>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(
            MileageComponentResponses.Breakdown(
              id,
              row,
              ++reads == 1 ? 120 : 145
            )
          );
        Assert.Equal(
          $"/api/mileage/movements/{row.MovementId}/allocation",
          request.RequestUri!.AbsolutePath
        );
        writes.Add(
          (
            await request.Content!.ReadFromJsonAsync<MileageAllocationUpdate>(
              ct
            )
          )!
        );
        return await pending.Task;
      }
    );
    Authorize(context);
    var component = context.Render<DispatchMileageBreakdown>(p =>
      p.Add(x => x.DispatchId, id)
    );
    await Button(component, "Mileage breakdown").ClickAsync(new());
    await Button(component, "Change allocation").ClickAsync(new());
    component.Find("select").Change("previous");
    component.Find("textarea").Change("Return belongs to the prior load.");
    var save = component.Find("form").SubmitAsync(EventArgs.Empty);
    component.WaitForAssertion(() => Assert.Single(writes));
    Assert.True(component.Find("fieldset").HasAttribute("disabled"));
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Single(writes);
    Assert.Equal(
      new(4, "previous", "Return belongs to the prior load."),
      writes[0]
    );
    pending.SetResult(MileageComponentResponses.Ok(row with { Revision = 5 }));
    await save;
    component.WaitForElement(".dispatch-mileage__saved");
    Assert.Equal(2, reads);
    Assert.Contains(
      "145",
      component.Find(".dispatch-mileage__basis").TextContent
    );
  }

  [Fact]
  public async Task LateAllocationReplyDoesNotMarkAnotherLoadSaved()
  {
    var firstId = Guid.NewGuid();
    var secondId = Guid.NewGuid();
    var firstRow = MileageComponentResponses.Row(firstId, editable: true);
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var writes = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method == HttpMethod.Put)
        {
          writes++;
          return pending.Task;
        }
        var id = request.RequestUri!.AbsolutePath.Contains(firstId.ToString())
          ? firstId
          : secondId;
        return Task.FromResult(
          MileageComponentResponses.Ok(
            MileageComponentResponses.Breakdown(
              id,
              id == firstId
                ? firstRow
                : MileageComponentResponses.Row(secondId, "home")
            )
          )
        );
      }
    );
    Authorize(context);
    var component = context.Render<DispatchMileageBreakdown>(p =>
      p.Add(x => x.DispatchId, firstId)
    );
    await Button(component, "Mileage breakdown").ClickAsync(new());
    await Button(component, "Change allocation").ClickAsync(new());
    component.Find("textarea").Change("Use the current policy.");
    var save = component.Find("form").SubmitAsync(EventArgs.Empty);
    component.WaitForAssertion(() => Assert.Equal(1, writes));
    component.Render(p => p.Add(x => x.DispatchId, secondId));
    await Button(component, "Mileage breakdown").ClickAsync(new());
    pending.SetResult(MileageComponentResponses.Ok(firstRow));
    await save;
    Assert.Contains(
      "Home",
      component.Find(".dispatch-mileage__movement").TextContent
    );
    Assert.Empty(component.FindAll(".dispatch-mileage__saved"));
  }

  private static void Authorize(ClientComponentContext context)
  {
    var auth = context.AddAuthorization();
    auth.SetAuthorized("Dispatcher");
    auth.SetRoles("Dispatch");
  }

  private static IElement Button(
    IRenderedComponent<DispatchMileageBreakdown> component,
    string label
  ) => component.FindAll("button").Single(x => x.TextContent.Trim() == label);
}
