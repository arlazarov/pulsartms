using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Models;
using Application.Features.Routing.Queries;
using Application.Models;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Integration")]
public sealed class ApiContractTests
{
  [Fact]
  public async Task TelemetryPollsRevalidateThroughTheRealPipelineAndKeepTheirTagWhenCompressed()
  {
    await using var host = await ApiTestHost.StartAsync();
    host.Mediator.Script = request => RequestResponse<FleetLocationsResponse>.Ok(new()
      { Revision = "rev-9", Trucks = [new() { TruckId = Guid.NewGuid(), UnitNumber = "54777" }] });

    using var first = await host.Client.GetAsync("/api/fleet/locations?points=false");
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.Equal("W/\"rev-9\"", first.Headers.ETag!.ToString());
    Assert.True(first.Headers.CacheControl is { Private: true, NoCache: true, NoStore: false });
    Assert.Equal("application/json", first.Content.Headers.ContentType!.MediaType);
    var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
    Assert.True(body.GetProperty("success").GetBoolean());
    Assert.Equal("54777", body.GetProperty("response").GetProperty("trucks")[0].GetProperty("unitNumber").GetString());
    Assert.False(Assert.IsType<GetFleetLocationsQuery>(host.Mediator.Requests.Single()).IncludePoints);

    using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/fleet/locations?points=false");
    conditional.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse("W/\"rev-9\""));
    using var unchanged = await host.Client.SendAsync(conditional);
    Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
    Assert.Equal("W/\"rev-9\"", unchanged.Headers.ETag!.ToString());
    Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());

    using var compressed = new HttpRequestMessage(HttpMethod.Get, "/api/fleet/locations");
    compressed.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
    using var encoded = await host.Client.SendAsync(compressed);
    Assert.Equal(HttpStatusCode.OK, encoded.StatusCode);
    Assert.Equal("br", encoded.Content.Headers.ContentEncoding.Single());
    // Weak validators survive compression; a strong one would be dropped by the middleware.
    Assert.Equal("W/\"rev-9\"", encoded.Headers.ETag!.ToString());
  }

  [Fact]
  public async Task PlanningPollsAcceptGetAndPostWithOneDigestTag()
  {
    await using var host = await ApiTestHost.StartAsync();
    var truck = Guid.NewGuid();
    var plan = Guid.NewGuid();
    var dispatch = Guid.NewGuid();
    host.Mediator.Script = request => RequestResponse<AutomaticPlanningResult>.Ok(new(truck, dispatch, 1441, null, "No remaining stops."));

    using var first = await host.Client.GetAsync($"/api/fleet/trucks/{truck}/planning?knownPlanId={plan}&knownVersion=3");
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.Equal("application/json", first.Content.Headers.ContentType!.MediaType);
    Assert.Equal("utf-8", first.Content.Headers.ContentType.CharSet);
    Assert.True(first.Headers.CacheControl is { Private: true, NoCache: true, NoStore: false });
    var tag = first.Headers.ETag!.ToString();
    Assert.StartsWith("W/\"", tag);
    var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal(truck, body.GetProperty("response").GetProperty("truckId").GetGuid());
    Assert.Equal(1441, body.GetProperty("response").GetProperty("loadNumber").GetInt32());
    var query = Assert.IsType<GetTruckPlanningQuery>(host.Mediator.Requests.Single());
    Assert.Equal((truck, plan, 3), (query.TruckId, query.KnownPlanId, query.KnownVersion));

    using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/fleet/trucks/{truck}/planning?knownPlanId={plan}&knownVersion=3");
    conditional.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(tag));
    using var unchanged = await host.Client.SendAsync(conditional);
    Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
    Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());

    // Browsers still running an older Client post to the same URL.
    using var legacy = await host.Client.PostAsync($"/api/fleet/trucks/{truck}/planning", null);
    Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
    Assert.Equal(tag, legacy.Headers.ETag!.ToString());
    Assert.Null(Assert.IsType<GetTruckPlanningQuery>(host.Mediator.Requests.Last()).KnownPlanId);
  }

  [Fact]
  public async Task FailedReadsKeepTheirStatusCodeAndWrappedBody()
  {
    await using var host = await ApiTestHost.StartAsync();
    host.Mediator.Script = request => RequestResponse<AutomaticPlanningResult>.Fail("Planning is unavailable.", 503);
    using var response = await host.Client.GetAsync($"/api/dispatch/{Guid.NewGuid()}/planning/automatic");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    Assert.Null(response.Headers.ETag);
    var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    Assert.False(body.GetProperty("success").GetBoolean());
    Assert.Equal("Planning is unavailable.", body.GetProperty("errors")[0].GetString());
  }
}
