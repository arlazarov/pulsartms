using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Shared.Dispatch.StopOperationEditor;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class StopOperationEditorTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ExplicitConfirmationSendsExactIdentityWithoutChangingImportedJob(
    bool inline
  )
  {
    StopOperationUpdate? body = null;
    string? path = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        path = request.RequestUri!.AbsolutePath;
        body = await request.Content!.ReadFromJsonAsync<StopOperationUpdate>(
          ct
        );
        return new(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<StopOperationState>
            {
              Success = true,
              Response = new(body!.Action, body.StateAfter, 1, DateTime.UtcNow),
            }
          ),
        };
      }
    );
    var loadId = Guid.NewGuid();
    var stop = new DispatchStopResponse
    {
      Id = Guid.NewGuid(),
      Job = "Pick Up",
      CompletionIdentity = "source-stop",
      StateAfter = "Loaded",
    };
    var changes = 0;
    var cut = context.Render<StopOperationEditor>(p =>
      p.Add(x => x.DispatchId, loadId)
        .Add(x => x.Inline, inline)
        .Add(x => x.Stop, stop)
        .Add(x => x.Changed, () => changes++)
    );
    if (!inline)
      await cut.Find("button").ClickAsync(new MouseEventArgs());
    Assert.Null(body);
    cut.FindAll("select")[0].Change("Driver start");
    Assert.Single(cut.FindAll("select"));
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Equal($"/api/dispatch/{loadId}/stops/{stop.Id}/operation", path);
    Assert.Equal(
      new StopOperationUpdate("Driver start", "No truck", 0, "source-stop"),
      body
    );
    Assert.Equal("Pick Up", stop.Job);
    Assert.Equal(1, changes);
    Assert.Equal(1, stop.OperationRevision);
  }

  [Fact]
  public async Task InlineFieldsAreImmediatelyAvailableAndCancelRestoresSavedState()
  {
    var requests = 0;
    var dirty = false;
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        requests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
      }
    );
    var cut = context.Render<StopOperationEditor>(p =>
      p.Add(x => x.Inline, true)
        .Add(
          x => x.Stop,
          new DispatchStopResponse
          {
            Id = Guid.NewGuid(),
            Job = "Delivery",
            StateAfter = "Loaded",
          }
        )
        .Add(x => x.DraftChanged, (bool value) => dirty = value)
    );
    Assert.Equal("Drop Off", cut.FindAll("select")[0].GetAttribute("value"));
    Assert.Empty(cut.FindAll("button"));
    cut.FindAll("select")[0].Change("Driver start");
    Assert.True(dirty);
    await cut.FindAll("button")
      .Single(x => x.TextContent == "Cancel")
      .ClickAsync(new());
    Assert.False(dirty);
    Assert.Single(cut.FindAll("select"));
    Assert.Empty(cut.FindAll("button"));
    Assert.Equal(0, requests);
  }

  [Fact]
  public void InlineSourceRefreshUpdatesIdleFieldsButRetainsAnActiveDraft()
  {
    using var context = new ClientComponentContext(
      (request, ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
    );
    var stop = new DispatchStopResponse
    {
      Id = Guid.NewGuid(),
      Job = "Pick Up",
      StateAfter = "Loaded",
    };
    var cut = context.Render<StopOperationEditor>(p =>
      p.Add(x => x.Inline, true).Add(x => x.Stop, stop)
    );
    stop.Job = "Delivery";
    stop.StateAfter = "Empty";
    cut.Render();
    Assert.Equal("Drop Off", cut.FindAll("select")[0].GetAttribute("value"));
    Assert.Single(cut.FindAll("select"));
    cut.FindAll("select")[0].Change("Driver start");
    stop.Job = "Pick Up";
    cut.Render();
    Assert.Equal(
      "Driver start",
      cut.FindAll("select")[0].GetAttribute("value")
    );
  }

  [Fact]
  public async Task CancelAndChangingSelectedStopDiscardTheDraftWithoutWrites()
  {
    var requests = 0;
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        requests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
      }
    );
    var cut = context.Render<StopOperationEditor>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid())
        .Add(
          x => x.Stop,
          new DispatchStopResponse { Id = Guid.NewGuid(), Job = "Pick Up" }
        )
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    await cut.FindAll("button")
      .Single(x => x.TextContent == "Cancel")
      .ClickAsync(new MouseEventArgs());
    Assert.Empty(cut.FindAll("select"));
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    cut.Render(p =>
      p.Add(x => x.Stop, new DispatchStopResponse { Id = Guid.NewGuid() })
    );
    Assert.Empty(cut.FindAll("select"));
    Assert.Equal(0, requests);
  }

  [Fact]
  public async Task DeliveryEditorOmitsTrailerStateControl()
  {
    using var context = new ClientComponentContext(
      (request, ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
    );
    var cut = context.Render<StopOperationEditor>(p =>
      p.Add(
        x => x.Stop,
        new DispatchStopResponse { Id = Guid.NewGuid(), Job = "Drop Off" }
      )
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    Assert.Single(cut.FindAll("select"));
    Assert.DoesNotContain("Trailer after stop", cut.Markup);
  }

  [Fact]
  public void TableKeepsDriverStartOutOfDeliveryColumn()
  {
    using var context = new ClientComponentContext(
      (request, ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
    );
    var start = new DispatchStopResponse
    {
      Id = Guid.NewGuid(),
      Job = "Driver start",
      DriverOnly = true,
      StateAfter = "No truck",
      City = "Start city",
      Sequence = 1,
    };
    var delivery = new DispatchStopResponse
    {
      Id = Guid.NewGuid(),
      Job = "Drop Off",
      City = "Delivery city",
      Sequence = 2,
    };
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      Stops = [start, delivery],
    };
    var cut = context.Render<DispatchTable>(p =>
      p.Add(
        x => x.Trucks,
        new[]
        {
          new TruckDispatchBoardResponse
          {
            TruckId = Guid.NewGuid(),
            Dispatches = [load],
          },
        }
      )
    );
    Assert.Contains("Pickup / other stops", cut.Find("thead").TextContent);
    Assert.Contains("Driver start", cut.Find("td.is-pickup").TextContent);
    Assert.DoesNotContain("Start city", cut.Find("td.is-delivery").TextContent);
    Assert.Contains("Delivery city", cut.Find("td.is-delivery").TextContent);
  }
}
