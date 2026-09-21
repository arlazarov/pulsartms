using System.Text.RegularExpressions;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

// The same two rules the browser sources live under, for the same reason:
// the scheme was right and nothing inside it stopped a service from growing
// into a screen of its own.
[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class ServerStructureTests
{
  private const int Limit = 400;

  // A file that moves to another layer may raise its number here once, by
  // what the move itself takes: a feature's namespace splits into the
  // vocabulary, the rules and the policy, and a file that used one of them
  // now names three. That is the only thing allowed to raise a number.
  //
  // Written as whole screens before there was a rule. Each is recorded at
  // the length it had when the rule arrived: it may shrink, it may not grow,
  // and the number only ever comes down. A file leaves this list by getting
  // under the limit, where the ordinary rule holds it.
  private static readonly Dictionary<string, int> Written = new()
  {
    ["Application/Features/Eta/Services/EtaChainInputsService.cs"] = 561,
    ["Infrastructure/Persistence/TruckFuelPlanStore.cs"] = 545,
    [
      "Application/Features/Mileage/Services/AutomaticMileageRecorder.Odometer.cs"
    ] = 533,
    ["Application/Features/Routing/Services/Deadheads/DeadheadService.cs"] =
      484,
    ["Application/Features/Execution/Services/ExecutionWorkReader.cs"] = 479,
    ["Application/Features/Dispatch/Commands/SyncDispatche/SyncDispatche.cs"] =
      475,
    ["Domain/Rules/Eta/HosTravelClock.cs"] = 462,
    ["Domain/Rules/Routing/FuelPlanProjection.cs"] = 454,
    ["Domain/Rules/Routing/FuelOptimizer.cs"] = 434,
    ["Application/Features/Routing/Services/Routes/BaseRouteService.cs"] = 428,
    ["Application/Features/Routing/Background/BaseRouteOperation.cs"] = 427,
    ["Application/Features/Execution/Commands/PlanSwitch.cs"] = 421,
    ["Application/Features/Eta/Services/EtaForecastService.cs"] = 419,
    ["Application/Features/Execution/Commands/SwitchParticipantActions.cs"] =
      414,
    ["Application/Features/Dispatch/Services/DispatchWorkspaceReader.cs"] = 409,
    [
      "Application/Features/Routing/Services/FuelPlanning/FuelCheckedRouteSearch.cs"
    ] = 407,
  };

  private static IEnumerable<(string Name, int Lines)> ServerFiles()
  {
    var server = Path.Combine(Root(), "Server");
    var files = Sources(server, "*.cs")
      .Where(file =>
        !file.Contains(
          $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"
        )
      )
      .ToList();
    // A rule that reads no files passes by saying nothing.
    Assert.True(
      files.Count >= 500,
      $"this rule looked at {files.Count} server files and expected at least 500"
    );
    return files.Select(file =>
      (
        Path.GetRelativePath(server, file).Replace('\\', '/'),
        File.ReadAllLines(file).Length
      )
    );
  }

  [Fact]
  public void AFileWrittenAsAWholeScreenMayOnlyGetSmaller()
  {
    var lengths = ServerFiles().ToDictionary(x => x.Name, x => x.Lines);
    foreach (var (name, budget) in Written)
    {
      Assert.True(
        lengths.TryGetValue(name, out var now),
        $"{name} is gone - drop it from the list"
      );
      Assert.True(
        now <= budget,
        $"{name}: {now} lines, was {budget} - it may shrink, not grow"
      );
      Assert.True(
        now > Limit,
        $"{name}: {now} lines is under the limit - drop it from the list"
      );
    }
  }

  [Fact]
  public void AFileStartedSinceIsOneThingYouCanName()
  {
    foreach (var (name, lines) in ServerFiles())
      if (!Written.ContainsKey(name))
        Assert.True(lines <= Limit, $"{name}: {lines} lines - split it");
  }

  // Domain is the one layer every other may lean on, which only holds while
  // it leans on nothing: no package, no project, no framework. A rule about
  // the business that needs a database to state is not yet a rule.
  [Fact]
  public void DomainDependsOnNothing()
  {
    var domain = Path.Combine(Root(), "Server", "Domain");
    var project = File.ReadAllText(Path.Combine(domain, "Domain.csproj"));
    Assert.DoesNotContain("PackageReference", project);
    Assert.DoesNotContain("ProjectReference", project);
    var sources = Sources(domain, "*.cs").ToList();
    Assert.True(sources.Count >= 50, $"looked at {sources.Count} domain files");
    foreach (var file in sources)
      Assert.DoesNotMatch(
        new Regex(
          @"^using\s+(Application|Infrastructure|API|Microsoft)\b",
          RegexOptions.Multiline
        ),
        File.ReadAllText(file)
      );
  }
}
