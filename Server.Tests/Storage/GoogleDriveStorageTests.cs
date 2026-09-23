using System.Net;
using System.Text;
using Application.Storage;
using Infrastructure.Integrations.Google.Drive;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Storage;

// The Drive adapter against a scripted Google, never the network: tokens
// refreshed from the connection's grant, resumable uploads into the PulsR
// folder, trash rather than delete, and failures that say nothing of the
// provider's response or the grant.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class GoogleDriveStorageTests
{
  private const string Key = "drive-file-id-456";

  private static readonly StorageTarget Target = new(
    Guid.NewGuid(),
    Guid.NewGuid(),
    "root-folder-id-123",
    GoogleDriveClient.Serialize("refresh-secret")
  );

  [Fact]
  public async Task AnUploadRefreshesTheTokenAndStoresIntoThePulsRFolder()
  {
    var google = new ScriptedGoogle();
    var storage = Storage(google);

    await storage.PutAsync(Target, Key, Object(), default);

    Assert.Equal(
      [
        "POST https://oauth2.googleapis.com/token",
        "POST https://www.googleapis.com/upload/drive/v3/files",
        "PUT https://www.googleapis.com/upload/session",
      ],
      google.Calls
    );
    Assert.Contains("refresh-secret", google.Bodies[0]);
    Assert.Contains("\"root-folder-id-123\"", google.Bodies[1]);
    Assert.Contains("pulsrFile", google.Bodies[1]);
    Assert.Contains($"\"id\":\"{Key}\"", google.Bodies[1]);
    Assert.Equal("%PDF", google.Bodies[2]);
    Assert.All(google.Tokens.Skip(1), x => Assert.Equal("Bearer access-1", x));

    await storage.PutAsync(Target, Key, Object(), default);
    Assert.Single(google.Calls, x => x.EndsWith("/token"));
  }

  [Fact]
  public async Task ARefusedUploadSaysNothingOfTheResponseOrTheGrant()
  {
    var google = new ScriptedGoogle { UploadStatus = HttpStatusCode.Forbidden };

    var failure = await Assert.ThrowsAsync<StorageUnavailableException>(
      () => Storage(google).PutAsync(Target, Key, Object(), default)
    );

    Assert.DoesNotContain("quota", failure.Message);
    Assert.DoesNotContain("refresh-secret", failure.ToString());
  }

  [Fact]
  public async Task ASessionOutsideGoogleIsNeverSentTheFile()
  {
    var google = new ScriptedGoogle
    {
      Session = "https://attacker.example.invalid/upload",
    };

    await Assert.ThrowsAsync<StorageUnavailableException>(
      () => Storage(google).PutAsync(Target, Key, Object(), default)
    );
    Assert.DoesNotContain(google.Calls, x => x.StartsWith("PUT"));
  }

  [Fact]
  public async Task AMissingFileIsNullAndARevokedGrantIsUnavailable()
  {
    var missing = new ScriptedGoogle
    {
      DownloadStatus = HttpStatusCode.NotFound,
    };
    Assert.Null(
      await Storage(missing).OpenAsync(Target, "drive-file-id-456", default)
    );
    Assert.Null(await Storage(missing).OpenAsync(Target, "../etc", default));

    var revoked = new ScriptedGoogle
    {
      TokenStatus = HttpStatusCode.BadRequest,
    };
    await Assert.ThrowsAsync<StorageUnavailableException>(
      () => Storage(revoked).OpenAsync(Target, "drive-file-id-456", default)
    );
    Assert.False(await Storage(revoked).CheckAsync(Target, default));
  }

  [Fact]
  public async Task DeletingTrashesTheFileAndAGoneFileIsNotAnError()
  {
    var google = new ScriptedGoogle();
    await Storage(google).DeleteAsync(Target, "drive-file-id-456", default);
    Assert.Contains(
      "PATCH https://www.googleapis.com/drive/v3/files/drive-file-id-456",
      google.Calls
    );
    Assert.Contains("\"trashed\":true", google.Bodies[^1]);

    var gone = new ScriptedGoogle { PatchStatus = HttpStatusCode.NotFound };
    await Storage(gone).DeleteAsync(Target, "drive-file-id-456", default);
  }

  [Fact]
  public async Task AReservedIdMakesASecondCreateOfTheSameFileANoOp()
  {
    var google = new ScriptedGoogle();
    Assert.Equal(
      "reserved-id-789",
      await Storage(google).ReserveKeyAsync(Target, Guid.NewGuid(), default)
    );
    Assert.Contains(google.Calls, x => x.EndsWith("/files/generateIds"));

    var duplicate = new ScriptedGoogle
    {
      CreateStatus = HttpStatusCode.Conflict,
    };
    await Storage(duplicate).PutAsync(Target, Key, Object(), default);
    Assert.DoesNotContain(duplicate.Calls, x => x.StartsWith("PUT"));
  }

  // Readable folders are found or made under the chosen root; the file goes
  // into the last one. A name with a quote cannot break the search.
  [Fact]
  public async Task FilesGoIntoReadableFoldersUnderTheChosenRoot()
  {
    var google = new ScriptedGoogle();

    await Storage(google)
      .PutAsync(
        Target with
        {
          Connection = Guid.NewGuid(),
        },
        Key,
        Object("Dispatch", "O'Brien - 1407"),
        default
      );

    var searches = google
      .Queries.Where(x => x.Contains("q="))
      .Select(Uri.UnescapeDataString)
      .ToList();
    Assert.Contains("'root-folder-id-123' in parents", searches[0]);
    Assert.Contains("name = 'O\\'Brien - 1407'", searches[1]);
    Assert.Contains("'existing-folder-1' in parents", searches[1]);
    var create = google.Bodies[
      google.Calls.IndexOf("POST https://www.googleapis.com/drive/v3/files")
    ];
    Assert.Contains("\"parents\":[\"existing-folder-1\"]", create);
    var upload = google.Bodies[
      google.Calls.IndexOf(
        "POST https://www.googleapis.com/upload/drive/v3/files"
      )
    ];
    Assert.Contains("\"parents\":[\"created-folder-", upload);
  }

  [Fact]
  public async Task AnObjectExistsOnlyWhileItIsNotTrashed()
  {
    Assert.True(
      await Storage(new ScriptedGoogle()).ExistsAsync(Target, Key, default)
    );
    Assert.False(
      await Storage(new ScriptedGoogle { Trashed = true })
        .ExistsAsync(Target, Key, default)
    );
    Assert.False(
      await Storage(new ScriptedGoogle { Missing = true })
        .ExistsAsync(Target, Key, default)
    );
    Assert.False(
      await Storage(new ScriptedGoogle()).ExistsAsync(Target, "../x", default)
    );
  }

  [Fact]
  public async Task OnlyAFolderThisAccountCanAddFilesToIsAccepted()
  {
    var picker = new GoogleDriveRootPicker(
      new HttpClient(new ScriptedGoogle { Folder = Folder(true) }),
      new GoogleDriveClient(Configuration())
    );
    var root = await picker.VerifyAsync(Target, "shared-folder-id-1", default);
    Assert.Equal(("Loads", "shared-drive-1"), (root!.Name, root.DriveId));

    foreach (
      var refused in new[]
      {
        Folder(false),
        Folder(true).Replace("google-apps.folder", "google-apps.document"),
        Folder(true).Replace("\"trashed\":false", "\"trashed\":true"),
      }
    )
      Assert.Null(
        await new GoogleDriveRootPicker(
          new HttpClient(new ScriptedGoogle { Folder = refused }),
          new GoogleDriveClient(Configuration())
        ).VerifyAsync(Target, "shared-folder-id-1", default)
      );
    Assert.Null(await picker.VerifyAsync(Target, "../x", default));
  }

  private static string Folder(bool canAdd) =>
    "{\"id\":\"shared-folder-id-1\",\"name\":\"Loads\","
    + "\"mimeType\":\"application/vnd.google-apps.folder\","
    + "\"driveId\":\"shared-drive-1\",\"trashed\":false,"
    + $"\"capabilities\":{{\"canAddChildren\":{(canAdd ? "true" : "false")}}}}}";

  [Fact]
  public void WithoutAClientRegistrationTheKindIsNotConfigured()
  {
    Assert.False(
      new GoogleDriveClient(Configuration(https: false)).IsConfigured
    );
    Assert.False(
      new GoogleDriveClient(new ConfigurationBuilder().Build()).IsConfigured
    );
    Assert.True(new GoogleDriveClient(Configuration()).IsConfigured);
  }

  private static GoogleDriveStorage Storage(ScriptedGoogle google) =>
    new(new HttpClient(google), new GoogleDriveClient(Configuration()));

  private static StorageUpload Object(params string[] folder) =>
    new(
      Guid.NewGuid(),
      "pod.pdf",
      folder,
      "application/pdf",
      4,
      new MemoryStream(Encoding.UTF8.GetBytes("%PDF"))
    );

  private static IConfiguration Configuration(bool https = true) =>
    new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["GoogleDrive:ClientId"] = "client-id",
          ["GoogleDrive:ClientSecret"] = "client-secret",
          ["GoogleDrive:RedirectUri"] = https
            ? "https://tms.example.invalid/api/storage/connections/callback"
            : "http://tms.example.invalid/callback",
        }
      )
      .Build();

  private sealed class ScriptedGoogle : HttpMessageHandler
  {
    public HttpStatusCode TokenStatus { get; init; } = HttpStatusCode.OK;
    public HttpStatusCode UploadStatus { get; init; } = HttpStatusCode.OK;
    public HttpStatusCode DownloadStatus { get; init; } = HttpStatusCode.OK;
    public HttpStatusCode PatchStatus { get; init; } = HttpStatusCode.OK;
    public string Session { get; init; } =
      "https://www.googleapis.com/upload/session";
    public List<string> Calls { get; } = [];
    public List<string> Bodies { get; } = [];
    public List<string?> Tokens { get; } = [];
    public List<string> Queries { get; } = [];
    public HttpStatusCode CreateStatus { get; init; } = HttpStatusCode.OK;
    public bool Trashed { get; init; }
    public bool Missing { get; init; }
    public string? Folder { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
      Calls.Add($"{request.Method} {url}");
      Bodies.Add(
        request.Content is null
          ? ""
          : await request.Content.ReadAsStringAsync(cancellationToken)
      );
      Tokens.Add(request.Headers.Authorization?.ToString());
      Queries.Add(request.RequestUri.Query);
      if (url.EndsWith("/token"))
        return Json(
          TokenStatus,
          "{\"access_token\":\"access-1\",\"expires_in\":3600}"
        );
      if (url.EndsWith("/generateIds"))
        return Json(HttpStatusCode.OK, "{\"ids\":[\"reserved-id-789\"]}");
      if (request.Method == HttpMethod.Post && url.Contains("/upload/"))
      {
        var started = new HttpResponseMessage(
          CreateStatus == HttpStatusCode.OK ? UploadStatus : CreateStatus
        )
        {
          Content = new StringContent("{\"error\":{\"message\":\"quota\"}}"),
        };
        started.Headers.Location = new Uri(Session);
        return started;
      }
      if (request.Method == HttpMethod.Put)
        return Json(HttpStatusCode.OK, "{\"id\":\"drive-file-id-456\"}");
      if (request.Method.Method == "PATCH")
        return Json(PatchStatus, "{}");
      if (request.RequestUri.Query.Contains("q="))
      {
        var query = Uri.UnescapeDataString(request.RequestUri.Query);
        return Json(
          HttpStatusCode.OK,
          query.Contains("name = 'Dispatch'")
            ? "{\"files\":[{\"id\":\"existing-folder-1\"}]}"
            : "{\"files\":[]}"
        );
      }
      if (request.Method == HttpMethod.Post && url.EndsWith("/drive/v3/files"))
        return Json(
          HttpStatusCode.OK,
          "{\"id\":\"created-folder-" + Calls.Count + "\"}"
        );
      if (Missing)
        return Json(HttpStatusCode.NotFound, "{}");
      if (Folder is not null)
        return Json(HttpStatusCode.OK, Folder);
      return request.RequestUri.Query.Contains("alt=media")
        ? new HttpResponseMessage(DownloadStatus)
        {
          Content = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF")),
        }
        : Json(
          HttpStatusCode.OK,
          "{\"id\":\"root-folder-id-123\",\"trashed\":"
            + (Trashed ? "true" : "false")
            + ","
            + "\"capabilities\":{\"canAddChildren\":true}}"
        );
    }

    private static HttpResponseMessage Json(
      HttpStatusCode status,
      string body
    ) =>
      new(status)
      {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
      };
  }
}
