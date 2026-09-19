using System.Text.RegularExpressions;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed partial class ProcessStateTests
{
  // Gates come from the ProcessGates singleton. The only static process state left is the
  // per-process request counter behind the diagnostics endpoint, which is process-wide by design
  // like the meter it accompanies. This list may only shrink.
  private static readonly string[] Debt = ["Behaviors/RequestDiagnosticsBehavior.cs"];

  [GeneratedRegex(@"static\s+readonly\s+(?:SemaphoreSlim|KeyedGates|System\.Collections\.Concurrent\.\w+<)")]
  private static partial Regex StaticState();

  [Fact]
  public void StaticGatesAndCountersStayWithinListedDebt()
  {
    var root = Path.Combine(Root(), "Server/Application");
    var owners = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
      .Where(file => StaticState().IsMatch(File.ReadAllText(file)))
      .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).Order().ToArray();
    Assert.Equal(Debt.Order(), owners);
  }
}
