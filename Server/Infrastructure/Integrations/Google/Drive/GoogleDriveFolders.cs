using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Storage;

namespace Infrastructure.Integrations.Google.Drive;

// The readable folder chain a file goes into, under the folder an
// administrator picked. Folder ids are cached per process and connection.
public sealed class GoogleDriveFolders(HttpClient http)
{
  private static readonly ConcurrentDictionary<
    (Guid, string, string),
    string
  > Folders = new();
  private static readonly ConcurrentDictionary<
    Guid,
    SemaphoreSlim
  > FolderGates = new();

  // The readable folder chain under the chosen root, found or made one level
  // at a time. With drive.file PulsR sees only folders it made or was shown,
  // so a same-named folder a person made by hand is not reused. Creation is
  // serialized per connection in this process; two servers at once can
  // still make twin folders, which changes no file's identity.
  public async Task<string> ResolveAsync(
    StorageTarget target,
    string token,
    IReadOnlyList<string> path,
    CancellationToken ct
  )
  {
    var parent = target.Root!;
    if (path.Count == 0)
      return parent;
    var gate = FolderGates.GetOrAdd(
      target.Connection,
      _ => new SemaphoreSlim(1, 1)
    );
    await gate.WaitAsync(ct);
    try
    {
      foreach (var name in path)
      {
        var cacheKey = (target.Connection, parent, name.ToLowerInvariant());
        if (Folders.TryGetValue(cacheKey, out var known))
        {
          parent = known;
          continue;
        }
        parent =
          await FindFolderAsync(token, parent, name, ct)
          ?? await CreateFolderAsync(token, parent, name, ct);
        Folders[cacheKey] = parent;
      }
      return parent;
    }
    finally
    {
      gate.Release();
    }
  }

  private async Task<string?> FindFolderAsync(
    string token,
    string parent,
    string name,
    CancellationToken ct
  )
  {
    var escaped = name.Replace("\\", "\\\\").Replace("'", "\\'");
    var query = Uri.EscapeDataString(
      $"name = '{escaped}' and '{parent}' in parents"
        + " and mimeType = 'application/vnd.google-apps.folder'"
        + " and trashed = false"
    );
    using var request = GoogleDriveClient.Request(
      HttpMethod.Get,
      $"{GoogleDriveClient.FilesUrl}?q={query}&corpora=allDrives"
        + "&includeItemsFromAllDrives=true&supportsAllDrives=true"
        + "&pageSize=1&fields=files(id)",
      token
    );
    using var response = await GoogleDriveClient.SendAsync(http, request, ct);
    if (!response.IsSuccessStatusCode)
      throw new StorageUnavailableException("Google Drive did not answer.");
    try
    {
      using var document = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync(ct)
      );
      foreach (
        var file in document.RootElement.GetProperty("files").EnumerateArray()
      )
        if (
          file.TryGetProperty("id", out var id)
          && GoogleDriveClient.IsId(id.GetString())
        )
          return id.GetString();
      return null;
    }
    catch (Exception ex)
      when (ex
          is JsonException
            or KeyNotFoundException
            or InvalidOperationException
      )
    {
      throw new StorageUnavailableException("Google Drive did not answer.");
    }
  }

  private async Task<string> CreateFolderAsync(
    string token,
    string parent,
    string name,
    CancellationToken ct
  )
  {
    using var request = GoogleDriveClient.Request(
      HttpMethod.Post,
      $"{GoogleDriveClient.FilesUrl}?supportsAllDrives=true&fields=id",
      token
    );
    request.Content = JsonContent.Create(
      new
      {
        name,
        mimeType = "application/vnd.google-apps.folder",
        parents = new[] { parent },
        appProperties = new { pulsrFolder = "1" },
      }
    );
    using var response = await GoogleDriveClient.SendAsync(http, request, ct);
    return
      response.IsSuccessStatusCode
      && GoogleDriveClient.Id(await response.Content.ReadAsStringAsync(ct))
        is { } id
      ? id
      : throw new StorageUnavailableException(
        "Google Drive refused the folder."
      );
  }
}
