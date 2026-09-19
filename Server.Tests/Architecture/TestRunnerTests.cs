using System.Diagnostics;
using Server.Tests.Support;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class TestRunnerTests
{
  [Theory]
  [InlineData("map")]
  [InlineData("styles")]
  [InlineData("architecture")]
  [InlineData("addresses")]
  [InlineData("routing")]
  [InlineData("finance")]
  [InlineData("eta")]
  [InlineData("fleet")]
  [InlineData("dispatch")]
  [InlineData("fuel")]
  [InlineData("identity")]
  [InlineData("synchronization")]
  public async Task EveryFeatureRunsBothDotNetAssembliesAndArchitecture(
    string category
  )
  {
    var result = await RunAsync(category);
    Assert.Equal(0, result.ExitCode);
    Assert.Contains("DOTNET:test pulsartms.slnx", result.Output);
    Assert.Contains(
      $"--artifacts-path {Path.Combine(RepositoryFiles.Root(), "artifacts", "tests")}",
      result.Output
    );
    Assert.Contains("Category=Architecture", result.Output);
    Assert.Contains("NPM:run test:architecture --prefix Client", result.Output);
  }

  [Fact]
  public async Task MultipleFeaturesRunTheUnionOnce()
  {
    var result = await RunAsync("fuel", "identity");
    Assert.Equal(0, result.ExitCode);
    Assert.Single(
      result.Output.Split('\n'),
      line => line.StartsWith("DOTNET:")
    );
    Assert.Contains("Category=Fuel", result.Output);
    Assert.Contains("Category=Routing", result.Output);
    Assert.Contains("Category=Identity", result.Output);
    Assert.Contains("NPM:run test:identity --prefix Client", result.Output);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FullSuiteRunsUnfilteredDotNetAndAllJavaScript(
    bool explicitAll
  )
  {
    var result = await RunAsync(explicitAll ? ["all"] : []);
    Assert.Equal(0, result.ExitCode);
    Assert.Contains("DOTNET:test pulsartms.slnx", result.Output);
    Assert.Contains(
      $"--artifacts-path {Path.Combine(RepositoryFiles.Root(), "artifacts", "tests")}",
      result.Output
    );
    Assert.DoesNotContain("--filter", result.Output);
    Assert.Contains("NPM:test --prefix Client", result.Output);
  }

  [Fact]
  public async Task UnknownCategoryFailsBeforeRunningAnyChecks()
  {
    var result = await RunAsync("unknown");
    Assert.Equal(2, result.ExitCode);
    Assert.DoesNotContain("DOTNET:", result.Output);
    Assert.DoesNotContain("NPM:", result.Output);
  }

  private static async Task<(int ExitCode, string Output)> RunAsync(
    params string[] categories
  )
  {
    var start = new ProcessStartInfo("bash")
    {
      WorkingDirectory = RepositoryFiles.Root(),
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    start.ArgumentList.Add("-c");
    start.ArgumentList.Add(
      "dotnet() { printf 'DOTNET:%s\\n' \"$*\"; }; npm() { printf 'NPM:%s\\n' \"$*\"; }; source ./test.sh \"$@\""
    );
    start.ArgumentList.Add("test-runner-check");
    foreach (var category in categories)
      start.ArgumentList.Add(category);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    try
    {
      await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
      process.Kill(entireProcessTree: true);
      throw;
    }
    return (process.ExitCode, await output + await error);
  }
}
