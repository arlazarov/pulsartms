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

  private sealed class Operation : IBackgroundOperation
  {
    public int Calls { get; private set; }

    public Task RunAsync(CancellationToken cancellationToken)
    {
      Calls++;
      return Task.CompletedTask;
    }
  }
}
