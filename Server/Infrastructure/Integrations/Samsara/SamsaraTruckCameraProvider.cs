using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Fleet.Interfaces;
using Application.Features.Integrations.Models;
using Application.Interfaces;

namespace Infrastructure.Integrations.Samsara;

public sealed class SamsaraTruckCameraProvider(
  SamsaraApiService api,
  IReadCache cache,
  ICurrentCompany company
) : ITruckCameraProvider
{
  public async Task<CameraRequest> RequestAsync(
    string vehicleId,
    DateTimeOffset time,
    CancellationToken ct
  )
  {
    var credentials = await api.CaptureCameraCredentialsAsync(ct);
    var scope = Scope(credentials);
    var response = await api.CaptureCameraAsync(
      vehicleId,
      time,
      credentials,
      ct
    );
    cache.Invalidate($"camera:{vehicleId}");
    var id =
      response.GetProperty("data").GetProperty("retrievalId").GetString()
      ?? throw new JsonException("Camera retrieval identifier missing.");
    return new(id, scope);
  }

  public async Task<CameraImage> GetAsync(
    string vehicleId,
    CameraRequest retrieval,
    CancellationToken ct
  )
  {
    var credentials = await api.CaptureCameraCredentialsAsync(ct);
    var scope = Scope(credentials);
    if (retrieval.Scope != scope)
      return new("expired");
    var response = await api.ReadCameraAsync(
      "cameras/media/retrieval?retrievalId="
        + Uri.EscapeDataString(retrieval.Id),
      credentials,
      ct
    );
    foreach (var media in ReadMedia(response))
    {
      if (
        media.GetProperty("vehicleId").GetString() != vehicleId
        || media.GetProperty("input").GetString() != "dashcamRoadFacing"
        || media.GetProperty("mediaType").GetString() != "image"
      )
        continue;
      var status = media.GetProperty("status").GetString() ?? "pending";
      if (status == "pending")
        return await LatestAsync(vehicleId, credentials, scope, ct);
      if (status != "available")
        return new(status);
      var url = media.GetProperty("urlInfo").GetProperty("url").GetString();
      if (
        !Uri.TryCreate(url, UriKind.Absolute, out var uri)
        || uri.Scheme != "https"
      )
        throw new JsonException("Camera image URL is invalid.");
      var captured = media.GetProperty("startTime").GetDateTimeOffset();
      return new("available", url, captured);
    }
    return await LatestAsync(vehicleId, credentials, scope, ct);
  }

  public async Task<CameraImage> LatestAsync(
    string vehicleId,
    CancellationToken ct
  )
  {
    var credentials = await api.CaptureCameraCredentialsAsync(ct);
    return await LatestAsync(vehicleId, credentials, Scope(credentials), ct);
  }

  private Task<CameraImage> LatestAsync(
    string vehicleId,
    IntegrationCredentialValues credentials,
    string scope,
    CancellationToken ct
  ) =>
    cache.GetAsync(
      $"camera:{vehicleId}",
      scope,
      () => ReadLatestAsync(vehicleId, credentials, ct),
      TimeSpan.FromSeconds(30),
      ct
    );

  private async Task<CameraImage> ReadLatestAsync(
    string vehicleId,
    IntegrationCredentialValues credentials,
    CancellationToken ct
  )
  {
    var now = DateTimeOffset.UtcNow;
    var response = await api.ReadCameraAsync(
      "cameras/media?vehicleIds="
        + Uri.EscapeDataString(vehicleId)
        + "&inputs=dashcamRoadFacing&mediaTypes=image&startTime="
        + Uri.EscapeDataString(now.AddHours(-1).ToString("O"))
        + "&endTime="
        + Uri.EscapeDataString(now.ToString("O")),
      credentials,
      ct
    );
    var result = new CameraImage("pending");
    foreach (var media in ReadMedia(response))
    {
      if (
        media.GetProperty("vehicleId").GetString() != vehicleId
        || media.GetProperty("input").GetString() != "dashcamRoadFacing"
      )
        continue;
      var captured = media.GetProperty("startTime").GetDateTimeOffset();
      if (result.CapturedAt is not null && captured <= result.CapturedAt)
        continue;
      var url = media.GetProperty("urlInfo").GetProperty("url").GetString();
      if (
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https"
      )
        result = new("pending", url, captured);
    }
    return result;
  }

  private string Scope(IntegrationCredentialValues credentials)
  {
    var owner =
      company.Id
      ?? throw new InvalidOperationException("Camera reads require a company.");
    var token = credentials.Get("apiKey");
    if (string.IsNullOrWhiteSpace(token))
      throw new InvalidOperationException(
        "Samsara API token is not configured."
      );
    var fingerprint = Convert.ToHexString(
      SHA256.HashData(Encoding.UTF8.GetBytes(token))
    );
    return $"{owner:N}:{fingerprint}";
  }

  private static IEnumerable<JsonElement> ReadMedia(JsonElement response)
  {
    if (response.ValueKind != JsonValueKind.Object)
      throw new JsonException("Camera response must be an object.");
    if (
      !response.TryGetProperty("data", out var data)
      || data.ValueKind == JsonValueKind.Null
    )
      yield break;
    if (data.ValueKind != JsonValueKind.Object)
      throw new JsonException("Camera data must be an object.");
    if (
      !data.TryGetProperty("media", out var media)
      || media.ValueKind == JsonValueKind.Null
    )
      yield break;
    if (media.ValueKind != JsonValueKind.Array)
      throw new JsonException("Camera media must be an array.");
    foreach (var item in media.EnumerateArray())
      yield return item;
  }
}
