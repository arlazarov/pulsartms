using System.Reflection;
using Client.Services;

namespace Client.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class TestConventionsTests
{
  [Fact]
  public void ClientTestsUseTheCompiledClientAssemblyWithoutServerDependencies()
  {
    var tests = typeof(TestConventionsTests).Assembly;
    Assert.Equal("Client.Tests", tests.GetName().Name);
    Assert.NotEqual(tests, typeof(MapRoutePublisher).Assembly);
    Assert.Equal("Client", typeof(MapRoutePublisher).Assembly.GetName().Name);
    Assert.DoesNotContain(
      tests.GetReferencedAssemblies(),
      assembly =>
        assembly.Name is "API" or "Application" or "Infrastructure" or "Domain"
    );
  }

  [Fact]
  public void EveryClientTestClassHasAFeatureAndTestKind()
  {
    string[] categories =
    [
      "Addresses",
      "Architecture",
      "Dispatch",
      "Eta",
      "Finance",
      "Fleet",
      "Fuel",
      "Identity",
      "Routing",
      "Synchronization",
    ];
    string[] kinds =
    [
      "Unit",
      "Integration",
      "Component",
      "Architecture",
      "Allocation",
    ];
    var types = typeof(TestConventionsTests)
      .Assembly.GetTypes()
      .Where(type =>
        type.GetMethods()
          .Any(method => method.GetCustomAttributes<FactAttribute>().Any())
      );
    foreach (var type in types)
    {
      var traits = type.GetCustomAttributesData()
        .Where(trait => trait.AttributeType == typeof(TraitAttribute))
        .ToArray();
      Assert.StartsWith("Client.Tests.", type.Namespace);
      Assert.Contains(
        traits,
        trait => trait.ConstructorArguments[0].Value as string == "Category"
      );
      Assert.Contains(
        traits,
        trait => trait.ConstructorArguments[0].Value as string == "Kind"
      );
      foreach (var trait in traits)
      {
        if (trait.ConstructorArguments[0].Value as string == "Category")
          Assert.Contains(
            trait.ConstructorArguments[1].Value as string,
            categories
          );
        if (trait.ConstructorArguments[0].Value as string == "Kind")
          Assert.Contains(trait.ConstructorArguments[1].Value as string, kinds);
      }
    }
  }
}
