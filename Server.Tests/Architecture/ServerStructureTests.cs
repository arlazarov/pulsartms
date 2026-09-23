using System.Text.RegularExpressions;
using static Server.Tests.Support.RepositoryFiles;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class ServerStructureTests
{
  private const int ReviewThreshold = 400;

  // An exception needs a bounded budget and a reviewed explanation of why
  // splitting would harm cohesion. See docs/architecture/source-size.md.
  private sealed record SizeReview(
    int MaximumLines,
    string Responsibility,
    string WhyNotSplit,
    string ReviewReference
  );

  private static readonly Dictionary<string, SizeReview> Reviewed = [];

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
  public void OversizedFilesHaveCurrentBoundedReviews()
  {
    var lengths = ServerFiles().ToDictionary(x => x.Name, x => x.Lines);
    foreach (var (name, review) in Reviewed)
    {
      Assert.True(
        lengths.TryGetValue(name, out var lines),
        $"{name} is gone - remove its size review"
      );
      Assert.True(
        lines > ReviewThreshold,
        $"{name} no longer needs a size review"
      );
      Assert.True(review.MaximumLines > ReviewThreshold);
      Assert.False(string.IsNullOrWhiteSpace(review.Responsibility));
      Assert.False(string.IsNullOrWhiteSpace(review.WhyNotSplit));
      Assert.False(string.IsNullOrWhiteSpace(review.ReviewReference));
      Assert.True(
        lines <= review.MaximumLines,
        $"{name}: {lines} lines exceeds its reviewed budget; review again"
      );
    }
    foreach (var (name, lines) in lengths)
      Assert.True(
        lines <= ReviewThreshold || Reviewed.ContainsKey(name),
        $"{name}: {lines} lines requires a cohesion review; "
          + "split by responsibility or document a bounded size review"
      );
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
