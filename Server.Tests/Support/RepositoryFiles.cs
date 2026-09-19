namespace Server.Tests.Support;

internal static class RepositoryFiles
{
  private static readonly HashSet<string> ExcludedDirectories =
  [
    "bin",
    "obj",
    "node_modules",
    ".git",
    ".cache",
    "artifacts",
    "test-results",
  ];

  public static string Root()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (
      directory is not null
      && !File.Exists(Path.Combine(directory.FullName, "pulsartms.slnx"))
    )
      directory = directory.Parent;
    return directory?.FullName
      ?? throw new InvalidOperationException("Project root not found.");
  }

  public static IEnumerable<string> Sources(string directory, string pattern)
  {
    foreach (var file in Directory.EnumerateFiles(directory, pattern))
      yield return file;
    foreach (var child in new DirectoryInfo(directory).EnumerateDirectories())
    {
      // Prune outputs before traversal; release artifacts can contain entire
      // dependency trees.
      if (
        ExcludedDirectories.Contains(child.Name)
        || child.Attributes.HasFlag(FileAttributes.ReparsePoint)
      )
        continue;
      foreach (var file in Sources(child.FullName, pattern))
        yield return file;
    }
  }
}
