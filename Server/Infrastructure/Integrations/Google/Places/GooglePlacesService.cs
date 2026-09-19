using System.Net.Http.Json;
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
      "places.id,places.displayName,places.formattedAddress,places.location"
    );

    request.Content = JsonContent.Create(new { textQuery = query });

    var data = await SendAsync<PlacesResponse>(request, cancellationToken);
    var place = data?.Places.FirstOrDefault();

    if (place is null)
    {
      return null;
    }

    return new PlaceSearchResult
    {
      PlaceId = place.Id,
      Name = place.DisplayName?.Text ?? string.Empty,
      Address = place.FormattedAddress,
      Latitude = place.Location?.Latitude ?? 0,
      Longitude = place.Location?.Longitude ?? 0,
    };
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
