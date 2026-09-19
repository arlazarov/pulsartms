using Application.Features.Synchronization.Interfaces;
using Application.Features.Fuel.Interfaces;
using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class HostingRoleRegistrationTests
{
  [Theory]
  [InlineData(null, true)]
  [InlineData("All", true)]
  [InlineData("Workers", true)]
  [InlineData("Api", false)]
  public void ApiRoleKeepsOnlyTheFollowerAndTheMigrationGate(string? role, bool workers)
  {
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
      { ["Hosting:Role"] = role, ["Gmail:BackgroundMaintenanceEnabled"] = "true" }).Build();
    var hosted = new ServiceCollection().AddInfrastructure(configuration)
      .Where(x => x.ServiceType == typeof(IHostedService)).Select(x => x.ImplementationType!).ToHashSet();
    Assert.Contains(typeof(DatabaseInitializer), hosted);
    Assert.Contains(typeof(ApplicationWorker<IFleetSynchronizationOperation>), hosted);
    foreach (var worker in new[] { typeof(ApplicationWorker<IGmailWatchOperation>), typeof(ApplicationWorker<IEtaRefreshOperation>),
      typeof(ApplicationWorker<ITruckHistoryOperation>), typeof(ApplicationWorker<IPlanningRefreshOperation>), typeof(ApplicationWorker<IBaseRouteOperation>) })
      Assert.Equal(workers, hosted.Contains(worker));
    // Framework services (health check publishing) may host themselves; application workers are the only ones gated.
    if (!workers) Assert.Equal([typeof(ApplicationWorker<IFleetSynchronizationOperation>)],
      hosted.Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApplicationWorker<>)));
  }

  [Fact]
  public void UnknownRoleFailsAtStartup()
  {
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
      { ["Hosting:Role"] = "Edge" }).Build();
    Assert.ThrowsAny<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(configuration));
  }
}
