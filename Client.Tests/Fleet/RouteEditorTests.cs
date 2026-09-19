using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Shared.Routing.RouteEditor;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Fleet;

[Trait("Category", "Routing")]
[Trait("Kind", "Component")]
public sealed class RouteEditorTests
{
  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  public void CurrentLocationPreviewShowsOnlyAvailableOptionsAndLabelsRemainingDistances(
    int count
  )
  {
    var preview = Preview() with
    {
      OriginUpdatedAt = DateTime.UtcNow,
      SavedRoute = new(90, 3200),
    };
    preview.Stops[0] = new(
      Guid.Empty,
      "Current truck location",
      "",
      0,
      new(41, -80)
    )
    {
      Job = "GPS start",
    };
    preview.Options.RemoveRange(count, preview.Options.Count - count);
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(Ok(preview))
    );
    var cut = context.Render<RouteEditor>(p =>
      p.Add(x => x.DispatchId, preview.DispatchId)
    );
    cut.WaitForAssertion(
      () => Assert.Equal(count, cut.FindAll(".route-editor__option").Count)
    );
    Assert.Contains("From current truck location", cut.Markup);
    Assert.Contains("Remaining stops only", cut.Markup);
    Assert.Contains("Saved remaining route", cut.Markup);
    Assert.Equal(
      count == 1,
      cut.Markup.Contains("Only one suitable route was returned.")
    );
  }

  [Fact]
  public async Task ViaOrderingUsesItsOwnLegEvenWhenOtherLegsWereEditedBetweenPoints()
  {
    var preview = Preview();
    var middle = new PlanStop(Guid.NewGuid(), "Middle", "", 2, new(42, -80));
    preview.Stops.Insert(1, middle);
    var end = preview.Stops[^1].Id;
    preview.ViaPoints.AddRange(
      [
        new(Guid.NewGuid(), end, "A1", new(42.1, -80)),
        new(Guid.NewGuid(), middle.Id, "B", new(41, -80)),
        new(Guid.NewGuid(), end, "A2", new(42.2, -80)),
      ]
    );
    RouteChoiceRequest? edited = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        var body = (
          await request.Content!.ReadFromJsonAsync<RouteChoiceRequest>(ct)
        )!;
        if (!body.UseSavedVia)
          edited = body;
        return Ok(
          body.UseSavedVia
            ? preview
            : preview with
            {
              ViaPoints = body.ViaPoints,
            }
        );
      }
    );
    var cut = context.Render<RouteEditor>(p =>
      p.Add(x => x.DispatchId, preview.DispatchId)
    );
    cut.WaitForAssertion(
      () => Assert.Equal(2, cut.FindAll(".route-editor__option").Count)
    );
    await cut.FindAll(".route-editor__tabs button")[1]
      .ClickAsync(new MouseEventArgs());
    Assert.True(
      cut.Find("button[aria-label='Move B earlier']").HasAttribute("disabled")
    );
    await cut.Find("button[aria-label='Move A2 earlier']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      new[] { "A2", "A1" },
      edited!.ViaPoints.Where(x => x.BeforeStopId == end).Select(x => x.Label)
    );
    Assert.Equal(3, cut.FindAll(".route-editor__number").Count);
  }

  private static RouteChoicePreview Preview() =>
    new(
      Guid.NewGuid(),
      Guid.NewGuid(),
      Guid.NewGuid(),
      1383,
      4,
      DateTime.UtcNow.AddMinutes(10),
      [
        new(Guid.NewGuid(), "Pickup", "", 1, new(40, -80)),
        new(Guid.NewGuid(), "Delivery", "", 2, new(43, -80)),
      ],
      [],
      [
        new(1, new() { Miles = 100, Seconds = 3600 }, 0, 0),
        new(2, new() { Miles = 110, Seconds = 4200 }, 10, 600),
      ],
      null
    );

  private static HttpResponseMessage Ok<T>(T value) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<T> { Success = true, Response = value }
      ),
    };

  [Fact]
  public void RejectedPreviewShowsThePlanningError()
  {
    const string error = "The delivery address needs confirmation.";
    using var context = new ClientComponentContext(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.BadRequest)
          {
            Content = JsonContent.Create(
              new RequestResponseDTO<RouteChoicePreview>
              {
                Success = false,
                Errors = [error],
              }
            ),
          }
        )
    );

    var cut = context.Render<RouteEditor>(p =>
      p.Add(x => x.DispatchId, Guid.NewGuid())
    );

    cut.WaitForAssertion(() => Assert.Contains(error, cut.Markup));
  }

  [Fact]
  public async Task SelectionOnlyPreviewsUntilExplicitSaveAndSendsServerIdentity()
  {
    var preview = Preview();
    RouteChoiceSave? saved = null;
    RouteEditorMap? map = null;
    var saves = 0;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Put)
        {
          saved = await request.Content!.ReadFromJsonAsync<RouteChoiceSave>(ct);
          return Ok(5L);
        }
        return Ok(preview);
      }
    );
    var cut = context.Render<RouteEditor>(p =>
      p.Add(x => x.DispatchId, preview.DispatchId)
        .Add(x => x.Session, Guid.NewGuid())
        .Add(x => x.MapChanged, value => map = value)
        .Add(x => x.Saved, () => saves++)
    );
    cut.WaitForAssertion(
      () => Assert.Equal(2, cut.FindAll(".route-editor__option").Count)
    );
    await cut.FindAll(".route-editor__option")[1]
      .ClickAsync(new MouseEventArgs());
    Assert.Null(saved);
    Assert.Equal(2, map!.Selected);
    await cut.Find(".route-editor__actions .btn--primary")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(new(preview.Id, 2, 4), saved);
    Assert.Equal(1, saves);
  }

  [Fact]
  public async Task MapViaRecalculatesWithoutAddingLoadStopAndCancelDoesNotSave()
  {
    var preview = Preview();
    List<RouteChoiceRequest> requests = [];
    var puts = 0;
    var closes = 0;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Put)
        {
          puts++;
          return Ok(5L);
        }
        var body = (
          await request.Content!.ReadFromJsonAsync<RouteChoiceRequest>(ct)
        )!;
        requests.Add(body);
        return Ok(preview with { ViaPoints = body.ViaPoints });
      }
    );
    var cut = context.Render<RouteEditor>(p =>
      p.Add(x => x.DispatchId, preview.DispatchId)
        .Add(x => x.Closed, () => closes++)
    );
    cut.WaitForAssertion(
      () => Assert.Equal(2, cut.FindAll(".route-editor__option").Count)
    );
    await cut.FindAll(".route-editor__tabs button")[1]
      .ClickAsync(new MouseEventArgs());
    await cut.Find("input").InputAsync("Columbia, SC");
    cut.WaitForAssertion(
      () =>
        Assert.False(
          cut.FindAll(".route-editor__add button")[0].HasAttribute("disabled")
        )
    );
    await cut.InvokeAsync(() => cut.Instance.PointChanged(null, 0, 41, -79));
    Assert.Equal(2, requests.Count);
    Assert.False(requests[1].Alternatives);
    Assert.Equal(
      preview.Stops[1].Id,
      Assert.Single(requests[1].ViaPoints).BeforeStopId
    );
    Assert.Equal(3, cut.FindAll(".route-editor__itinerary li").Count);
    await cut.Find("button[aria-label='Close route editor']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(1, closes);
    Assert.Equal(0, puts);
  }

  [Fact]
  public async Task RejectedSaveRequiresNewPreviewWithoutLeakingTechnicalError()
  {
    var preview = Preview();
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.Method == HttpMethod.Put
            ? new(HttpStatusCode.Conflict)
            {
              Content = JsonContent.Create(
                new RequestResponseDTO<long>
                {
                  Success = false,
                  Errors = ["internal fixture detail"],
                }
              ),
            }
            : Ok(preview)
        )
    );
    var cut = context.Render<RouteEditor>(p =>
      p.Add(x => x.DispatchId, preview.DispatchId)
    );
    cut.WaitForAssertion(
      () => Assert.Equal(2, cut.FindAll(".route-editor__option").Count)
    );
    await cut.Find(".route-editor__actions .btn--primary")
      .ClickAsync(new MouseEventArgs());
    Assert.Contains("Preview route", cut.Markup);
    Assert.DoesNotContain("internal fixture detail", cut.Markup);
    Assert.DoesNotContain("Use this route", cut.Markup);
  }
}
