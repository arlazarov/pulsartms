using Application.Features.Fleet.Background;
using Application.Features.Synchronization.Interfaces;
using Application.Interfaces;
using Infrastructure.Synchronization;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class ApplicationWorkerTests
{
  [Theory]
  [InlineData(null, 1)]
  [InlineData("true", 1)]
  [InlineData("false", 0)]
  public async Task BackgroundSwitchControlsOperationExecution(
    string? enabled,
    int expectedCalls
  )
  {
    var settings = new Dictionary<string, string?>();
    if (enabled is not null)
      settings["BackgroundOperations:Enabled"] = enabled;
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(settings)
      .Build();
    var operation = new Operation();
    using var worker = new ApplicationWorker<IBackgroundOperation>(
      operation,
      configuration
    );
    await worker.StartAsync(default);
    await worker.ExecuteTask!;
    Assert.Equal(expectedCalls, operation.Calls);
    await worker.StopAsync(default);
  }

  // An instance that states roles runs only that work, so request serving and
  // background work can be separated without disabling either wholesale.
  [Theory]
  [InlineData("DriverHosRefresh", 1)]
  [InlineData("driverhosrefresh", 1)]
  [InlineData(" DriverHosRefresh ", 1)]
  [InlineData("DriverHosRefresh,PlanningRefresh", 1)]
  [InlineData("PlanningRefresh", 0)]
  [InlineData("IDriverHosRefreshOperation", 0)]
  [InlineData("", 1)]
  public async Task StatedRolesSelectWhichWorkAnInstanceRuns(
    string roles,
    int expectedCalls
  )
  {
    var settings = new Dictionary<string, string?>();
    var stated = roles.Split(',', StringSplitOptions.RemoveEmptyEntries);
    for (var index = 0; index < stated.Length; index++)
      settings[$"BackgroundOperations:Roles:{index}"] = stated[index];
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(settings)
      .Build();
    var operation = new Operation();
    using var worker = new ApplicationWorker<IDriverHosRefreshOperation>(
      operation,
      configuration
    );

    await worker.StartAsync(default);
    await worker.ExecuteTask!;

    Assert.Equal(expectedCalls, operation.Calls);
    await worker.StopAsync(default);
  }

  [Fact]
  public async Task DisablingBackgroundWorkOverridesAnyStatedRole()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["BackgroundOperations:Enabled"] = "false",
          ["BackgroundOperations:Roles:0"] = "DriverHosRefresh",
        }
      )
      .Build();
    var operation = new Operation();
    using var worker = new ApplicationWorker<IDriverHosRefreshOperation>(
      operation,
      configuration
    );

    await worker.StartAsync(default);
    await worker.ExecuteTask!;

    Assert.Equal(0, operation.Calls);
    await worker.StopAsync(default);
  }

  private sealed class Operation
    : IBackgroundOperation,
      IDriverHosRefreshOperation
  {
    public int Calls { get; private set; }

    public Task RunAsync(CancellationToken cancellationToken)
    {
      Calls++;
      return Task.CompletedTask;
    }
  }
}
