using System.Text.RegularExpressions;
using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Services;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Rules.Routing;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public class LayerBoundaryTests
{
  [Fact]
  public void ExecutionReadContractsStayIndependentOfDispatchScreenModels()
  {
    foreach (
      var name in new[]
      {
        "ExecutionLoadSnapshot",
        "TruckWorkSelection",
        "WorkOrderKey",
        "WorkSequence",
        "TruckItinerarySnapshot",
      }
    )
    {
      var path = Path.Combine(
        Root(),
        Directory.Exists(Path.Combine(Root(), "Server/Domain/Models/Execution"))
        && File.Exists(
          Path.Combine(Root(), $"Server/Domain/Models/Execution/{name}.cs")
        )
          ? $"Server/Domain/Models/Execution/{name}.cs"
          : $"Server/Application/Features/Execution/Models/{name}.cs"
      );
      Assert.DoesNotMatch(
        @"\bDispatchResponse\b|\bTruckDispatchBoardResponse\b|Application\.Features\.Dispatch",
        File.ReadAllText(path)
      );
    }
    var reader = File.ReadAllText(
      Path.Combine(
        Root(),
        "Server/Application/Features/Execution/Services/ExecutionWorkReader.cs"
      )
    );
    Assert.DoesNotMatch(
      @"\bDispatchResponse\b|\bTruckDispatchBoardResponse\b"
        + @"|Application\.Features\.Dispatch",
      reader
    );
    var application = typeof(GetDispatchBoardHandler).Assembly;
    Assert.Null(
      application.GetType(
        "Application.Features.Execution.Services.ExecutionRouteProjection"
      )
    );
    Assert.Null(
      application.GetType(
        "Application.Features.Execution.Services.ExecutionWorkProjection"
      )
    );
    var eta = Path.Combine(
      Root(),
      "Server/Application/Features/Eta/Services/EtaChainInputsService.cs"
    );
    var etaSource = File.ReadAllText(eta);
    Assert.DoesNotMatch(
      @"GetDispatchBoardQuery|ResolveAssignmentAsync|ExecutionWorkReader"
        + @"|\.Dispatches\b|\.Trucks\b",
      etaSource
    );
    Assert.Contains("TruckItineraryReader", etaSource);
  }

  [Fact]
  public void TruckPlanningReadsUseCapturedAssignments()
  {
    foreach (var name in new[] { "RoutePreviewService", "PlanningReadService" })
    {
      var source = File.ReadAllText(
        Path.Combine(
          Root(),
          "Server/Application/Features/Routing/Services/Routes",
          name + ".cs"
        )
      );
      var start = source.IndexOf("ForTruckAsync(", StringComparison.Ordinal);
      var end = source.IndexOf("\n  }", start, StringComparison.Ordinal);
      var truckRead = source[start..end];
      Assert.Contains("inputs.ReadAsync", truckRead);
      Assert.DoesNotMatch(
        @"GetDispatchBoardQuery|LoadAsync|ResolveAssignmentAsync",
        truckRead
      );
      Assert.DoesNotMatch(
        @"ExecutionWorkReader|ResolveAssignmentAsync|"
          + @"\.Dispatches\.AsNoTracking|\.Trucks\.AsNoTracking",
        source
      );
    }
  }

  [Fact]
  public void RouteMutationsUseCapturedWorkInsteadOfBoardSelection()
  {
    var folder = Path.Combine(
      Root(),
      "Server/Application/Features/Routing/Services/Routes"
    );
    foreach (
      var name in new[]
      {
        "AutomaticPlanningService",
        "RouteChoiceService",
        "RouteChoiceService.Current",
      }
    )
    {
      var source = File.ReadAllText(Path.Combine(folder, name + ".cs"));
      Assert.DoesNotMatch(
        @"GetDispatchBoardQuery|ResolveAssignmentAsync|plans\.LoadAsync|planning\.LoadAsync",
        source
      );
      if (name == "AutomaticPlanningService")
        Assert.Contains("inputs.RequireCurrentAsync", source);
      if (name == "RouteChoiceService")
      {
        Assert.Contains("publication.BeginAsync(work, ct)", source);
        Assert.Contains("await transaction.CommitAsync(ct)", source);
      }
    }
    // Each use of the service is a part of its own, named after what it
    // does.
    foreach (
      var (part, method) in new[]
      {
        ("Build", "BuildAsync("),
        ("Tracking", "AdvanceAutomaticallyAsync("),
      }
    )
    {
      var routes = File.ReadAllText(
        Path.Combine(folder, $"RoutePlanningService.{part}.cs")
      );
      var start = routes.IndexOf(method, StringComparison.Ordinal);
      Assert.True(
        start >= 0,
        $"{method} is not in RoutePlanningService.{part}"
      );
      var end = routes.IndexOf("\n  }", start, StringComparison.Ordinal);
      var body = routes[start..end];
      Assert.Contains("PlanningWorkPolicy.Resolve", body);
      Assert.Contains("inputs.RequireCurrentAsync", body);
      Assert.DoesNotMatch(@"await LoadAsync\(", body);
    }
  }

  [Fact]
  public void FuelWorkComesFromTheCapturedItinerary()
  {
    var folder = Path.Combine(
      Root(),
      "Server/Application/Features/Routing/Services/FuelPlanning"
    );
    foreach (
      var name in new[]
      {
        "FuelHorizon",
        "FuelRegionPlanner",
        "FuelPlanningService",
        "FuelPlanningService.Editing",
        "TruckFuelPlans",
        "FuelPriceRefreshService",
      }
    )
    {
      var source = File.ReadAllText(Path.Combine(folder, name + ".cs"));
      Assert.DoesNotMatch(
        @"GetDispatchBoardQuery|ExecutionWorkReader|ResolveAssignmentAsync",
        source
      );
      if (name is "FuelHorizon" or "FuelRegionPlanner")
        Assert.DoesNotMatch(@"plans\.LoadAsync|suppliedLoads", source);
    }
    var reader = File.ReadAllText(
      Path.Combine(folder, "FuelWorkInputsReader.cs")
    );
    Assert.Contains("requireFreshSnapshot: true", reader);
    Assert.Contains("includeHos: false", reader);
  }

  [Fact]
  public void ServerDependenciesStayBehindContracts()
  {
    var root = Root();
    foreach (
      var file in Sources(Path.Combine(root, "Server/Application"), "*.cs")
    )
    {
      Assert.DoesNotMatch(
        @"\b(?:API|Infrastructure)\.",
        File.ReadAllText(file)
      );
      Assert.DoesNotMatch(
        @"\b(?:Npgsql|SqlConnection|SqliteConnection|ExecuteSqlRawAsync|ExecuteSqlInterpolatedAsync)\b|pg_advisory_",
        File.ReadAllText(file)
      );
    }
    foreach (
      var file in Sources(Path.Combine(root, "Server/Infrastructure"), "*.cs")
    )
    {
      var source = File.ReadAllText(file);
      Assert.DoesNotMatch(@"\bAPI\.", source);
      Assert.DoesNotMatch(
        @"\bApplication\.(?:\w+\.)*(?:Services|Commands|Queries|Behaviors|Algorithms)\b",
        source
      );
      Assert.DoesNotMatch(@"\b(?:ISender|IMediator)\b|using MediatR", source);
    }
    foreach (var file in Sources(Path.Combine(root, "Server/API"), "*.cs"))
    {
      var source = File.ReadAllText(file);
      // The web layer may name the vocabulary - the models and policies the
      // whole server speaks in - but never a stored entity: what the
      // database holds is not what a controller answers with.
      Assert.DoesNotMatch(@"\bDomain\.Entities\b", source);
      if (Path.GetFileName(file) != "DependencyInjection.cs")
        Assert.DoesNotMatch(@"\bInfrastructure(?:\.|;)", source);
      Assert.DoesNotMatch(
        @"\bApplication\.(?:\w+\.)*(?:Services|Behaviors|Algorithms)\b",
        source
      );
    }
  }

  [Fact]
  public void PlanningBudgetAndHorizonCannotBeOptionalDependencies()
  {
    var budget = typeof(RoutePlanningService)
      .GetConstructors()
      .Single()
      .GetParameters()
      .Single(x => x.ParameterType == typeof(RouteRecalculationBudget));
    var horizon = typeof(FuelPlanningService)
      .GetConstructors()
      .Single()
      .GetParameters()
      .Single(x => x.ParameterType == typeof(FuelHorizon));
    Assert.False(budget.IsOptional);
    Assert.False(horizon.IsOptional);
    foreach (
      var service in new[]
      {
        typeof(FuelHorizon),
        typeof(RoutePreviewService),
        typeof(RoutePlanningService),
        typeof(PlanningSettingsService),
        typeof(PlanningReadService),
        typeof(FuelPlanningService),
        typeof(EtaService),
        typeof(GetDispatchBoardHandler),
      }
    )
      Assert.All(
        service.GetConstructors().Single().GetParameters(),
        parameter => Assert.False(parameter.IsOptional)
      );
  }

  [Fact]
  public void FuelSelectionCannotRequestOrRepairRoads()
  {
    foreach (
      var name in new[]
      {
        "FuelPlanningService",
        "FuelHorizon",
        "FuelRegionPlanner",
      }
    )
    {
      var path = Path.Combine(
        Root(),
        $"Server/Application/Features/Routing/Services/FuelPlanning/{name}.cs"
      );
      Assert.DoesNotMatch(
        @"\b(?:IRoutingProvider|FuelCheckedRouteSearch)\b|\.CalculateAsync\(|\.EnsureAsync\(|StopLocation\.ResolveAsync\(",
        File.ReadAllText(path)
      );
    }
    var source = File.ReadAllText(
      Path.Combine(
        Root(),
        "Server/Application/Features/Routing/Services/Routes/AutomaticPlanningService.cs"
      )
    );
    var method = source
      .Split(
        "public async Task<AutomaticPlanningResult> RecalculateFuelAsync",
        2
      )[1]
      .Split("public static void ProjectRecommendations", 2)[0];
    Assert.DoesNotMatch(
      @"plans\.(?:BuildAsync|AdvanceAutomaticallyAsync)\(",
      method
    );
  }

  [Fact]
  public void ApplicationConstructorsRequireRegisteredDependencies()
  {
    var assembly = typeof(RoutePlanningService).Assembly;
    foreach (
      var type in assembly
        .GetTypes()
        .Where(x =>
          x.IsPublic
          && !x.IsAbstract
          && (
            x.Name.EndsWith("Handler")
            || x.Namespace?.Split('.').Contains("Services") == true
          )
        )
    )
    foreach (
      var parameter in type.GetConstructors().SelectMany(x => x.GetParameters())
    )
      if (
        parameter.ParameterType.Namespace is { } ns
        && (
          ns.StartsWith("Application.")
          || ns.StartsWith("Microsoft.Extensions.")
        )
      )
        Assert.False(
          parameter.IsOptional,
          $"{type.FullName}: {parameter.Name}"
        );
  }

  [Fact]
  public void MaintainedDocumentationAndCommentsHaveNoCyrillicText()
  {
    foreach (var file in Sources(Root(), "*.md"))
      Assert.False(Regex.IsMatch(File.ReadAllText(file), "[А-Яа-яЁё]"), file);
    foreach (
      var dir in new[]
      {
        "Client",
        "Server",
        "Client.Tests",
        "Server.Tests",
        "tools",
      }
    )
    foreach (var file in Sources(Path.Combine(Root(), dir), "*"))
    {
      if (
        !new[] { ".cs", ".js", ".scss", ".razor" }.Contains(
          Path.GetExtension(file)
        ) || file.EndsWith(".bundle.js")
      )
        continue;
      foreach (
        var line in File.ReadLines(file)
          .Where(l => l.TrimStart().StartsWith("//"))
      )
        Assert.False(Regex.IsMatch(line, "[А-Яа-яЁё]"), file);
    }
  }
}
