using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchCreateTests
{
  [Fact]
  public async Task OpenAndEditingDoNotWriteAndUncertainSaveRetriesExactRequest()
  {
    var writes = new List<CreateDispatchRequest>();
    var loadId = Guid.NewGuid();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/dispatch", request.RequestUri!.AbsolutePath);
        writes.Add(
          (await request.Content!.ReadFromJsonAsync<CreateDispatchRequest>(ct))!
        );
        if (writes.Count == 1)
          throw new HttpRequestException("Connection lost after submission.");
        return MileageComponentResponses.Ok(
          new DispatchWorkspaceResponse { Load = new() { Id = loadId } }
        );
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var page = context.Render<DispatchCreate>();
    Assert.Empty(writes);
    Assert.Equal(
      new[] { "Stops", "Details" },
      page.FindAll(".stop-workspace__mobile-tabs button")
        .Select(x => x.TextContent)
    );
    Assert.Single(page.FindAll(".stop-workspace--single"));
    await page.Find("#new-order").ChangeAsync("Native order");
    Assert.Empty(writes);
    await page.Find(".dispatch-details__save button").ClickAsync(new());
    Assert.Single(writes);
    Assert.True(page.Find("fieldset").HasAttribute("disabled"));
    Assert.Equal(
      "Retry save",
      page.Find(".dispatch-details__save button").TextContent.Trim()
    );
    await page.Find(".dispatch-details__save button").ClickAsync(new());
    Assert.Equal(2, writes.Count);
    Assert.Equal(
      JsonSerializer.Serialize(writes[0]),
      JsonSerializer.Serialize(writes[1])
    );
    Assert.NotEqual(Guid.Empty, writes[0].IdempotencyKey);
    Assert.Equal("Native order", writes[0].OrderNumber);
    Assert.Equal(
      new[] { "Pick Up", "Delivery" },
      writes[0].Stops.Select(x => x.Job)
    );
    Assert.EndsWith(
      $"/dispatch/{loadId}",
      context.Services.GetRequiredService<NavigationManager>().Uri
    );
  }

  [Fact]
  public async Task RejectedInputRemainsEditableAndNavigationProtectsDraft()
  {
    using var context = new ClientComponentContext(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.BadRequest)
          {
            Content = JsonContent.Create(
              new { success = false, errors = new[] { "Verify locations." } }
            ),
          }
        )
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var page = context.Render<DispatchCreate>();
    await page.Find("#new-order").ChangeAsync("Keep this draft");
    await page.Find(".dispatch-details__save button").ClickAsync(new());
    Assert.False(page.Find("fieldset").HasAttribute("disabled"));
    Assert.Equal(
      "Keep this draft",
      page.Find("#new-order").GetAttribute("value")
    );
    Assert.Contains("Verify locations.", page.Find("[role=alert]").TextContent);
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    await page.InvokeAsync(() => navigation.NavigateTo("/dispatch"));
    Assert.Single(page.FindAll("[role=alertdialog]"));
    await page.Find("[role=alertdialog] .btn").ClickAsync(new());
    Assert.Empty(page.FindAll("[role=alertdialog]"));
  }
}
