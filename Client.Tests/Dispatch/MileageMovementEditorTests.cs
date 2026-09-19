using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using Client.Models.DTO.Mileage;
using Client.Pages.Dispatch;
using Client.Tests.Support;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class MileageMovementEditorTests
{
  [Fact]
  public async Task UncertainWriteRetriesSamePayloadWithoutFleetReload()
  {
    var dispatch = Guid.NewGuid();
    var truck = Guid.NewGuid();
    var reads = 0;
    var writes = new List<RecordMovementRequest>();
    MileageMovementRow? saved = null;
    var row = MileageComponentResponses.Row(dispatch, "home", true);
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
        {
          reads++;
          return MileageComponentResponses.Fleet(
            request.RequestUri!.AbsolutePath,
            truck
          );
        }
        writes.Add(
          (await request.Content!.ReadFromJsonAsync<RecordMovementRequest>(ct))!
        );
        return writes.Count == 1
          ? MileageComponentResponses.Error<MileageMovementRow>(
            HttpStatusCode.ServiceUnavailable,
            "Result unknown."
          )
          : MileageComponentResponses.Ok(row);
      }
    );
    var component = context.Render<MileageMovementEditor>(p =>
      p.Add(x => x.DispatchId, dispatch)
        .Add(x => x.DefaultTruckId, truck)
        .Add(x => x.Saved, value => saved = value)
    );
    component.WaitForElement("form");
    Assert.Equal(3, reads);
    AssertBlankSelection(component, "-driver");
    AssertBlankSelection(component, "-trailer");
    component.Find("input[id$='-from']").Change("Customer yard");
    component.Find("input[id$='-to']").Change("Home");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Contains("Result unknown.", component.Markup);
    Assert.True(component.Find("fieldset").HasAttribute("disabled"));
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(2, writes.Count);
    Assert.Equal(writes[0], writes[1]);
    Assert.NotEqual(Guid.Empty, writes[0].IdempotencyKey);
    Assert.Equal(dispatch, writes[0].PreviousDispatchId);
    Assert.Null(writes[0].NextDispatchId);
    Assert.Null(writes[0].DriverId);
    Assert.Null(writes[0].TrailerId);
    Assert.Null(writes[0].StartedAt);
    Assert.Null(writes[0].EndedAt);
    Assert.Null(writes[0].ExecutionLegId);
    Assert.Equal(row, saved);
    Assert.Equal(3, reads);
  }

  [Fact]
  public async Task PartialActualTimeNeverWritesAndCancelDiscardsDraft()
  {
    var truck = Guid.NewGuid();
    var cancelled = false;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        return Task.FromResult(
          MileageComponentResponses.Fleet(
            request.RequestUri!.AbsolutePath,
            truck
          )
        );
      }
    );
    var component = context.Render<MileageMovementEditor>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid())
        .Add(x => x.DefaultTruckId, truck)
        .Add(x => x.Cancelled, () => cancelled = true)
    );
    component.WaitForElement("form");
    component.Find("input[id$='-from']").Change("Yard");
    component.Find("input[id$='-to']").Change("Home");
    component.Find("input[id$='-start-date']").Change("2026-09-12");
    component.Find("input[id$='-start-time']").Change("01:00 PM");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Contains("Enter both actual dates", component.Markup);
    await component
      .FindAll("button")
      .Single(x => x.TextContent.Trim() == "Cancel")
      .ClickAsync(new());
    Assert.True(cancelled);
  }

  [Fact]
  public async Task BusyRecordIsSingleAndDisposedResponseCannotPublish()
  {
    var truck = Guid.NewGuid();
    var dispatch = Guid.NewGuid();
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var writes = 0;
    var published = false;
    CancellationToken writeToken = default;
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Task.FromResult(
            MileageComponentResponses.Fleet(
              request.RequestUri!.AbsolutePath,
              truck
            )
          );
        writes++;
        writeToken = ct;
        return pending.Task;
      }
    );
    var component = context.Render<MileageMovementEditor>(p =>
      p.Add(x => x.DispatchId, dispatch)
        .Add(x => x.DefaultTruckId, truck)
        .Add(x => x.Saved, _ => published = true)
    );
    component.WaitForElement("form");
    component.Find("input[id$='-from']").Change("Customer");
    component.Find("input[id$='-to']").Change("Yard");
    var save = component.Find("form").SubmitAsync(EventArgs.Empty);
    component.WaitForAssertion(() => Assert.Equal(1, writes));
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(1, writes);
    await context.DisposeComponentsAsync();
    Assert.True(writeToken.IsCancellationRequested);
    pending.SetResult(
      MileageComponentResponses.Ok(
        MileageComponentResponses.Row(dispatch, "yard-return", true)
      )
    );
    await save;
    Assert.False(published);
  }

  private static void AssertBlankSelection(
    IRenderedComponent<MileageMovementEditor> component,
    string suffix
  )
  {
    var select = (IHtmlSelectElement)component.Find($"select[id$='{suffix}']");
    Assert.True(string.IsNullOrEmpty(select.Value));
    Assert.Equal("", select.Options[0].Value);
  }
}
