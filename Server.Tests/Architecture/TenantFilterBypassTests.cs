using System.Text.RegularExpressions;
using Server.Tests.Support;

namespace Server.Tests.Architecture;

// The company filter covers LINQ over carrier tables. Raw SQL and
// IgnoreQueryFilters step outside it, so each file that does is listed
// here with why its rows stay the serving carrier's (or are nobody's). A
// new file stepping outside fails until it is reviewed and listed.
[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed partial class TenantFilterBypassTests
{
  private static readonly Dictionary<string, string> Reviewed = new()
  {
    ["Server/Infrastructure/Persistence/AppDbContext.cs"] =
      "advisory and row locks by id; reads no carrier rows",
    ["Server/Infrastructure/Persistence/DatabaseInitializer.cs"] =
      "schema start-up: migrations and their locks",
    ["Server/Infrastructure/Persistence/ConversationReadMarkers.cs"] =
      "upsert that writes the serving CompanyId and conflicts on it",
    ["Server/Infrastructure/Persistence/DeadheadHistoryReader.cs"] =
      "unnests captured inputs; the rows it joins come through LINQ",
    ["Server/Infrastructure/Persistence/EtaForecastStore.cs"] =
      "upsert that writes the serving CompanyId",
    ["Server/Infrastructure/Persistence/ExecutionPlanningStore.cs"] =
      "claims from a shared work queue (SharedTables)",
    ["Server/Infrastructure/Persistence/IntegrationCredentialStore.cs"] =
      "detects a provider account another carrier already holds; "
      + "answers yes or no, never returns the values (audit F27)",
    ["Server/Infrastructure/Persistence/PlanningPublicationScope.cs"] =
      "advisory locks by truck id; reads no carrier rows",
    ["Server/Infrastructure/Persistence/PlanningRefreshStore.cs"] =
      "shared work queue (SharedTables); inserts name the serving CompanyId",
    ["Server/Infrastructure/Persistence/SavedRoutePlanReader.cs"] =
      "names the serving CompanyId in its WHERE",
    ["Server/Infrastructure/Persistence/SourceRoadStore.cs"] =
      "shared work queue (SharedTables); inserts name the serving CompanyId",
    ["Server/Infrastructure/Persistence/TruckFuelPlanStore.cs"] =
      "upsert that writes and conflicts on the serving CompanyId",
    ["Server/Infrastructure/Identity/LiveAccounts.cs"] =
      "sign-in: finds the account before any carrier is known",
    ["Server/Infrastructure/Identity/UserRoleService.cs"] =
      "an account's role by its identity id, across carriers by design",
    [
      "Server/Infrastructure/Integrations/TomTom/TomTomRoutingProvider.Budget.cs"
    ] = "shared provider budget table (SharedTables)",
  };

  [Fact]
  public void EveryStepOutsideTheCompanyFilterIsReviewed()
  {
    var root = RepositoryFiles.Root();
    var found = new[] { "Server/Application", "Server/Infrastructure" }
      .SelectMany(x => RepositoryFiles.Sources(Path.Combine(root, x), "*.cs"))
      .Where(x => !x.Contains("/Migrations/", StringComparison.Ordinal))
      .Where(x => Bypass().IsMatch(File.ReadAllText(x)))
      .Select(x => Path.GetRelativePath(root, x).Replace('\\', '/'))
      .Order(StringComparer.Ordinal)
      .ToArray();

    Assert.Equal(Reviewed.Keys.Order(StringComparer.Ordinal), found);
  }

  [GeneratedRegex(@"IgnoreQueryFilters\(|FromSql|ExecuteSql|SqlQuery")]
  private static partial Regex Bypass();
}
