using Application.Features.Routing.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class RouteBudgetConfigurationTests
{
  [Fact]
  public async Task ApiDisablesTruckBudgetWithoutChangingGlobalProviderLimits()
  {
    var builder = WebApplication.CreateBuilder();
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddJsonFile(Path.Combine(RepositoryFiles.Root(), "Server/API/appsettings.json"));
    API.DependencyInjection.AddApplicationServices(builder);
    await using var services = builder.Services.BuildServiceProvider();
    Assert.False(services.GetRequiredService<IOptions<RouteRecalculationBudgetOptions>>().Value.Enabled);
    Assert.Equal(1000, builder.Configuration.GetValue<int>("TomTom:DailyRequestLimit"));
    Assert.Equal(30, builder.Configuration.GetValue<int>("TomTom:RequestsPerMinute"));
  }
}
