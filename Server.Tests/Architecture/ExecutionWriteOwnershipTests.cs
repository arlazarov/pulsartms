using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class ExecutionWriteOwnershipTests
{
  [Theory]
  [InlineData("ExecutionStopRows.Replace(")]
  [InlineData("ExecutionHistory.RecordAsync(")]
  [InlineData("ExecutionPlanningChanges.Enqueue(")]
  public void AcceptedWorkHasOneStopHistoryAndPlanningWriter(string operation)
  {
    var root = Path.Combine(Root(), "Server", "Application");
    var owner = Path.Combine(
      root,
      "Features",
      "Execution",
      "Services",
      "ExecutionAcceptance.cs"
    );
    var writers = Sources(root, "*.cs")
      .Where(file => File.ReadAllText(file).Contains(operation))
      .ToArray();
    Assert.Equal(owner, Assert.Single(writers));
  }
}
