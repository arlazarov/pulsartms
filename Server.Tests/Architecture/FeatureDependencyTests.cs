using System.Text.RegularExpressions;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed partial class FeatureDependencyTests
{
  // Cross-feature references inside Application, as of September 2026. Routing, Eta, Dispatch,
  // Fleet and Synchronization form cycles; this map may only lose edges, never gain them.
  private static readonly Dictionary<string, string[]> Allowed = new()
  {
    ["Dispatch"] = ["Eta", "Fleet", "Routing", "Synchronization"],
    ["Eta"] = ["Dispatch", "Fleet", "Routing", "Synchronization"],
    ["Fleet"] = ["Routing", "Synchronization"],
    ["Fuel"] = ["Synchronization"],
    ["Routing"] = ["Dispatch", "Eta", "Fleet", "Fuel", "Synchronization"],
    ["Synchronization"] = ["Dispatch", "Fleet", "Routing"],
    ["Users"] = ["Synchronization"],
  };

  [GeneratedRegex(@"\bApplication\.Features\.(\w+)")]
  private static partial Regex FeatureReference();

  [Fact]
  public void FeaturesReferenceOnlyTheFeaturesTheyAlreadyDependedOn()
  {
    var root = Path.Combine(Root(), "Server/Application/Features");
    var found = new Dictionary<string, SortedSet<string>>();
    foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
    {
      var feature = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar)[0];
      foreach (Match match in FeatureReference().Matches(File.ReadAllText(file)))
      {
        var target = match.Groups[1].Value;
        if (target == feature) continue;
        Assert.True(Allowed.TryGetValue(feature, out var allowed) && allowed.Contains(target),
          $"{Path.GetRelativePath(Root(), file)} references Application.Features.{target}; {feature} may not depend on it.");
        if (!found.TryGetValue(feature, out var set)) found[feature] = set = [];
        set.Add(target);
      }
    }
    foreach (var (feature, targets) in Allowed)
      Assert.True(found.TryGetValue(feature, out var current) && current.SetEquals(targets),
        $"{feature} no longer references every listed feature; remove the dropped edges from the allowed map.");
  }

  [Fact]
  public void ApplicationHoldsNoStaticGates()
  {
    foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "Server/Application"), "*.cs", SearchOption.AllDirectories))
      Assert.DoesNotContain("static readonly KeyedGates", File.ReadAllText(file));
  }
}
