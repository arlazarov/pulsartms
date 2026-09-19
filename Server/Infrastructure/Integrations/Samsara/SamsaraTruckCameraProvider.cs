using System.Text.Json;
using Application.Features.Fleet.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Integrations.Samsara;

public sealed class SamsaraTruckCameraProvider(
  SamsaraApiService api,
  IMemoryCache cache
) : ITruckCameraProvider
{
  public async Task<string> RequestAsync(
    string vehicleId,
    DateTimeOffset time,
    CancellationToken ct
  )
  {
    var response = await api.RequestCameraImageAsync(vehicleId, time, ct);
    cache.Remove($"camera-latest:{vehicleId}");
    return response.GetProperty("data").GetProperty("retrievalId").GetString()
      ?? throw new JsonException("Camera retrieval identifier missing.");
  }

  public async Task<CameraImage> GetAsync(
    string vehicleId,
    string retrievalId,
    CancellationToken ct
  )
  {
    var response = await api.GetAsync<JsonElement>(
      "cameras/media/retrieval?retrievalId="
        + Uri.EscapeDataString(retrievalId),
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
        return await LatestAsync(vehicleId, ct);
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
    return await LatestAsync(vehicleId, ct);
  }

  public async Task<CameraImage> LatestAsync(
    string vehicleId,
    CancellationToken ct
  )
  {
    var key = $"camera-latest:{vehicleId}";
    if (cache.TryGetValue<CameraImage>(key, out var saved) && saved is not null)
      return saved;
    var now = DateTimeOffset.UtcNow;
    var response = await api.GetAsync<JsonElement>(
      "cameras/media?vehicleIds="
        + Uri.EscapeDataString(vehicleId)
        + "&inputs=dashcamRoadFacing&mediaTypes=image&startTime="
        + Uri.EscapeDataString(now.AddHours(-1).ToString("O"))
        + "&endTime="
        + Uri.EscapeDataString(now.ToString("O")),
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
    cache.Set(key, result, TimeSpan.FromSeconds(30));
    return result;
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
