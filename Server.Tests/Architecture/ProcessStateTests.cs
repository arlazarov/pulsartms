using System.Text.RegularExpressions;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed partial class ProcessStateTests
{
  // Static gates and counters are process-wide state shared by every request and by parallel
  // tests. Per-service stripes moved to ProcessGates; these remaining owners are debt that may
  // only shrink, each moving to a DI singleton when its feature is next touched.
  private static readonly string[] Debt =
  [
    "Behaviors/RequestDiagnosticsBehavior.cs",
    "Features/Fleet/Queries/GetFleetLocations/GetTruckHistory.cs",
    "Features/Routing/Services/FuelPlanning/FuelPlanningService.cs",
    "Features/Routing/Services/Routes/PlanningSettingsService.cs",
    "Features/Routing/Services/Routes/RoutePreviewService.cs",
    "Features/Routing/Services/Routes/RouteRecalculationBudget.cs",
    "Features/Synchronization/Services/SynchronizationGates.cs",
  ];

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
