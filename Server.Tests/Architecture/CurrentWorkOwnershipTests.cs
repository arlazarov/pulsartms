using System.Text.RegularExpressions;

namespace Server.Tests.Architecture;

// Which work a truck is on has one rule, PlanningWorkPolicy.ChooseCurrent,
// and one owner of its answer, TruckPlanningInputsReader. A reader that
// walks the candidates or judges their completion itself is a second rule
// (docs/architecture/current-work.md). The remaining uses are listed with
// what they do; a new one is a design change to review, not an entry to
// add.
[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed partial class CurrentWorkOwnershipTests
{
  private static readonly Dictionary<string, int> CandidateReads = new()
  {
    // Bounds its own passes by the truck's candidates.
    ["Features/Routing/Services/Routes/AutomaticPlanningService.cs"] = 1,
    // Whether the truck has any planning work at all.
    ["Features/Routing/Services/Routes/PlanningReadService.cs"] = 1,
    // Finds a dispatch the caller named.
    ["Features/Routing/Services/Routes/PlanningSummaryReader.cs"] = 1,
    // Reads saved plans lazily and asks PlanningWorkPolicy.IsPassed of each.
    ["Features/Routing/Services/Routes/PlanningCurrency.cs"] = 1,
  };

  private static readonly Dictionary<string, int> CompletionChecks = new()
  {
    // A plan the owner chose found passed since: a change, not a step.
    ["Features/Routing/Services/Routes/PlanningReadService.cs"] = 1,
    // The tracking writer judging the plan its own pass just advanced.
    ["Features/Routing/Services/Routes/AutomaticPlanningService.cs"] = 1,
  };

  [Fact]
  public void OnlyTheOwnerChoosesTheCurrentWork()
  {
    Assert.Equal(CandidateReads, Count(CandidateRead()));
    Assert.Equal(CompletionChecks, Count(CompletionCheck()));
  }

  private static Dictionary<string, int> Count(Regex pattern)
  {
    var root = Support.RepositoryFiles.Root();
    var counts = new Dictionary<string, int>();
    foreach (
      var layer in new[]
      {
        "Server/Application",
        "Server/Infrastructure",
        "Server/API",
      }
    )
    {
      var directory = Path.Combine(root, layer);
      foreach (var file in Support.RepositoryFiles.Sources(directory, "*.cs"))
      {
        var found = pattern.Matches(File.ReadAllText(file)).Count;
        if (found > 0)
          counts[
            Path.GetRelativePath(Path.Combine(root, "Server/Application"), file)
              .Replace('\\', '/')
          ] = found;
      }
    }
    return counts;
  }

  [GeneratedRegex(@"PlanningWorkPolicy\s*\.\s*Candidates\s*\(")]
  private static partial Regex CandidateRead();

  [GeneratedRegex(@"PlanningWorkPolicy\s*\.\s*IsCompleted\s*\(")]
  private static partial Regex CompletionCheck();
}
