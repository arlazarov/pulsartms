using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.DriverGroups;
using Client.Models.DTO.Messaging;
using Client.Models.DTO.Mileage;
using Client.Pages.Settings;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MessagesPage = Client.Pages.Messages.Messages;

namespace Client.Tests.Fleet;

// One choice of driver group for every page: choosing one on a page saves
// it for the dispatcher and that page reads its list again. Groups are
// made, filled and removed in personal settings; a refused save keeps the
// draft and nothing is removed without confirming.
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class DriverGroupComponentTests
{
  private static readonly Guid West = Guid.NewGuid();
  private static readonly Guid Driver = Guid.NewGuid();

  [Fact]
  public async Task ChoosingAGroupSavesItAndThePageReadsAgain()
  {
    var api = new Api();
    await using var context = Context(api);
    var page = context.Render<MessagesPage>();
    page.WaitForAssertion(
      () =>
        Assert.Contains(
          "West (1)",
          page.Find("#messages-driver-group").TextContent
        )
    );
    var inboxReads = api.InboxReads;

    await page.Find("#messages-driver-group")
      .ChangeAsync(new() { Value = West.ToString() });

    page.WaitForAssertion(() => Assert.True(api.InboxReads > inboxReads));
    Assert.Equal([West], api.Selections);
    Assert.Equal(
      West,
      context.Services.GetRequiredService<ChosenDriverGroup>().View!.Selected
    );
  }

  [Fact]
  public async Task AGroupIsMadeWithItsDriversAndARefusalKeepsTheDraft()
  {
    var api = new Api { RefuseFirstSave = true };
    await using var context = Context(api);
    var settings = context.Render<DriverGroupSettings>();
    await settings
      .WaitForElement("button:contains('New group')")
      .ClickAsync(new());

    settings.Find("#driver-group-name").Input("Local");
    await settings
      .Find("input[type=checkbox]")
      .ChangeAsync(new() { Value = true });
    await settings.Find("form").SubmitAsync();
    settings.WaitForAssertion(
      () => Assert.Contains("already have", settings.Markup)
    );
    Assert.Equal(
      "Local",
      settings.Find("#driver-group-name").GetAttribute("value")
    );

    await settings.Find("form").SubmitAsync();
    settings.WaitForAssertion(
      () => Assert.Contains("Group saved.", settings.Markup)
    );
    var saved = api.Saved[^1];
    Assert.Equal(("Local", 0L), (saved.Name, saved.Revision));
    Assert.Equal([Driver], saved.Drivers);
  }

  [Fact]
  public async Task AGroupIsRemovedOnlyAfterConfirming()
  {
    var api = new Api();
    await using var context = Context(api);
    var settings = context.Render<DriverGroupSettings>();
    await settings
      .WaitForElement("button:contains('Remove')")
      .ClickAsync(new());
    Assert.Empty(api.Removed);

    await settings.Find("button:contains('Remove group')").ClickAsync(new());

    settings.WaitForAssertion(() => Assert.Equal([West], api.Removed));
  }

  private static ClientComponentContext Context(Api api)
  {
    var context = new ClientComponentContext(api.SendAsync);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.Services.AddSingleton<TokenStorageService>();
    context.Services.AddSingleton<MessagingSignals>();
    return context;
  }

  private sealed class Api
  {
    public bool RefuseFirstSave { get; init; }
    public int InboxReads { get; private set; }
    public List<Guid?> Selections { get; } = [];
    public List<DriverGroupRequest> Saved { get; } = [];
    public List<Guid> Removed { get; } = [];

    public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      var path = request.RequestUri!.AbsolutePath;
      if (path == "/api/driver-groups" && request.Method == HttpMethod.Get)
        return Ok(
          new DriverGroupsView(
            Selections.LastOrDefault(),
            Removed.Count > 0 ? [] : [new(West, "West", 1, [Driver])]
          )
        );
      if (path == "/api/driver-groups/selection")
      {
        Selections.Add(
          (
            await request.Content!.ReadFromJsonAsync<DriverGroupSelection>(ct)
          )!.GroupId
        );
        return Ok(true);
      }
      if (path == "/api/driver-groups" && request.Method == HttpMethod.Post)
      {
        Saved.Add(
          (await request.Content!.ReadFromJsonAsync<DriverGroupRequest>(ct))!
        );
        return RefuseFirstSave && Saved.Count == 1
          ? new(HttpStatusCode.Conflict)
          {
            Content = JsonContent.Create(
              new RequestResponseDTO<DriverGroupView>
              {
                Success = false,
                Errors = ["You already have a group with this name."],
              }
            ),
          }
          : Ok(new DriverGroupView(Guid.NewGuid(), "Local", 1, [Driver]));
      }
      if (
        path.StartsWith("/api/driver-groups/")
        && request.Method == HttpMethod.Delete
      )
      {
        Removed.Add(Guid.Parse(path["/api/driver-groups/".Length..]));
        return Ok(true);
      }
      if (path == "/api/fleet/drivers")
        return Ok(
          new MileageFleetList<MileageDriverOption>(
            1,
            [new(Driver, "West One", true)]
          )
        );
      if (path == "/api/messaging/inbox")
      {
        InboxReads++;
        return Ok(new InboxView([], false));
      }
      if (path == "/api/messaging/templates")
        return Ok<IReadOnlyList<MessageTemplateView>>([]);
      return new(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Ok<T>(T body) =>
      new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<T> { Success = true, Response = body }
        ),
      };
  }
}
