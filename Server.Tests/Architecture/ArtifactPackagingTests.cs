using System.Diagnostics;
using System.Text.Json;
using Server.Tests.Support;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class ArtifactPackagingTests
{
  [Fact]
  public void CloudSourceIncludesPackagingRulesRequiredByReleaseChecks()
  {
    var patterns = File.ReadAllLines(
        Path.Combine(RepositoryFiles.Root(), ".gcloudignore")
      )
      .Select(line => line.Trim())
      .ToArray();
    foreach (var file in new[] { ".gcloudignore", ".dockerignore" })
    {
      Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root(), file)));
      Assert.Contains("!" + file, patterns);
      Assert.DoesNotContain(file, patterns);
    }
  }

  [Theory]
  [InlineData(".gcloudignore")]
  [InlineData(".dockerignore")]
  public void BuildInputsExcludeRawCapturesAndRootOrNestedEnvironmentFiles(
    string file
  )
  {
    string[] required =
    [
      "docs/archive/**/*.jsonl",
      ".env",
      ".env.*",
      "**/.env",
      "**/.env.*",
    ];
    var patterns = File.ReadAllLines(Path.Combine(RepositoryFiles.Root(), file))
      .Select(line => line.Trim())
      .Where(line => line.Length > 0 && !line.StartsWith('#'))
      .ToArray();
    Assert.Equal(required, patterns.TakeLast(required.Length));
  }

  [Fact]
  public void CloudSourceIncludesMaintainedToolsForArchitectureAuditButDockerExcludesThem()
  {
    var root = RepositoryFiles.Root();
    var cloud = File.ReadAllLines(Path.Combine(root, ".gcloudignore"))
      .Select(line => line.Trim())
      .ToArray();
    var docker = File.ReadAllLines(Path.Combine(root, ".dockerignore"))
      .Select(line => line.Trim())
      .ToArray();
    Assert.DoesNotContain("tools/", cloud);
    Assert.Contains("tools/**/*.bundle.js", cloud);
    Assert.Contains("tools/**/*.jsonl", cloud);
    Assert.Contains("tools/", docker);
    foreach (
      var file in new[]
      {
        "tools/LoadProbe/Program.cs",
        "tools/RouteMemoryProbe/Program.cs",
        "tools/MapLoadProbe/probe.js",
        "tools/map-performance-probe.mjs",
      }
    )
      Assert.True(
        File.Exists(Path.Combine(root, file)),
        $"Missing maintained tool source: {file}"
      );
  }

  [Fact]
  public async Task ApiContentEvaluationExcludesCredentialFiles()
  {
    var result = await EvaluateAsync("-getItem:Content,None");
    Assert.Equal(0, result.ExitCode);
    using var json = JsonDocument.Parse(result.Output);
    foreach (
      var group in json.RootElement.GetProperty("Items").EnumerateObject()
    )
    foreach (var item in group.Value.EnumerateArray())
    {
      var path = item.GetProperty("Identity").GetString()!;
      Assert.DoesNotContain("gmail-credentials.json", path);
      Assert.DoesNotContain("gmail-token", path);
      Assert.DoesNotContain("secrets.json", path);
      Assert.DoesNotContain("appsettings.Local.json", path);
      var name = Path.GetFileName(path);
      Assert.False(
        name == ".env" || name.StartsWith(".env.", StringComparison.Ordinal)
      );
    }
  }

  [Theory]
  [InlineData("gmail-credentials.json")]
  [InlineData("gmail-token/token.json")]
  [InlineData("secrets.json")]
  [InlineData("appsettings.Local.json")]
  [InlineData(".env")]
  [InlineData(".env.production")]
  [InlineData("nested/.env")]
  [InlineData("nested/.env.production")]
  public async Task PublishGuardRejectsStaleCredentialArtifacts(string artifact)
  {
    var directory = Directory.CreateTempSubdirectory(
      "pulsartms-publish-guard-"
    );
    try
    {
      var args = new[]
      {
        "-t:RejectSecretPublishArtifacts",
        $"-p:PublishDir={directory.FullName}/",
      };
      Assert.Equal(0, (await EvaluateAsync(args)).ExitCode);
      var path = Path.Combine(directory.FullName, artifact);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      await File.WriteAllTextAsync(path, "{}");
      var result = await EvaluateAsync(args);
      Assert.NotEqual(0, result.ExitCode);
      Assert.Contains(
        "Publish output contains credential files",
        result.Output
      );
    }
    finally
    {
      directory.Delete(recursive: true);
    }
  }

  private static async Task<(int ExitCode, string Output)> EvaluateAsync(
    params string[] args
  )
  {
    var start = new ProcessStartInfo("dotnet")
    {
      WorkingDirectory = RepositoryFiles.Root(),
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (
      var argument in new[]
      {
        "msbuild",
        "Server/API/API.csproj",
        "-verbosity:quiet",
        "-nologo",
      }.Concat(args)
    )
      start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
