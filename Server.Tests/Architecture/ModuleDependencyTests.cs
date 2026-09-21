using System.Reflection;
using System.Runtime.CompilerServices;
using Application.Interfaces;

namespace Server.Tests.Architecture;

// Feature modules still reference each other in both directions. This check
// does not accept that as correct: it records the edges that exist today so a
// new one fails, and the recorded set can only shrink. It reads compiled
// signatures, not source text, so moving or renaming code does not break it.
[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class ModuleDependencyTests
{
  // Each entry is a dependency a module still has on another module. Remove
  // an entry once the dependency is gone; never add one to make a change pass.
  private static readonly string[] Recorded =
  [
    "Border -> Shipments",
    "Dispatch -> Eta",
    "Dispatch -> Fleet",
    "Dispatch -> Routing",
    "Eta -> Dispatch",
    "Eta -> Execution",
    "Eta -> Fleet",
    "Eta -> Routing",
    "Execution -> Dispatch",
    "Execution -> Routing",
    "Fleet -> Synchronization",
    "Mileage -> Routing",
    "Routing -> Dispatch",
    "Routing -> Eta",
    "Routing -> Execution",
    "Routing -> Fleet",
    "Routing -> Fuel",
    "Routing -> Synchronization",
    "Synchronization -> Dispatch",
    "Synchronization -> Fleet",
  ];

  [Fact]
  public void NoModuleGainsANewDependencyOnAnother()
  {
    var found = Edges();

    Assert.Empty(found.Except(Recorded).Order());
  }

  [Fact]
  public void TheRecordedDependenciesStillExist()
  {
    var found = Edges();

    Assert.Empty(Recorded.Except(found).Order());
  }

  private static HashSet<string> Edges()
  {
    // Compiler-generated types - async state machines, closures, iterator
    // classes - hoist locals into fields, and how many they hoist depends on
    // the build configuration. Only types the code actually declares count.
    var types = typeof(IBackgroundOperation)
      .Assembly.GetTypes()
      .Where(type =>
        Feature(type) is not null
        && !type.IsDefined(typeof(CompilerGeneratedAttribute), false)
        && type.DeclaringType?.IsDefined(
          typeof(CompilerGeneratedAttribute),
          false
        ) != true
      )
      .ToArray();
    var edges = new HashSet<string>();
    foreach (var type in types)
    {
      var from = Feature(type)!;
      foreach (var referenced in References(type))
      {
        var to = Feature(referenced);
        if (to is null || to == from)
          continue;
        edges.Add($"{from} -> {to}");
      }
    }
    return edges;
  }

  private static string? Feature(Type type)
  {
    const string root = "Application.Features.";
    var name = type.Namespace;
    if (name is null || !name.StartsWith(root, StringComparison.Ordinal))
      return null;
    var rest = name[root.Length..];
    var end = rest.IndexOf('.', StringComparison.Ordinal);
    return end < 0 ? rest : rest[..end];
  }

  private static IEnumerable<Type> References(Type type)
  {
    const BindingFlags members =
      BindingFlags.Public
      | BindingFlags.NonPublic
      | BindingFlags.Instance
      | BindingFlags.Static
      | BindingFlags.DeclaredOnly;
    IEnumerable<Type> declared =
    [
      type.BaseType ?? typeof(object),
      .. type.GetInterfaces(),
      .. type.GetFields(members).Select(x => x.FieldType),
      .. type.GetProperties(members).Select(x => x.PropertyType),
      .. type.GetMethods(members)
        .SelectMany(x =>
          x.GetParameters().Select(p => p.ParameterType).Append(x.ReturnType)
        ),
      .. type.GetConstructors(members)
        .SelectMany(x => x.GetParameters().Select(p => p.ParameterType)),
    ];
    return declared.SelectMany(Unwrap);
  }

  private static IEnumerable<Type> Unwrap(Type type)
  {
    if (type.HasElementType && type.GetElementType() is { } element)
      return Unwrap(element);
    return type.IsGenericType
      ? type.GetGenericArguments()
        .SelectMany(Unwrap)
        .Append(type.GetGenericTypeDefinition())
      : [type];
  }
}
