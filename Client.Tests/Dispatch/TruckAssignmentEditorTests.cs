using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch.TruckAssignmentEditor;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class TruckAssignmentEditorTests
{
  [Fact]
  public async Task OpeningAndCancelDoNotWriteAndConfirmationSendsExactAnchor()
  {
    TruckAssignmentUpdate? body = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        body = await request.Content!.ReadFromJsonAsync<TruckAssignmentUpdate>(
          ct
        );
        return new(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<TruckAssignmentState>
            {
              Success = true,
              Response = new(
                Guid.NewGuid(),
                body!.FromStopId,
                1,
                DateTime.UtcNow
              ),
            }
          ),
        };
      }
    );
    var stop = new DispatchStopResponse
    {
      Id = Guid.NewGuid(),
      Sequence = 2,
      City = "Webster",
      CompletionIdentity = "source-identity",
    };
    var load = new DispatchResponse { Id = Guid.NewGuid(), Stops = [stop] };
    var changed = 0;
    var cut = context.Render<TruckAssignmentEditor>(p =>
      p.Add(x => x.Load, load).Add(x => x.Changed, () => changed++)
    );
    await cut.Find("button").ClickAsync(new MouseEventArgs());
    Assert.Null(body);
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Null(body);
    Assert.Contains("Choose the truck", cut.Markup);
    cut.Find("input").Change("54777");
    cut.Find("select").Change(stop.Id.ToString());
    await cut.Find(".btn--primary").ClickAsync(new MouseEventArgs());
    Assert.Equal("54777", body!.TruckNumber);
    Assert.Equal(stop.Id, body.FromStopId);
    Assert.Equal("source-identity", body.StopIdentity);
    Assert.Equal(1, changed);
    Assert.Equal(stop.Id, load.PlanningFromStopId);
  }
}
