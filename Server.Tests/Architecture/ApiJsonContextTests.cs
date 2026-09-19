using System.Text.Json;
using API;
using Application.Features.Dispatch.Models;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
using Application.Models;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class ApiJsonContextTests
{
  [Fact]
  public void EveryRequestResponseShapeHasGeneratedMetadata()
  {
    var responses = typeof(Application.DependencyInjection).Assembly.GetTypes()
      .Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
      .SelectMany(type => type.GetInterfaces())
      .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequest<>))
      .Select(contract => contract.GetGenericArguments()[0])
      .Where(response => response.IsGenericType && response.GetGenericTypeDefinition() == typeof(RequestResponse<>))
      .Distinct().OrderBy(type => type.FullName).ToArray();
    Assert.NotEmpty(responses);
    var missing = responses.Where(type => ApiJsonContext.Default.GetTypeInfo(type) is null).Select(type => type.FullName).ToArray();
    Assert.Empty(missing);
  }

  [Fact]
  public void GeneratedMetadataWritesTheSameJsonAsReflection()
  {
    var builder = WebApplication.CreateBuilder();
    builder.AddApplicationServices();
    using var provider = builder.Services.BuildServiceProvider();
    var mvc = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
    Assert.Same(ApiJsonContext.Default, mvc.TypeInfoResolverChain[0]);
    var reflection = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    var samples = new object[]
    {
      RequestResponse<FleetLocationsResponse>.Ok(new()
      {
        Revision = "abc-1",
        Trucks = [new() { TruckId = Guid.NewGuid(), TruckExternalId = "t-1", UnitNumber = "101", DriverName = "Driver",
          Latitude = 40.5m, Longitude = -80.25m, Speed = 55, Heading = 270, UpdatedAt = now, ObservedAt = now,
          FormattedLocation = "Pittsburgh, PA", EngineState = "On", FuelPercent = 62.5m, FuelUpdatedAt = now }],
        Points = [new("t-1", 40.4m, -80.2m, 50, 265, now.AddMinutes(-1))]
      }),
      RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(new()
      {
        Page = 1, PageSize = 12, TotalCount = 1,
        Items = [new() { Key = "k", TruckId = Guid.NewGuid(), TruckNumber = "101", DriverName = "Driver",
          Dispatches = [new() { Id = Guid.NewGuid(), LoadNumber = 7, Status = "in_transit",
            Stops = [new() { Id = Guid.NewGuid(), Job = "Pick Up", City = "Erie", ScheduledDate = new(2026, 9, 20), ScheduledTime = new(8, 30) }] }] }]
      }),
      RequestResponse<RoutePlanningState>.Ok(new(new(), null, null, 12.5, now, true)),
      RequestResponse<object>.Fail("Unavailable.", 503)
    };
    foreach (var sample in samples)
      Assert.Equal(JsonSerializer.Serialize(sample, sample.GetType(), reflection), JsonSerializer.Serialize(sample, sample.GetType(), mvc));
  }
}
