using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Border;
using Client.Models.DTO.Mileage;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using BorderPage = Client.Pages.Border.Border;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class BorderPageTests
{
  [Fact]
  public async Task TabsRetainDraftAndUncertainSaveRetainsRequestIdentity()
  {
    var writes = new List<SaveBorderCrossing>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Put)
        {
          var write = (
            await request.Content!.ReadFromJsonAsync<SaveBorderCrossing>(ct)
          )!;
          writes.Add(write);
          return writes.Count == 1
            ? MileageComponentResponses.Error<BorderCrossing>(
              HttpStatusCode.ServiceUnavailable,
              "Unconfirmed"
            )
            : MileageComponentResponses.Ok(write.Crossing);
        }
        if (path.EndsWith("/drivers"))
          return MileageComponentResponses.Ok(
            new MileageFleetList<MileageDriverOption>(0, [])
          );
        if (path.StartsWith("/api/fleet/"))
          return MileageComponentResponses.Ok(
            new MileageFleetList<MileageUnitOption>(0, [])
          );
        return MileageComponentResponses.Ok(new List<BorderSummary>());
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var page = context.Render<BorderPage>();
    page.WaitForAssertion(() => Assert.Contains("New crossing", page.Markup));
    await Click(page, "New crossing");
    await page.InvokeAsync(
      () =>
        page.FindAll("label")
          .Single(x => x.TextContent.Contains("Crossing reference"))
          .QuerySelector("input")!
          .Input("SYNTHETIC-CROSSING")
    );
    await Click(page, "Shipments");
    await Click(page, "Crew & equipment");
    await Click(page, "Crossing");
    Assert.Equal(
      "SYNTHETIC-CROSSING",
      page.FindAll("label")
        .Single(x => x.TextContent.Contains("Crossing reference"))
        .QuerySelector("input")!
        .GetAttribute("value")
    );
    await Click(page, "Save draft");
    await Click(page, "Retry save");
    page.WaitForAssertion(() => Assert.Equal(2, writes.Count));
    Assert.Equal(writes[0].RequestId, writes[1].RequestId);
    Assert.Equal("SYNTHETIC-CROSSING", writes[1].Crossing.Reference);
  }

  private static Task Click(IRenderedComponent<BorderPage> page, string text) =>
    page.InvokeAsync(
      () =>
        page.FindAll("button")
          .Single(x => x.TextContent.Trim() == text)
          .ClickAsync(new MouseEventArgs())
    );
}
