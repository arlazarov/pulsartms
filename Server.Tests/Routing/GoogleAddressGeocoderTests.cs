using System.Net;
using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Infrastructure.Integrations.Google.Places;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Routing;

[Trait("Category", "Addresses")]
[Trait("Kind", "Unit")]
public sealed class GoogleAddressGeocoderTests
{
  [Theory]
  [InlineData("ROOFTOP", false, "2045", "San Antonio", 1, true)]
  [InlineData("RANGE_INTERPOLATED", false, "2045", "San Antonio", 1, true)]
  [InlineData("APPROXIMATE", false, "2045", "San Antonio", 1, false)]
  [InlineData("GEOMETRIC_CENTER", false, "2045", "San Antonio", 1, false)]
  [InlineData("ROOFTOP", true, "2045", "San Antonio", 1, false)]
  [InlineData("ROOFTOP", false, "2046", "San Antonio", 1, false)]
  [InlineData("ROOFTOP", false, "2045", "Houston", 1, false)]
  [InlineData("ROOFTOP", false, "2045", "San Antonio", 2, false)]
  [InlineData("ROOFTOP", false, "2045", "San Antonio", 0, false)]
  public async Task RequiresAnUnambiguousMatchingStreetAddress(string precision, bool partial, string number, string city, int count, bool accepted)
  {
    var result = new {
      partial_match = partial,
      geometry = new { location_type = precision, location = new { lat = 29.407257, lng = -98.3613863 } },
      address_components = new[] {
        new { short_name = number, types = new[] { "street_number" } },
        new { short_name = "S Foster Rd", types = new[] { "route" } },
        new { short_name = city, types = new[] { "locality" } },
        new { short_name = "TX", types = new[] { "administrative_area_level_1" } },
        new { short_name = city == "Houston" ? "77001" : "78222", types = new[] { "postal_code" } },
        new { short_name = "US", types = new[] { "country" } }
      }
    };
    using var handler = new Handler(JsonSerializer.Serialize(new { status = "OK", results = Enumerable.Repeat(result, count) }));
    using var http = new HttpClient(handler);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GooglePlaces:ApiKey"] = "test" }).Build();
    var geocoder = new GoogleAddressGeocoder(http, configuration, cache);
    const string address = "2045 South Foster Road, San Antonio, TX, USA, 78222";
    if (!accepted)
    {
      var first = await Assert.ThrowsAsync<RoutePlanningException>(() => geocoder.GeocodeAsync(address, default));
      var second = await Assert.ThrowsAsync<RoutePlanningException>(() => geocoder.GeocodeAsync(address, default));
      Assert.Equal(first.RetryAfter, second.RetryAfter);
      Assert.Equal(count == 1 ? 2 : 1, handler.Calls);
    }
    else
    {
      Assert.Equal(29.407257, (await geocoder.GeocodeAsync(address, default)).Latitude);
      await geocoder.GeocodeAsync(address, default);
      Assert.Equal(1, handler.Calls);
    }
  }

  [Theory]
  [InlineData("178 Mooresville Blvd Attn Sam Mishler", "178", "Mooresville Blvd", "Mooresville", "NC", true)]
  [InlineData("1730 State Highway 5 S", "1730", "NY-5S", "Amsterdam", "NY", true)]
  [InlineData("1730 State Highway 5 S", "1730", "NY-5", "Amsterdam", "NY", false)]
  [InlineData("1730 State Highway 5 S", "1730", "NY-5N", "Amsterdam", "NY", false)]
  [InlineData("1730 State Highway 5 S", "1731", "NY-5S", "Amsterdam", "NY", false)]
  [InlineData("13077 SW Anthony F Sansone Sr Blvd", "13077", "SW Anthony F. Sansone Sr. Blvd", "Port St. Lucie", "FL", true, "Port Saint Lucie")]
  [InlineData("13077 SW Anthony F Sansone Sr Blvd", "13078", "SW Anthony F. Sansone Sr. Blvd", "Port St. Lucie", "FL", false, "Port Saint Lucie")]
  [InlineData("13077 SW Anthony F Sansone Sr Blvd", "13077", "SW Anthony F. Sansone Sr. Blvd", "Fort Pierce", "FL", false, "Port Saint Lucie")]
  [InlineData("13077 SW Anthony F Sansone Sr Blvd", "13077", "SW Anthony F. Sansone Sr. Blvd", "Port St. Lucie", "FL", true, "Port Saint Lucie", "34987")]
  [InlineData("13077 SW Anthony F Sansone Sr Blvd", "13077", "SW Anthony F. Sansone Sr. Blvd", "Port St. Lucie", "FL", false, "Port Saint Lucie", "34986")]
  [InlineData("13077 SW Anthony F Sansone Sr Blvd", "13077", "SW Anthony F. Sansone Sr. Blvd", "Port St. Lucie", "FL", true, "Postal city alias", "34987-1234")]
  public async Task AddressAliasesPreserveHouseAndHighwayNumbers(string street, string number, string route, string city, string region, bool accepted, string? inputCity = null, string? zip = null)
  {
    var result = new {
      geometry = new { location_type = "ROOFTOP", location = new { lat = 42.9379051, lng = -74.2410061 } },
      address_components = new[] {
        new { short_name = number, types = new[] { "street_number" } },
        new { short_name = route, types = new[] { "route" } },
        new { short_name = city, types = new[] { "locality" } },
        new { short_name = region, types = new[] { "administrative_area_level_1" } },
        new { short_name = "34987", types = new[] { "postal_code" } },
        new { short_name = "US", types = new[] { "country" } }
      }
    };
    using var handler = new Handler(JsonSerializer.Serialize(new { status = "OK", results = new[] { result } }));
    using var http = new HttpClient(handler);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GooglePlaces:ApiKey"] = "test" }).Build();
    var geocoder = new GoogleAddressGeocoder(http, configuration, cache);
    var address = $"{street}, {inputCity ?? city}, {region}, USA, {zip}";
    if (accepted) await geocoder.GeocodeAsync(address, default);
    else await Assert.ThrowsAsync<RoutePlanningException>(() => geocoder.GeocodeAsync(address, default));
    Assert.DoesNotContain("Attn", Uri.UnescapeDataString(handler.Query!), StringComparison.OrdinalIgnoreCase);
  }

  [Theory]
  [InlineData("ACCEPT", "CONFIRMED", false, "PREMISE", true)]
  [InlineData("CONFIRM", "CONFIRMED", false, "PREMISE", false)]
  [InlineData("ACCEPT", "UNCONFIRMED_BUT_PLAUSIBLE", false, "PREMISE", false)]
  [InlineData("ACCEPT", "CONFIRMED", true, "PREMISE", false)]
  [InlineData("ACCEPT", "CONFIRMED", false, "ROUTE", false)]
  public async Task CorrectionsRequireConfirmedPremiseComponents(string action, string confirmation, bool replaced, string granularity, bool accepted)
  {
    var geocode = JsonSerializer.Serialize(new { status = "OK", results = new[] { new {
      partial_match = true,
      geometry = new { location_type = "ROOFTOP", location = new { lat = 27.62, lng = -99.47 } },
      address_components = new[] { ("street_number", "11402"), ("route", "EastPoint Dr"), ("locality", "Laredo"), ("administrative_area_level_1", "TX"), ("country", "US"), ("postal_code", "78045") }
        .Select(x => new { short_name = x.Item2, types = new[] { x.Item1 } })
    } } });
    var validation = JsonSerializer.Serialize(new { result = new {
      verdict = new { addressComplete = true, possibleNextAction = action, validationGranularity = granularity, geocodeGranularity = "PREMISE" },
      address = new {
        postalAddress = new { regionCode = "US" },
        addressComponents = new[] { ("street_number", "11402"), ("route", "EastPoint Drive"), ("locality", "Laredo"), ("administrative_area_level_1", "TX"), ("postal_code", "78045") }
          .Select(x => new { componentType = x.Item1, componentName = new { text = x.Item2 }, confirmationLevel = confirmation, replaced })
      },
      geocode = new { location = new { latitude = 27.62, longitude = -99.47 } }
    } });
    using var handler = new Handler(geocode, validation);
    using var http = new HttpClient(handler);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GooglePlaces:ApiKey"] = "test" }).Build();
    var service = new GoogleAddressGeocoder(http, configuration, cache);
    const string input = "11402 East Point Drive, Laredo, TX, USA, 78045";
    for (var i = 0; i < 2; i++)
      if (accepted) Assert.Equal(27.62, (await service.GeocodeAsync(input, default)).Latitude);
      else await Assert.ThrowsAsync<RoutePlanningException>(() => service.GeocodeAsync(input, default));
    Assert.Equal(2, handler.Calls);
  }

  [Theory]
  [InlineData("1", "Arizona Wy", "NJ", "08832", "ROOFTOP", true)]
  [InlineData("2", "Arizona Wy", "NJ", "08832", "ROOFTOP", false)]
  [InlineData("1", "Arizona Rd", "NJ", "08832", "ROOFTOP", false)]
  [InlineData("1", "Arizona Wy", "NY", "08832", "ROOFTOP", false)]
  [InlineData("1", "Arizona Wy", "WY", "08832", "ROOFTOP", false)]
  [InlineData("1", "Arizona Wy", "NJ", "08831", "ROOFTOP", false)]
  [InlineData("1", "Arizona Wy", "NJ", "08832", "GEOMETRIC_CENTER", false)]
  public async Task WayAliasRequiresTheSameHouseStreetRegionAndPostalCode(string number, string street, string region, string postal, string precision, bool accepted)
  {
    using var handler = new Handler(Candidate(number, street, "Woodbridge Township", region, postal, precision));
    using var http = new HttpClient(handler);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var service = new GoogleAddressGeocoder(http, Configuration(), cache);
    const string input = "1 Arizona Way, KEASBEY, NJ, USA, 08832";
    if (accepted)
    {
      var result = await service.ResolveAsync(input, default);
      Assert.Equal("1 Arizona Wy", result.Address);
      Assert.Equal("Woodbridge Township", result.City);
      Assert.Equal("08832", result.ZipCode);
      Assert.Equal(1, handler.Calls);
    }
    else await Assert.ThrowsAsync<RoutePlanningException>(() => service.ResolveAsync(input, default));
  }

  [Theory]
  [InlineData("953309257", "95330-9257", "95330", true)]
  [InlineData("95330-9257", "95330-9257", "95330", true)]
  [InlineData("953309257", "95330-9257", "90210", false)]
  public async Task ZipPlusFourFormattingPreservesPostalMatchingAndSharesTheCanonicalCache(string inputPostal, string canonicalPostal, string resultPostal, bool accepted)
  {
    using var handler = new Handler(Candidate("16825", "Murphy Pkwy", "Lathrop", "CA", resultPostal));
    using var http = new HttpClient(handler);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var service = new GoogleAddressGeocoder(http, Configuration(), cache);
    var input = $"16825 Murphy Parkway, LATHROP, CA, USA, {inputPostal}";
    if (accepted)
    {
      await service.ResolveAsync(input, default);
      await service.ResolveAsync($"16825 Murphy Parkway, LATHROP, CA, USA, {canonicalPostal}", default);
      Assert.Equal(1, handler.Calls);
    }
    else await Assert.ThrowsAsync<RoutePlanningException>(() => service.ResolveAsync(input, default));
    Assert.Contains($"address=16825 Murphy Parkway, LATHROP, CA, USA, {canonicalPostal}&", Uri.UnescapeDataString(handler.GeocodeQuery!));
  }

  [Fact]
  public async Task ZipFormattingDoesNotRewriteAStreetBuildingNumber()
  {
    using var handler = new Handler(Candidate("123456789", "Murphy Pkwy", "Lathrop", "CA", "95330"));
    using var http = new HttpClient(handler);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var service = new GoogleAddressGeocoder(http, Configuration(), cache);
    await service.ResolveAsync("123456789 Murphy Parkway, LATHROP, CA, USA, 953309257", default);
    Assert.Contains("address=123456789 Murphy Parkway, LATHROP, CA, USA, 95330-9257&", Uri.UnescapeDataString(handler.GeocodeQuery!));
  }

  [Theory]
  [InlineData("1 Arizona Way, Keasbey, NJ, USA, 08832", "NJ", "US", true)]
  [InlineData("1 Arizona Way, Keasbey NJ 08832, USA", "NJ", "US", true)]
  [InlineData("1 Arizona Way, Keasbey, NJ, USA, 08832", "WY", "US", false)]
  [InlineData("1 CA Road, Toronto, ON, CA", "CA", "US", false)]
  [InlineData("1 CA Road, Toronto, ON, CA", "ON", "CA", true)]
  [InlineData("1 CA Road, Toronto ON M5V 1A1, Canada", "ON", "CA", true)]
  [InlineData("1 CA Road, Toronto, ON, Canada, CA", "ON", "CA", true)]
  [InlineData("1 CA Road, Lathrop, CA, USA, 95330", "CA", "US", true)]
  [InlineData("16825 Murphy Pkwy, Lathrop, CA, 95330", "CA", "US", true)]
  public async Task RegionMatchingUsesExplicitLocalityTokensNotStreetOrCountryAliases(string input, string region, string country, bool accepted)
  {
    var number = input.Split(' ')[0];
    var street = input.Split(',')[0][number.Length..].Trim();
    var city = input.Contains("Keasbey") ? "Keasbey" : input.Contains("Toronto") ? "Toronto" : "Lathrop";
    var postal = country == "CA" ? "M5V 1A1" : city == "Keasbey" ? "08832" : "95330";
    using var handler = new Handler(Candidate(number, street, city, region, postal, country: country));
    using var http = new HttpClient(handler);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var service = new GoogleAddressGeocoder(http, Configuration(), cache);
    if (accepted) await service.ResolveAsync(input, default);
    else await Assert.ThrowsAsync<RoutePlanningException>(() => service.ResolveAsync(input, default));
  }

  [Fact]
  public async Task AcceptedAddressValidationCannotTakeWyomingFromTheStreetAlias()
  {
    var validation = JsonSerializer.Serialize(new { result = new {
      verdict = new { addressComplete = true, possibleNextAction = "ACCEPT", validationGranularity = "PREMISE", geocodeGranularity = "PREMISE" },
      address = new {
        postalAddress = new { regionCode = "US" },
        addressComponents = new[] { ("street_number", "1"), ("route", "Arizona Way"), ("locality", "Woodbridge Township"),
          ("administrative_area_level_1", "WY"), ("postal_code", "08832") }
          .Select(x => new { componentType = x.Item1, componentName = new { text = x.Item2 }, confirmationLevel = "CONFIRMED" })
      },
      geocode = new { location = new { latitude = 40.5, longitude = -74.3 } }
    } });
    using var handler = new Handler(Candidate("1", "Arizona Wy", "Woodbridge Township", "WY", "08832", partial: true), validation);
    using var http = new HttpClient(handler);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var service = new GoogleAddressGeocoder(http, Configuration(), cache);
    await Assert.ThrowsAsync<RoutePlanningException>(() => service.ResolveAsync("1 Arizona Way, Keasbey, NJ, USA, 08832", default));
    Assert.Equal(2, handler.Calls);
  }

  private static IConfiguration Configuration() => new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["GooglePlaces:ApiKey"] = "test" }).Build();

  private static string Candidate(string number, string street, string city, string region, string postal, string precision = "ROOFTOP", bool partial = false, string country = "US") =>
    JsonSerializer.Serialize(new { status = "OK", results = new[] { new {
      partial_match = partial,
      geometry = new { location_type = precision, location = new { lat = 40.5, lng = -74.3 } },
      address_components = new[] { ("street_number", number), ("route", street), ("locality", city),
        ("administrative_area_level_1", region), ("country", country), ("postal_code", postal) }
        .Select(x => new { short_name = x.Item2, types = new[] { x.Item1 } })
    } } });

  private sealed class Handler(string json, string? validation = null) : HttpMessageHandler
  {
    public int Calls;
    public string? Query;
    public string? GeocodeQuery;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Calls++;
      Query = request.RequestUri!.Query;
      if (request.Method == HttpMethod.Get) GeocodeQuery = Query;
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Post ? validation ?? "{}" : json) });
    }
  }
}
