using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Shipments;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using ShipmentPage = Client.Pages.Shipments.Shipments;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class ShipmentPageTests
{
  [Fact]
  public async Task DomesticDraftHasNoCustomsFieldsAndRetainsBillOfLading()
  {
    SaveShipment? saved = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Put)
        {
          saved = await request.Content!.ReadFromJsonAsync<SaveShipment>(ct);
          return MileageComponentResponses.Ok(saved!.Shipment);
        }
        return request.RequestUri!.AbsolutePath.EndsWith("/stops")
          ? MileageComponentResponses.Ok(new List<ShipmentStopOption>())
          : MileageComponentResponses.Ok(new List<Shipment>());
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var page = context.Render<ShipmentPage>(p =>
      p.Add(x => x.LoadId, Guid.NewGuid())
    );
    await Click(page, "Add shipment");
    Assert.DoesNotContain("PARS number", page.Markup);
    var bol = page.FindAll("label")
      .Single(x => x.TextContent.Contains("Bill of lading"));
    bol.QuerySelector("input")!.Input("0012345");
    await Click(page, "Save draft");
    page.WaitForAssertion(() => Assert.NotNull(saved));
    Assert.Equal("0012345", saved!.Shipment.BillOfLading);
  }

  [Fact]
  public async Task UncertainSaveRetriesTheExactPayloadAndIdentity()
  {
    var writes = new List<SaveShipment>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Put)
        {
          var write = (
            await request.Content!.ReadFromJsonAsync<SaveShipment>(ct)
          )!;
          writes.Add(write);
          return writes.Count == 1
            ? MileageComponentResponses.Error<Shipment>(
              HttpStatusCode.ServiceUnavailable,
              "Try again"
            )
            : MileageComponentResponses.Ok(write.Shipment);
        }
        return request.RequestUri!.AbsolutePath.EndsWith("/stops")
          ? MileageComponentResponses.Ok(new List<ShipmentStopOption>())
          : MileageComponentResponses.Ok(new List<Shipment>());
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var page = context.Render<ShipmentPage>(p =>
      p.Add(x => x.LoadId, Guid.NewGuid())
    );
    await Click(page, "Add shipment");
    await Click(page, "Save draft");
    await Click(page, "Retry save");
    page.WaitForAssertion(() => Assert.Equal(2, writes.Count));
    Assert.Equal(writes[0].RequestId, writes[1].RequestId);
    Assert.Equal(writes[0].Shipment.Id, writes[1].Shipment.Id);
  }

  private static Task Click(
    IRenderedComponent<ShipmentPage> page,
    string text
  ) =>
    page.WaitForElement("button") is not null
      ? page.FindAll("button")
        .Single(x => x.TextContent.Trim() == text)
        .ClickAsync(new MouseEventArgs())
      : Task.CompletedTask;
}
