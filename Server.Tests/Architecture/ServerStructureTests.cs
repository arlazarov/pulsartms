using System.Text.RegularExpressions;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class ServerStructureTests
{
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

  [Fact]
  public void RequestShapeIsAskedOfTheRequestWithoutAValidationPackage()
  {
    var checkers = 0;
    var files = Sources(Path.Combine(Root(), "Server"), "*.cs")
      .Where(file =>
        !file.Contains(
          $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"
        )
      )
      .ToArray();
    Assert.True(
      files.Length >= 600,
      $"this rule looked at {files.Length} files and expected at least 600 "
        + "- it is no longer reading what it is about"
    );
    foreach (var file in files)
    {
      var source = File.ReadAllText(file);
      Assert.DoesNotContain("AbstractValidator", source);
      // Naming the package in prose is how the code explains why it is
      // gone; using it is what this forbids.
      Assert.DoesNotMatch(@"using FluentValidation|FluentValidation\.", source);
      if (source.Contains("IEnumerable<string> Wrong()"))
        checkers++;
    }
    // A request says what is wrong with its own shape. If this number
    // falls, something stopped being checked before its handler runs.
    Assert.True(
      checkers >= 33,
      $"{checkers} requests check their own shape; there were 33"
    );
    foreach (
      var project in Directory.GetFiles(
        Path.Combine(Root(), "Server"),
        "*.csproj",
        SearchOption.AllDirectories
      )
    )
      Assert.DoesNotContain("FluentValidation", File.ReadAllText(project));
  }
}
