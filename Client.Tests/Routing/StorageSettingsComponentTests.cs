using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Storage;
using Client.Pages.Settings;
using Client.Tests.Support;
using Settings = Client.Models.DTO.Storage.StorageSettings;
using StorageCard = Client.Pages.Settings.StorageSettings;

namespace Client.Tests.Routing;

// The storage card says why a kind cannot be used, asks for a folder before
// an account holds files, and sends the revision it showed.
[Trait("Category", "Routing")]
[Trait("Kind", "Component")]
public sealed class StorageSettingsComponentTests
{
  private static readonly Guid Drive = Guid.NewGuid();

  [Fact]
  public async Task AnAccountWithoutAFolderAsksForOneAndChangesSendTheShownRevision()
  {
    var posts = new List<(string Path, string Body)>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Json(
            new Settings(
              [
                new("managed", "PulsR storage", true, true, null),
                new("google-drive", "Google Drive", true, true, null),
                new("dropbox", "Dropbox", false, false, "Not available yet."),
              ],
              [
                new(
                  Guid.NewGuid(),
                  "managed",
                  "PulsR storage",
                  "connected",
                  true,
                  null,
                  null,
                  3,
                  DateTime.UtcNow
                ),
                new(
                  Drive,
                  "google-drive",
                  "Loads drive",
                  "needs-root",
                  false,
                  null,
                  null,
                  5,
                  DateTime.UtcNow
                ),
              ]
            )
          );
        posts.Add(
          (
            request.RequestUri!.AbsolutePath,
            await request.Content!.ReadAsStringAsync(ct)
          )
        );
        return Json(
          new StorageConnectionView(
            Drive,
            "google-drive",
            "Loads drive",
            "connected",
            true,
            "Loads",
            null,
            6,
            DateTime.UtcNow
          )
        );
      }
    );

    var card = context.Render<StorageCard>();

    card.WaitForElement(".storage-settings__connection");
    Assert.Contains("Choose a folder", card.Markup);
    Assert.Contains("Choose folder", card.Markup);
    var dropbox = card.FindAll(".storage-settings__kind button")
      .Single(x => x.TextContent.Contains("Dropbox"));
    Assert.True(dropbox.HasAttribute("disabled"));
    Assert.Contains("Not available yet.", card.Markup);
    Assert.DoesNotContain(
      card.FindAll("button"),
      x => x.TextContent.Contains("Make default")
    );

    await card.FindAll("button")
      .Single(x => x.TextContent.Contains("Disconnect…"))
      .ClickAsync(new());
    await card.FindAll("button")
      .Single(x => x.TextContent.Trim() == "Disconnect")
      .ClickAsync(new());

    var (path, body) = Assert.Single(posts);
    Assert.Equal($"/api/storage/connections/{Drive}/disconnect", path);
    Assert.Contains("\"expectedRevision\":5", body);
  }

  // A refused template keeps the draft and says why; the example shown is
  // the server's, not a guess made in the browser.
  [Fact]
  public async Task ARefusedLayoutKeepsTheDraftAndTheSavedExample()
  {
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.Method == HttpMethod.Get
            ? Json(
              new StorageLayoutView(
                "Dispatch/Loads",
                "{date} - {load}",
                "Canceled",
                "Inbox",
                2,
                "Dispatch/Loads/2026.09.21 - 1407",
                "Dispatch/Loads/2026.09.21 - 1407 - Canceled"
              )
            )
            : new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
              Content = JsonContent.Create(
                new RequestResponseDTO<StorageLayoutView>
                {
                  Success = false,
                  Errors = ["The template names an unknown field: driver."],
                }
              ),
            }
        )
    );
    var card = context.Render<StorageLayoutSettings>();
    card.WaitForElement("#storage-load-template").Input("{load} - {driver}");

    await card.FindAll("button")
      .Single(x => x.TextContent.Contains("Save file names"))
      .ClickAsync(new());

    Assert.Contains("unknown field", card.Find("[role=alert]").TextContent);
    Assert.Equal(
      "{load} - {driver}",
      card.Find("#storage-load-template").GetAttribute("value")
    );
    Assert.Contains("Dispatch/Loads/2026.09.21 - 1407", card.Markup);
  }

  private static HttpResponseMessage Json<T>(T body) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<T> { Success = true, Response = body }
      ),
    };
}
