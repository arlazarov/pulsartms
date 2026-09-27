using System.Reflection;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class TestCategoryTests
{
  [Fact]
  public void ServerTestAssemblyContainsNoCopiedClientTypes()
  {
    var assembly = typeof(TestCategoryTests).Assembly;
    Assert.Equal("Server.Tests", assembly.GetName().Name);
    Assert.DoesNotContain(
      assembly.GetTypes(),
      type =>
        type.Namespace?.StartsWith("Client.", StringComparison.Ordinal) == true
    );
    Assert.DoesNotContain(
      assembly.GetReferencedAssemblies(),
      reference => reference.Name == "Client"
    );
  }

  [Fact]
  public void EveryTestClassHasAKnownCategory()
  {
    string[] categories =
    [
      "Addresses",
      "Architecture",
      "Caching",
      "Costs",
      "Database",
      "Dispatch",
      "Eta",
      "Finance",
      "Fleet",
      "Fuel",
      "Identity",
      "Messaging",
      "Persistence",
      "Routing",
      "Synchronization",
    ];
    var types = typeof(TestCategoryTests)
      .Assembly.GetTypes()
      .Where(type =>
        type.GetMethods()
          .Any(method => method.GetCustomAttributes<FactAttribute>().Any())
      );
    foreach (var type in types)
    {
      Assert.StartsWith("Server.Tests.", type.Namespace);
      var traits = type.GetCustomAttributesData()
        .Where(trait =>
          trait.AttributeType == typeof(TraitAttribute)
          && trait.ConstructorArguments[0].Value as string == "Category"
        )
        .ToArray();
      Assert.True(traits.Length > 0, $"{type.Name} needs a Category trait.");
      Assert.All(
        traits,
        trait =>
          Assert.Contains(
            trait.ConstructorArguments[1].Value as string,
            categories
          )
      );
    }
  }

  // A declared Kind is one of the kinds docs/testing.md names, so a filter
  // by kind selects what it says (audit F12). Classes that declare none
  // yet are older debt; this does not require one.
  [Fact]
  public void EveryDeclaredKindIsKnown()
  {
    string[] kinds =
    [
      "Unit",
      "Integration",
      "Component",
      "Architecture",
      "Allocation",
    ];
    var unknown = typeof(TestCategoryTests)
      .Assembly.GetTypes()
      .SelectMany(type =>
        type.GetCustomAttributesData()
          .Where(trait =>
            trait.AttributeType == typeof(TraitAttribute)
            && trait.ConstructorArguments[0].Value as string == "Kind"
            && !kinds.Contains(trait.ConstructorArguments[1].Value as string)
          )
          .Select(trait =>
            $"{type.Name}: {trait.ConstructorArguments[1].Value}"
          )
      )
      .ToArray();

    Assert.True(
      unknown.Length == 0,
      "Unknown test kinds: " + string.Join(", ", unknown)
    );
  }
}
