using System.Text.RegularExpressions;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public class LayerBoundaryTests
{
  [Fact]
  public void ServerDependenciesStayBehindContracts()
  {
    var root = Root();
    foreach (var file in Sources(Path.Combine(root, "Server/Application"), "*.cs"))
    {
      Assert.DoesNotMatch(@"\b(?:API|Infrastructure)\.", File.ReadAllText(file));
      Assert.DoesNotMatch(@"\b(?:Npgsql|SqlConnection|SqliteConnection|ExecuteSqlRawAsync|ExecuteSqlInterpolatedAsync)\b|pg_advisory_", File.ReadAllText(file));
    }
    foreach (var file in Sources(Path.Combine(root, "Server/Infrastructure"), "*.cs"))
    {
      var source = File.ReadAllText(file);
      Assert.DoesNotMatch(@"\bAPI\.", source);
      Assert.DoesNotMatch(@"\bApplication\.(?:\w+\.)*(?:Services|Commands|Queries|Behaviors|Algorithms)\b", source);
      Assert.DoesNotMatch(@"\b(?:ISender|IMediator)\b|using MediatR", source);
    }
    foreach (var file in Sources(Path.Combine(root, "Server/API"), "*.cs"))
    {
      var source = File.ReadAllText(file);
      Assert.DoesNotMatch(@"\bDomain\.", source);
      if (Path.GetFileName(file) != "DependencyInjection.cs")
        Assert.DoesNotMatch(@"\bInfrastructure(?:\.|;)", source);
      Assert.DoesNotMatch(@"\bApplication\.(?:\w+\.)*(?:Services|Behaviors|Algorithms)\b", source);
    }
  }

  [Fact]
  public void PlanningBudgetAndHorizonCannotBeOptionalDependencies()
  {
    var budget = typeof(Application.Features.Routing.Services.Routes.RoutePlanningService).GetConstructors().Single()
      .GetParameters().Single(x => x.ParameterType == typeof(Application.Features.Routing.Services.Routes.RouteRecalculationBudget));
    var horizon = typeof(Application.Features.Routing.Services.FuelPlanning.FuelPlanningService).GetConstructors().Single()
      .GetParameters().Single(x => x.ParameterType == typeof(Application.Features.Routing.Services.FuelPlanning.FuelHorizon));
    Assert.False(budget.IsOptional);
    Assert.False(horizon.IsOptional);
    foreach (var service in new[] {
      typeof(Application.Features.Routing.Services.FuelPlanning.FuelHorizon), typeof(Application.Features.Routing.Services.Routes.RoutePreviewService),
      typeof(Application.Features.Routing.Services.Routes.RoutePlanningService), typeof(Application.Features.Routing.Services.Routes.PlanningSettingsService),
      typeof(Application.Features.Routing.Services.Routes.PlanningReadService), typeof(Application.Features.Routing.Services.FuelPlanning.FuelPlanningService),
      typeof(Application.Features.Eta.Services.EtaService), typeof(Application.Features.Dispatch.Queries.GetDispatchBoardHandler) })
      Assert.All(service.GetConstructors().Single().GetParameters(), parameter => Assert.False(parameter.IsOptional));
  }

  [Fact]
  public void FuelSelectionCannotRequestOrRepairRoads()
  {
    foreach (var name in new[] { "FuelPlanningService", "FuelHorizon", "FuelRegionPlanner" })
    {
      var path = Path.Combine(Root(), $"Server/Application/Features/Routing/Services/FuelPlanning/{name}.cs");
      Assert.DoesNotMatch(@"\b(?:IRoutingProvider|FuelCheckedRouteSearch)\b|\.CalculateAsync\(|\.EnsureAsync\(|StopLocation\.ResolveAsync\(",
        File.ReadAllText(path));
    }
    var source = File.ReadAllText(Path.Combine(Root(), "Server/Application/Features/Routing/Services/Routes/AutomaticPlanningService.cs"));
    var method = source.Split("public async Task<AutomaticPlanningResult> RecalculateFuelAsync", 2)[1]
      .Split("public static void ProjectRecommendations", 2)[0];
    Assert.DoesNotMatch(@"plans\.(?:BuildAsync|AdvanceAutomaticallyAsync)\(", method);
  }

  [Fact]
  public void ApplicationConstructorsRequireRegisteredDependencies()
  {
    var assembly = typeof(Application.Features.Routing.Services.Routes.RoutePlanningService).Assembly;
    foreach (var type in assembly.GetTypes().Where(x => x.IsPublic && !x.IsAbstract
      && (x.Name.EndsWith("Handler") || x.Namespace?.Split('.').Contains("Services") == true)))
      foreach (var parameter in type.GetConstructors().SelectMany(x => x.GetParameters()))
        if (parameter.ParameterType.Namespace is { } ns && (ns.StartsWith("Application.") || ns.StartsWith("Microsoft.Extensions.")))
          Assert.False(parameter.IsOptional, $"{type.FullName}: {parameter.Name}");
  }

  [Fact]
  public void MaintainedDocumentationAndCommentsHaveNoCyrillicText()
  {
    foreach (var file in Sources(Root(), "*.md"))
      Assert.False(Regex.IsMatch(File.ReadAllText(file), "[А-Яа-яЁё]"), file);
    foreach (var dir in new[] { "Client", "Server", "Client.Tests", "Server.Tests", "tools" })
      foreach (var file in Sources(Path.Combine(Root(), dir), "*"))
      {
        if (!new[] { ".cs", ".js", ".scss", ".razor" }.Contains(Path.GetExtension(file)) || file.EndsWith(".bundle.js")) continue;
        foreach (var line in File.ReadLines(file).Where(l => l.TrimStart().StartsWith("//")))
          Assert.False(Regex.IsMatch(line, "[А-Яа-яЁё]"), file);
      }
  }
}
