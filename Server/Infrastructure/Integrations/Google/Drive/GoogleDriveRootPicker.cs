using System.Net;
using System.Text.Json;
using Application.Storage;

namespace Infrastructure.Integrations.Google.Drive;

// The folder a company's files go into, chosen by an administrator in
// Google's Picker, in My Drive or a shared drive. Picking the folder is what
// grants the drive.file scope access to it; the server then checks the
// folder exists, is not trashed and lets this account add files.
public sealed class GoogleDriveRootPicker(
  HttpClient http,
  GoogleDriveClient client
) : IStorageRootPicker
{
  public string Kind => StorageKinds.GoogleDrive;
  public bool IsConfigured => client.PickerConfigured;

  public async Task<StoragePickerSession?> SessionAsync(
    StorageTarget target,
    CancellationToken ct
  )
  {
    if (!IsConfigured)
      return null;
    try
    {
      return new(
        await client.AccessTokenAsync(http, target, ct),
        client.ClientId!,
        client.PickerApiKey!,
        client.AppId!
      );
    }
    catch (StorageUnavailableException)
    {
      return null;
    }
  }

  public async Task<StorageRoot?> VerifyAsync(
    StorageTarget target,
    string folderId,
    CancellationToken ct
  )
  {
    if (!GoogleDriveClient.IsId(folderId))
      return null;
    string token;
    try
    {
      token = await client.AccessTokenAsync(http, target, ct);
    }
    catch (StorageUnavailableException)
    {
      return null;
    }
    using var request = GoogleDriveClient.Request(
      HttpMethod.Get,
      $"{GoogleDriveClient.FilesUrl}/{folderId}?supportsAllDrives=true"
        + "&fields=id,name,mimeType,driveId,trashed,capabilities/canAddChildren",
      token
    );
    try
    {
      using var response = await http.SendAsync(request, ct);
      if (
        response.StatusCode is HttpStatusCode.NotFound
        || !response.IsSuccessStatusCode
      )
        return null;
      using var document = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync(ct)
      );
      var root = document.RootElement;
      return
        Text(root, "id") == folderId
        && Text(root, "mimeType") == "application/vnd.google-apps.folder"
        && !(
          root.TryGetProperty("trashed", out var trashed)
          && trashed.ValueKind == JsonValueKind.True
        )
        && root.TryGetProperty("capabilities", out var capabilities)
        && capabilities.TryGetProperty("canAddChildren", out var add)
        && add.ValueKind == JsonValueKind.True
        ? new(folderId, Name(Text(root, "name")), Text(root, "driveId"))
        : null;
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException)
    {
      return null;
    }
  }

  private static string? Text(JsonElement parent, string name) =>
    parent.TryGetProperty(name, out var value)
    && value.ValueKind == JsonValueKind.String
      ? value.GetString()
      : null;

  private static string Name(string? name) =>
    name is { Length: > 0 } value
      ? value.Length > 200
        ? value[..200]
        : value
      : "Drive folder";
}
