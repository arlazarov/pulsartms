using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Storage;
using Infrastructure.Storage;

namespace Infrastructure.Integrations.Google.Drive;

// Files in a company's own Google Drive, in the folder an administrator
// picked (My Drive or a shared drive). Each file is created with a Drive id
// reserved before its upload, so an upload whose answer was lost, or two at
// once, end with one file. Uploads and downloads stream. Deleting moves a
// file to the Drive trash, where the company's own retention applies.
public sealed class GoogleDriveStorage(
  HttpClient http,
  GoogleDriveClient client
) : IFileStorageProvider
{
  public string Kind => StorageKinds.GoogleDrive;
  public bool IsAvailable => client.IsConfigured;

  // Resumable uploads accept far more; this bounds one PulsR file.
  public long MaximumSize => 1024L * 1024 * 1024;

  // A Drive id generated for this file; creating a file with it is what makes
  // a second create of the same file fail instead of duplicating it.
  public async Task<string> ReserveKeyAsync(
    StorageTarget target,
    Guid fileId,
    CancellationToken ct
  )
  {
    var token = await client.AccessTokenAsync(http, target, ct);
    using var request = GoogleDriveClient.Request(
      HttpMethod.Get,
      $"{GoogleDriveClient.FilesUrl}/generateIds?count=1&space=drive&type=files",
      token
    );
    using var response = await SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
      throw new StorageUnavailableException(
        "Google Drive did not reserve an id."
      );
    try
    {
      using var document = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync(ct)
      );
      var id = document.RootElement.GetProperty("ids")[0].GetString();
      return GoogleDriveClient.IsId(id)
        ? id!
        : throw new StorageUnavailableException(
          "Google Drive did not reserve an id."
        );
    }
    catch (Exception ex)
      when (ex
          is JsonException
            or KeyNotFoundException
            or InvalidOperationException
            or IndexOutOfRangeException
      )
    {
      throw new StorageUnavailableException(
        "Google Drive did not reserve an id."
      );
    }
  }

  public async Task PutAsync(
    StorageTarget target,
    string key,
    StorageUpload upload,
    CancellationToken ct
  )
  {
    if (!GoogleDriveClient.IsId(target.Root))
      throw new StorageUnavailableException("No Drive folder is chosen.");
    if (!GoogleDriveClient.IsId(key))
      throw new StorageUnavailableException("The key is not a Drive id.");
    var token = await client.AccessTokenAsync(http, target, ct);
    var parent = await new GoogleDriveFolders(http).ResolveAsync(
      target,
      token,
      upload.Folder,
      ct
    );
    using var start = GoogleDriveClient.Request(
      HttpMethod.Post,
      $"{GoogleDriveClient.UploadUrl}?uploadType=resumable&supportsAllDrives=true",
      token
    );
    start.Headers.Add("X-Upload-Content-Type", upload.ContentType);
    start.Headers.Add("X-Upload-Content-Length", upload.Length.ToString());
    start.Content = JsonContent.Create(
      new
      {
        id = key,
        name = upload.Name,
        parents = new[] { parent },
        mimeType = upload.ContentType,
        appProperties = new { pulsrFile = upload.FileId.ToString("N") },
      }
    );
    Uri session;
    using (var started = await SendAsync(start, ct))
    {
      if (started.StatusCode == HttpStatusCode.Conflict)
        return;
      session =
        started is { IsSuccessStatusCode: true, Headers.Location: { } location }
        && location.Host == "www.googleapis.com"
          ? location
          : throw new StorageUnavailableException(
            "Google Drive refused the upload."
          );
    }
    using var put = GoogleDriveClient.Request(
      HttpMethod.Put,
      session.ToString(),
      token
    );
    put.Content = new StreamContent(upload.Content);
    put.Content.Headers.ContentLength = upload.Length;
    put.Content.Headers.ContentType = new MediaTypeHeaderValue(
      upload.ContentType
    );
    using var uploaded = await SendAsync(put, ct);
    if (uploaded.StatusCode == HttpStatusCode.Conflict)
      return;
    if (
      !uploaded.IsSuccessStatusCode
      || GoogleDriveClient.Id(await uploaded.Content.ReadAsStringAsync(ct))
        != key
    )
      throw new StorageUnavailableException("Google Drive refused the upload.");
  }

  public async Task<bool> ExistsAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  )
  {
    if (!GoogleDriveClient.IsId(key))
      return false;
    var token = await client.AccessTokenAsync(http, target, ct);
    using var request = GoogleDriveClient.Request(
      HttpMethod.Get,
      $"{GoogleDriveClient.FilesUrl}/{key}?supportsAllDrives=true&fields=id,trashed",
      token
    );
    using var response = await SendAsync(request, ct);
    if (response.StatusCode == HttpStatusCode.NotFound)
      return false;
    if (!response.IsSuccessStatusCode)
      throw new StorageUnavailableException("Google Drive did not answer.");
    try
    {
      using var document = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync(ct)
      );
      return !(
        document.RootElement.TryGetProperty("trashed", out var trashed)
        && trashed.ValueKind == JsonValueKind.True
      );
    }
    catch (JsonException)
    {
      throw new StorageUnavailableException("Google Drive did not answer.");
    }
  }

  public async Task<Stream?> OpenAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  )
  {
    if (!GoogleDriveClient.IsId(key))
      return null;
    var token = await client.AccessTokenAsync(http, target, ct);
    using var request = GoogleDriveClient.Request(
      HttpMethod.Get,
      $"{GoogleDriveClient.FilesUrl}/{key}?alt=media&supportsAllDrives=true",
      token
    );
    var response = await SendAsync(
      request,
      ct,
      HttpCompletionOption.ResponseHeadersRead
    );
    if (response.StatusCode == HttpStatusCode.NotFound)
    {
      response.Dispose();
      return null;
    }
    if (!response.IsSuccessStatusCode)
    {
      response.Dispose();
      throw new StorageUnavailableException(
        "Google Drive refused the download."
      );
    }
    return new ResponseStream(
      response,
      await response.Content.ReadAsStreamAsync(ct)
    );
  }

  public async Task DeleteAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  )
  {
    if (!GoogleDriveClient.IsId(key))
      return;
    var token = await client.AccessTokenAsync(http, target, ct);
    using var request = GoogleDriveClient.Request(
      HttpMethod.Patch,
      $"{GoogleDriveClient.FilesUrl}/{key}?supportsAllDrives=true&fields=id",
      token
    );
    request.Content = JsonContent.Create(new { trashed = true });
    using var response = await SendAsync(request, ct);
    if (
      !response.IsSuccessStatusCode
      && response.StatusCode != HttpStatusCode.NotFound
    )
      throw new StorageUnavailableException("Google Drive refused the delete.");
  }

  public async Task<bool> CheckAsync(StorageTarget target, CancellationToken ct)
  {
    if (!GoogleDriveClient.IsId(target.Root))
      return false;
    try
    {
      var token = await client.AccessTokenAsync(http, target, ct);
      using var request = GoogleDriveClient.Request(
        HttpMethod.Get,
        $"{GoogleDriveClient.FilesUrl}/{target.Root}?supportsAllDrives=true"
          + "&fields=id,trashed,capabilities/canAddChildren",
        token
      );
      using var response = await SendAsync(request, ct);
      if (!response.IsSuccessStatusCode)
        return false;
      using var document = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync(ct)
      );
      var root = document.RootElement;
      return !(
          root.TryGetProperty("trashed", out var trashed)
          && trashed.ValueKind == JsonValueKind.True
        )
        && root.TryGetProperty("capabilities", out var capabilities)
        && capabilities.TryGetProperty("canAddChildren", out var add)
        && add.ValueKind == JsonValueKind.True;
    }
    catch (Exception ex)
      when (ex is StorageUnavailableException or JsonException)
    {
      return false;
    }
  }

  private Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken ct,
    HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead
  ) => GoogleDriveClient.SendAsync(http, request, ct, completion);
}
