using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Storage;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Storage;

// PulsR storage in a Cloud Storage bucket of the deployment, reached with
// the service's own identity from the metadata server. Objects are named
// "{company}/{file id}", reserved by construction, and uploads use
// ifGenerationMatch=0: a second attempt never writes a second object.
// Uploads and downloads stream; nothing is held whole in memory. Configured
// by Storage:Managed:Provider = "gcs" and Storage:Managed:Bucket; the bucket
// and its access are created outside the application.
public sealed partial class CloudStorageFileStorage(
  HttpClient http,
  IConfiguration configuration,
  TimeProvider clock
) : IFileStorageProvider
{
  private const string Api = "https://storage.googleapis.com/storage/v1/b";
  private const string Upload =
    "https://storage.googleapis.com/upload/storage/v1/b";
  private const string MetadataToken =
    "http://metadata.google.internal/computeMetadata/v1/instance/"
    + "service-accounts/default/token";

  // The client is transient; the service identity's token is per process.
  private static (string Token, DateTimeOffset Until)? token;
  private static readonly SemaphoreSlim tokenGate = new(1, 1);

  public string Kind => StorageKinds.Managed;

  public bool IsAvailable =>
    string.Equals(
      configuration["Storage:Managed:Provider"],
      "gcs",
      StringComparison.OrdinalIgnoreCase
    ) && Bucket is not null;

  public long MaximumSize => 1024L * 1024 * 1024;

  private string? Bucket =>
    configuration["Storage:Managed:Bucket"]?.Trim() is { } bucket
    && BucketName().IsMatch(bucket)
      ? bucket
      : null;

  public Task<string> ReserveKeyAsync(
    StorageTarget target,
    Guid fileId,
    CancellationToken ct
  ) => Task.FromResult($"{target.Company:N}/{fileId:N}");

  public async Task PutAsync(
    StorageTarget target,
    string key,
    StorageUpload upload,
    CancellationToken ct
  )
  {
    if (!Owned(target, key))
      throw new StorageUnavailableException("The key is not this company's.");
    using var start = await RequestAsync(
      HttpMethod.Post,
      $"{Upload}/{Bucket}/o?uploadType=resumable&ifGenerationMatch=0"
        + $"&name={Uri.EscapeDataString(key)}",
      ct
    );
    start.Headers.Add("X-Upload-Content-Type", upload.ContentType);
    start.Headers.Add("X-Upload-Content-Length", upload.Length.ToString());
    start.Content = JsonContent.Create(
      new
      {
        contentType = upload.ContentType,
        // Objects stay keyed by id; the readable path travels as metadata for
        // an export, not as a folder PulsR promises in the bucket.
        metadata = new
        {
          pulsrFile = upload.FileId.ToString("N"),
          path = string.Join('/', [.. upload.Folder, upload.Name]),
        },
      }
    );
    using var started = await SendAsync(start, ct);
    if (started.StatusCode == HttpStatusCode.PreconditionFailed)
      return;
    if (
      started
        is not { IsSuccessStatusCode: true, Headers.Location: { } session }
      || session.Host != "storage.googleapis.com"
    )
      throw new StorageUnavailableException(
        "Cloud Storage refused the upload."
      );
    using var put = await RequestAsync(HttpMethod.Put, session.ToString(), ct);
    put.Content = new StreamContent(upload.Content);
    put.Content.Headers.ContentLength = upload.Length;
    put.Content.Headers.ContentType = new MediaTypeHeaderValue(
      upload.ContentType
    );
    using var stored = await SendAsync(put, ct);
    if (
      !stored.IsSuccessStatusCode
      && stored.StatusCode != HttpStatusCode.PreconditionFailed
    )
      throw new StorageUnavailableException(
        "Cloud Storage refused the upload."
      );
  }

  public async Task<bool> ExistsAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  )
  {
    if (!Owned(target, key))
      return false;
    using var request = await RequestAsync(
      HttpMethod.Get,
      $"{Api}/{Bucket}/o/{Uri.EscapeDataString(key)}?fields=name",
      ct
    );
    using var response = await SendAsync(request, ct);
    return response.StatusCode switch
    {
      HttpStatusCode.NotFound => false,
      _ when response.IsSuccessStatusCode => true,
      _ => throw new StorageUnavailableException(
        "Cloud Storage did not answer."
      ),
    };
  }

  public async Task<Stream?> OpenAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  )
  {
    if (
      !key.StartsWith(
        target.Company.ToString("N") + "/",
        StringComparison.Ordinal
      )
    )
      return null;
    using var request = await RequestAsync(
      HttpMethod.Get,
      $"{Api}/{Bucket}/o/{Uri.EscapeDataString(key)}?alt=media",
      ct
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
        "Cloud Storage refused the download."
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
    if (
      !key.StartsWith(
        target.Company.ToString("N") + "/",
        StringComparison.Ordinal
      )
    )
      return;
    using var request = await RequestAsync(
      HttpMethod.Delete,
      $"{Api}/{Bucket}/o/{Uri.EscapeDataString(key)}",
      ct
    );
    using var response = await SendAsync(request, ct);
    if (
      !response.IsSuccessStatusCode
      && response.StatusCode != HttpStatusCode.NotFound
    )
      throw new StorageUnavailableException(
        "Cloud Storage refused the delete."
      );
  }

  public async Task<bool> CheckAsync(StorageTarget target, CancellationToken ct)
  {
    try
    {
      using var request = await RequestAsync(
        HttpMethod.Get,
        $"{Api}/{Bucket}?fields=name",
        ct
      );
      using var response = await SendAsync(request, ct);
      return response.IsSuccessStatusCode;
    }
    catch (StorageUnavailableException)
    {
      return false;
    }
  }

  // A company's objects live under its own prefix; the key is never taken
  // from anything a user supplied.
  private static bool Owned(StorageTarget target, string key) =>
    key.Length == 65
    && key.StartsWith(
      target.Company.ToString("N") + "/",
      StringComparison.Ordinal
    );

  private async Task<HttpRequestMessage> RequestAsync(
    HttpMethod method,
    string url,
    CancellationToken ct
  )
  {
    if (!IsAvailable)
      throw new StorageUnavailableException("PulsR storage is not set up.");
    var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue(
      "Bearer",
      await TokenAsync(ct)
    );
    return request;
  }

  private async Task<string> TokenAsync(CancellationToken ct)
  {
    await tokenGate.WaitAsync(ct);
    try
    {
      if (token is { } cached && cached.Until > clock.GetUtcNow())
        return cached.Token;
      using var request = new HttpRequestMessage(HttpMethod.Get, MetadataToken);
      request.Headers.Add("Metadata-Flavor", "Google");
      using var response = await SendAsync(request, ct);
      if (!response.IsSuccessStatusCode)
        throw new StorageUnavailableException(
          "No service identity for storage."
        );
      using var document = JsonDocument.Parse(
        await response.Content.ReadAsStringAsync(ct)
      );
      var access = document
        .RootElement.GetProperty("access_token")
        .GetString()!;
      var seconds = document.RootElement.GetProperty("expires_in").GetInt32();
      token = (access, clock.GetUtcNow().AddSeconds(Math.Max(0, seconds - 60)));
      return access;
    }
    catch (Exception ex)
      when (ex
          is JsonException
            or KeyNotFoundException
            or InvalidOperationException
      )
    {
      throw new StorageUnavailableException("No service identity for storage.");
    }
    finally
    {
      tokenGate.Release();
    }
  }

  private async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken ct,
    HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead
  )
  {
    try
    {
      return await http.SendAsync(request, completion, ct);
    }
    catch (Exception ex)
      when (ex is HttpRequestException
        || ex is OperationCanceledException && !ct.IsCancellationRequested
      )
    {
      throw new StorageUnavailableException("Cloud Storage did not answer.");
    }
  }

  [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,61}[a-z0-9]$")]
  private static partial Regex BucketName();
}
