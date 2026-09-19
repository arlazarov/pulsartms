using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Infrastructure.Integrations.Http;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.Google.Places;

public class GooglePlacesService(
  HttpClient httpClient,
  IConfiguration configuration
) : BaseApiService(httpClient), IPlaceSearchService
{
  private const string Fields =
    "id,displayName,formattedAddress,location,businessStatus,"
    + "regularOpeningHours,utcOffsetMinutes";

  public async Task<PlaceSearchResult?> ReadAsync(
    string placeId,
    CancellationToken cancellationToken = default
  )
  {
    if (string.IsNullOrWhiteSpace(placeId))
      return null;
    using var request = new HttpRequestMessage(
      HttpMethod.Get,
      $"https://places.googleapis.com/v1/places/{Uri.EscapeDataString(placeId)}"
    );
    request.Headers.Add("X-Goog-Api-Key", ApiKey());
    request.Headers.Add("X-Goog-FieldMask", Fields);
    return Read(await SendAsync<Place>(request, cancellationToken));
  }

  private static PlaceSearchResult? Read(Place? place) =>
    place is null
      ? null
      : new PlaceSearchResult
      {
        PlaceId = place.Id,
        Name = place.DisplayName?.Text ?? string.Empty,
        Address = place.FormattedAddress,
        Latitude = place.Location?.Latitude ?? 0,
        Longitude = place.Location?.Longitude ?? 0,
        BusinessStatus = place.BusinessStatus,
        OpeningHoursJson = place.RegularOpeningHours is null
          ? null
          : JsonSerializer.Serialize(place.RegularOpeningHours),
        UtcOffsetMinutes = place.UtcOffsetMinutes,
      };

  private string ApiKey() =>
    configuration["GooglePlaces:ApiKey"]
    ?? throw new InvalidOperationException(
      "Google Places API key is not configured."
    );

  public async Task<PlaceSearchResult?> SearchAsync(
    string query,
    CancellationToken cancellationToken = default
  )
  {
    var apiKey =
      configuration["GooglePlaces:ApiKey"]
      ?? throw new InvalidOperationException(
        "Google Places API key is not configured."
      );

    using var request = new HttpRequestMessage(
      HttpMethod.Post,
      "https://places.googleapis.com/v1/places:searchText"
    );

    request.Headers.Add("X-Goog-Api-Key", apiKey);
    request.Headers.Add(
      "X-Goog-FieldMask",
      string.Join(",", Fields.Split(',').Select(x => "places." + x))
    );

    request.Content = JsonContent.Create(new { textQuery = query });

    var data = await SendAsync<PlacesResponse>(request, cancellationToken);
    return Read(data?.Places.FirstOrDefault());
  }

  private class PlacesResponse
  {
    [JsonPropertyName("places")]
    public List<Place> Places { get; set; } = [];
  }

  private class Place
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public DisplayName? DisplayName { get; set; }

    [JsonPropertyName("formattedAddress")]
    public string FormattedAddress { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public Location? Location { get; set; }

    [JsonPropertyName("businessStatus")]
    public string BusinessStatus { get; set; } = string.Empty;

    [JsonPropertyName("regularOpeningHours")]
    public JsonElement? RegularOpeningHours { get; set; }

    [JsonPropertyName("utcOffsetMinutes")]
    public int? UtcOffsetMinutes { get; set; }
  }

  private class DisplayName
  {
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
  }

  private class Location
  {
    [JsonPropertyName("latitude")]
    public decimal Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public decimal Longitude { get; set; }
  }
}
