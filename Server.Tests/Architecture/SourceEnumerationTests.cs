using Server.Tests.Support;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class SourceEnumerationTests
{
  [Fact]
  public void ArchitectureScansExcludeBuildArtifactsAndDependenciesBeforeRecursing()
  {
    var directory = Path.Combine(Path.GetTempPath(), "amftms-source-scan-" + Guid.NewGuid().ToString("N"));
    try
    {
      foreach (var name in new[] { "source", "bin", "obj", "node_modules", ".git", ".cache", "artifacts", "test-results" })
      {
        var child = Directory.CreateDirectory(Path.Combine(directory, name));
        File.WriteAllText(Path.Combine(child.FullName, "example.cs"), "");
      }
      Assert.Equal([Path.Combine(directory, "source", "example.cs")], RepositoryFiles.Sources(directory, "*.cs").ToArray());
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
  }
}
