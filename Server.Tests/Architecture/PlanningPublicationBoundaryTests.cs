using System.Text.RegularExpressions;
using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class PlanningPublicationBoundaryTests
{
  [Fact]
  public void WriterRevisionsRetainOldAndNewMembershipAndIndependentRates()
  {
    var migration = WriterMigration();
    Assert.Contains("AFTER INSERT OR UPDATE OR DELETE", migration);
    Assert.Contains("to_jsonb(OLD)", migration);
    Assert.Contains("to_jsonb(NEW)", migration);
    Assert.Contains("5ea9c4aa-c4d8-4d6a-bb82-e0be7a6ab86c", migration);
    Assert.Contains("FOR SHARE", migration);
    Assert.Contains("ORDER BY id", migration);
  }

  [Fact]
  public void PublicationProtectsWorkHistorySettingsAndSavedRoadSources()
  {
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite("Data Source=:memory:")
        .Options
    );
    var sets = typeof(IAppDbContext)
      .GetProperties()
      .Where(x =>
        x.PropertyType.IsGenericType
        && x.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
      )
      .ToDictionary(
        x => x.Name,
        x =>
          db.Model.FindEntityType(x.PropertyType.GenericTypeArguments[0])!
            .GetTableName()!
      );
    var root = Path.Combine(Root(), "Server");
    var readers = new[]
    {
      "Application/Features/Execution/Services/TruckItineraryReader.cs",
      "Application/Features/Execution/Services/ExecutionWorkReader.cs",
      "Application/Features/Execution/Services/WorkSequenceReader.cs",
      "Application/Features/Execution/Queries/GetTruckExecutionLoads.cs",
      "Application/Features/Routing/Services/Deadheads/DeadheadHistoryService.cs",
      "Infrastructure/Persistence/DeadheadHistoryReader.cs",
      "Infrastructure/Persistence/SavedRoutePlanReader.cs",
      "Infrastructure/Persistence/NextLoadRouteReader.cs",
      "Application/Features/Routing/Services/Routes/TruckPlanningProfileService.cs",
      "Application/Features/Routing/Services/Routes/PlanningSettingsService.cs",
    };
    var source = string.Join(
      "\n",
      readers.Select(file => File.ReadAllText(Path.Combine(root, file)))
    );
    var queried = Regex
      .Matches(source, @"\b(?:db|dbContext)\s*\.\s*(\w+)")
      .Select(x => x.Groups[1].Value)
      .Where(sets.ContainsKey)
      .Select(x => sets[x])
      .ToHashSet();
    queried.UnionWith(
      Regex
        .Matches(
          source,
          @"\b(?:FROM|JOIN)\s+""(\w+)""",
          RegexOptions.IgnoreCase
        )
        .Select(x => x.Groups[1].Value)
        .Where(sets.Values.Contains)
    );
    foreach (
      var navigation in db
        .Model.GetEntityTypes()
        .SelectMany(x => x.GetNavigations())
    )
      if (Regex.IsMatch(source, @"\.\s*" + navigation.Name + @"\b"))
        queried.Add(navigation.TargetEntityType.GetTableName()!);
    var scope = File.ReadAllText(
      Path.Combine(
        Root(),
        "Server/Infrastructure/Persistence/PlanningPublicationScope.cs"
      )
    );
    var migration = WriterMigration();
    var catalog = migration[
      ..migration.IndexOf(
        "private static void InstallPlanningInputRevisions",
        StringComparison.Ordinal
      )
    ];
    var locked = Regex
      .Matches(catalog, "\"([A-Z]\\w+)\"")
      .Select(x => x.Groups[1].Value)
      .ToHashSet();
    queried.UnionWith(["DispatchRouteChoices", "SynchronizationCheckpoints"]);

    Assert.NotEmpty(queried);
    Assert.Equal(queried.Order(), locked.Order());
    Assert.Contains("FOR SHARE NOWAIT", scope);
    Assert.Contains("FOR UPDATE NOWAIT", scope);
    Assert.Contains("RequireRevisionAsync(Guid.Empty, !scoped, ct)", scope);
    Assert.Contains("RequireRevisionAsync(truckId!.Value, true, ct)", scope);
    Assert.Contains("transaction.DisposeAsync()", scope);
  }

  [Fact]
  public void StoredExchangeRatesJoinPublicationWithoutLockingAllCheckpoints()
  {
    var root = Path.Combine(Root(), "Server/Infrastructure/Persistence");
    var source = File.ReadAllText(
      Path.Combine(root, "FuelExchangeRateStore.cs")
    );
    var begin = source.IndexOf(
      "publication.BeginAsync",
      StringComparison.Ordinal
    );
    var write = source.IndexOf(
      "checkpoint.SaveAsync",
      StringComparison.Ordinal
    );
    var commit = source.IndexOf(
      "transaction.CommitAsync",
      StringComparison.Ordinal
    );

    Assert.True(begin >= 0 && begin < write && write < commit);
    Assert.Single(Regex.Matches(source, "checkpoint\\.SaveAsync"));
    Assert.Contains("IPlanningPublicationScope publication", source);
    var scope = File.ReadAllText(
      Path.Combine(root, "PlanningPublicationScope.cs")
    );
    var locked = Regex
      .Matches(scope, "\"([A-Z]\\w+)\"")
      .Select(x => x.Groups[1].Value);
    Assert.DoesNotContain("SynchronizationCheckpoints", locked);
  }

  [Fact]
  public void EtaSettingsAndRoadsAreValidatedBeforeTheForecastWrite()
  {
    var source = File.ReadAllText(
      Path.Combine(
        Root(),
        "Server/Application/Features/Eta/Services/EtaForecastService.cs"
      )
    );
    var begin = source.IndexOf(
      "publication.BeginAsync",
      StringComparison.Ordinal
    );
    var profile = source.IndexOf(
      "inputs.RequireCurrentProfileAsync",
      StringComparison.Ordinal
    );
    var roads = source.IndexOf(
      "inputs.RequireCurrentRoadsAsync",
      StringComparison.Ordinal
    );
    var save = source.IndexOf("store.SaveAsync", StringComparison.Ordinal);
    var commit = source.IndexOf(
      "transaction.CommitAsync",
      StringComparison.Ordinal
    );

    Assert.True(
      begin >= 0
        && begin < profile
        && profile < roads
        && roads < save
        && save < commit
    );
  }

  [Fact]
  public void FuelRoadsAreValidatedBeforeProfileAndResultWrites()
  {
    var source = File.ReadAllText(
      Path.Combine(
        Root(),
        "Server/Application/Features/Routing/Services/FuelPlanning/FuelPlanningService.cs"
      )
    );
    var begin = source.IndexOf(
      "publication.BeginAsync",
      StringComparison.Ordinal
    );
    var roads = source.IndexOf(
      "roads.RequireCurrentAsync",
      StringComparison.Ordinal
    );
    var profile = source.IndexOf(
      "profiles.SaveAsync",
      StringComparison.Ordinal
    );
    var route = source.IndexOf(
      "routeStore.StoreFuelAsync",
      StringComparison.Ordinal
    );
    var truck = source.IndexOf(
      "savedPlans.ReplaceAsync",
      StringComparison.Ordinal
    );
    var commit = source.IndexOf(
      "transaction.CommitAsync",
      StringComparison.Ordinal
    );

    Assert.True(
      begin >= 0
        && begin < roads
        && roads < profile
        && profile < route
        && route < truck
        && truck < commit
    );
  }

  [Theory]
  [InlineData("BaseRouteService", "EnsureCoreAsync(", "saved.InputHash =")]
  [InlineData("RouteChoiceService", "PreviewAsync(", "drafts.StoreAsync")]
  [InlineData("RouteChoiceService", "SaveAsync(", "db.LockExecutionLegAsync")]
  public void BaseAndChoiceSettingsValidationPrecedesPublicationWrites(
    string owner,
    string method,
    string write
  )
  {
    var source = File.ReadAllText(
      Path.Combine(
        Root(),
        "Server/Application/Features/Routing/Services/Routes/" + owner + ".cs"
      )
    );
    var start = source.IndexOf(method, StringComparison.Ordinal);
    var begin = source.IndexOf(
      "publication.BeginAsync",
      start,
      StringComparison.Ordinal
    );
    var validate = source.IndexOf(
      "profiles.RequireRoutingCurrentAsync",
      begin,
      StringComparison.Ordinal
    );
    var persist = source.IndexOf(write, begin, StringComparison.Ordinal);
    Assert.True(begin >= 0 && begin < validate && validate < persist);
    if (owner == "BaseRouteService")
    {
      Assert.Contains("publicationScope.BeginAsync(load.TruckId, ct)", source);
      Assert.DoesNotContain("db.Database.BeginTransactionAsync", source);
    }
  }

  [Theory]
  [InlineData("Routes/PlanningWorkPublication.cs", "itineraries.ReadAsync")]
  [InlineData("Deadheads/DeadheadHistoryPublication.cs", "history.ReadAsync")]
  public void FinalValidationBelongsToTheOwnedPublicationTransaction(
    string owner,
    string reader
  )
  {
    var source = File.ReadAllText(
      Path.Combine(
        Root(),
        "Server/Application/Features/Routing/Services",
        owner
      )
    );
    var begin = source.IndexOf("scope.BeginAsync", StringComparison.Ordinal);
    var read = source.IndexOf(reader, StringComparison.Ordinal);
    var validate = source.IndexOf("?.InputSignature", StringComparison.Ordinal);
    var returned = source.IndexOf(
      "return transaction",
      StringComparison.Ordinal
    );

    Assert.True(
      begin >= 0 && begin < read && read < validate && validate < returned
    );
    if (owner == "Routes/PlanningWorkPublication.cs")
    {
      var historical = source.IndexOf(
        "history.RequirePredecessorsAsync",
        StringComparison.Ordinal
      );
      Assert.True(validate < historical && historical < returned);
    }
    Assert.DoesNotContain("MatchesAsync", source);
    Assert.Contains("transaction.DisposeAsync()", source);
  }

  private static string WriterMigration() =>
    File.ReadAllText(
      Path.Combine(
        Root(),
        "Server/Infrastructure/Persistence/Migrations",
        "20260917055902_RebuildExecutionStorage.PlanningInputs.cs"
      )
    );
}
