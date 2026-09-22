using System.Text.Json;
using API.Controllers;
using Application;
using Application.Features.Synchronization.Queries;
using Application.Interfaces;
using Application.Models;
using Domain.Models.Eta;
using Infrastructure.Diagnostics;
using Infrastructure.Integrations.Samsara;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Integration")]
public sealed class MemoryDiagnosticsTests
{
  [Fact]
  public async Task QueryReadsAllRegisteredCachesWithoutStartingProviders()
  {
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddApplication();
    Infrastructure.DependencyInjection.AddInfrastructure(
      services,
      new ConfigurationBuilder().Build()
    );
    await using var provider = services.BuildServiceProvider();
    provider.GetRequiredService<IMemoryCache>().Set("private-key", "secret");
    var handler = new GetMemoryDiagnosticsHandler(
      provider.GetRequiredService<IRuntimeMemoryReader>(),
      provider.GetServices<ICacheMemorySource>()
    );
    var response = await handler.Handle(new(), default);
    Assert.True(response.Success);
    var result = response.Response!;
    Assert.Equal(Environment.ProcessId, result.Runtime.ProcessId);
    Assert.True(result.Runtime.WorkingSetBytes > 0);
    Assert.Equal(11, result.Caches.Count);
    Assert.Equal(11, result.Caches.Select(x => x.Name).Distinct().Count());
    var shared = Assert.Single(result.Caches, x => x.Name == "shared");
    Assert.True(shared.Entries >= 1);
    Assert.Null(shared.EstimatedSize);
    Assert.Null(shared.Limit);
    Assert.All(
      result.Caches.Where(x => x.Unit == "bytes"),
      x =>
      {
        Assert.Equal(0, x.Entries);
        Assert.Equal(0, x.EstimatedSize);
        Assert.True(x.Limit > 0);
      }
    );
    var json = JsonSerializer.Serialize(result);
    Assert.DoesNotContain("private-key", json);
    Assert.DoesNotContain("secret", json);
  }

  [Fact]
  public void HosStatisticsDescribeStoredEntriesRatherThanTheBudget()
  {
    using var cache = new SamsaraHosHistoryCache(
      TimeProvider.System,
      new TestCompany()
    );
    var now = DateTimeOffset.UtcNow;
    cache.Store(
      "driver",
      new(
        new HosHistory(
          now.AddHours(-1),
          now,
          "UTC",
          0,
          null,
          null,
          [new(now.AddHours(-1), now, "driving")]
        ),
        now,
        now,
        true
      )
    );
    var state = Assert.Single(cache.ReadMemory());
    Assert.Equal(1, state.Entries);
    Assert.InRange(state.EstimatedSize!.Value, 1024, 2048);
    Assert.Equal(16 * 1024 * 1024, state.Limit);
  }

  [Fact]
  public async Task CancellationDoesNotSampleTheRuntime()
  {
    var handler = new GetMemoryDiagnosticsHandler(new NeverRead(), []);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => handler.Handle(new(), new CancellationToken(true))
    );
  }

  [Fact]
  public void MemoryEndpointRetainsAdminAuthorization()
  {
    var controller = typeof(DiagnosticsController);
    var policies = controller
      .GetCustomAttributes(typeof(AuthorizeAttribute), true)
      .Cast<AuthorizeAttribute>();
    Assert.Contains(policies, x => x.Policy == "Admin");
    var method = controller.GetMethod(nameof(DiagnosticsController.Memory))!;
    Assert.Empty(
      method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
    );
  }

  [Fact]
  public async Task MemoryMapUsesRegisteredReaderAndRetainsAdminBoundary()
  {
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddApplication();
    Infrastructure.DependencyInjection.AddInfrastructure(
      services,
      new ConfigurationBuilder().Build()
    );
    await using var provider = services.BuildServiceProvider();
    var reader = provider.GetRequiredService<IProcessMemoryMapReader>();
    var handler = new GetProcessMemoryMapHandler(reader);
    var first = (await handler.Handle(new(), default)).Response!;
    var second = (await handler.Handle(new(), default)).Response!;
    Assert.Same(first, second);
    Assert.Equal(Environment.ProcessId, first.Runtime.ProcessId);
    Assert.All(first.Groups, x => Assert.True(x.VirtualBytes >= 0));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => handler.Handle(new(), new CancellationToken(true))
    );
    Assert.Empty(
      typeof(DiagnosticsController)
        .GetMethod(nameof(DiagnosticsController.MemoryMap))!
        .GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
    );
    Assert.Contains(
      typeof(DiagnosticsController)
        .GetCustomAttributes(typeof(AuthorizeAttribute), true)
        .Cast<AuthorizeAttribute>(),
      x => x.Policy == "Admin"
    );
  }

  private sealed class NeverRead : IRuntimeMemoryReader
  {
    public RuntimeMemorySnapshot Read() =>
      throw new InvalidOperationException(
        "Must not sample after cancellation."
      );
  }
}
