using System.Reflection;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Architecture;

// Every table is either one carrier's or deliberately nobody's. There is
// no third answer, and a table that has not been given one is the way a
// carrier's rows leak into another carrier's screen.
[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class CompanyOwnershipTests
{
  // Every table EF knows about, not every DbSet on the interface. Three
  // work queues reached the database without appearing there, and a rule
  // that reads the interface would have gone on passing while they were
  // unclassified.
  private static IEnumerable<Type> Tables
  {
    get
    {
      using var db = new Infrastructure.Persistence.AppDbContext(
        new DbContextOptionsBuilder<Infrastructure.Persistence.AppDbContext>()
          .UseNpgsql("Host=none;Database=none")
          .Options
      );
      return db
        .Model.GetEntityTypes()
        .Select(x => x.ClrType)
        .Where(x => x.Namespace?.StartsWith("Domain.Entities") == true)
        .Distinct()
        .ToArray();
    }
  }

  [Fact]
  public void EveryTableIsEitherACarriersOrDeliberatelyNobodys()
  {
    var unclassified = Tables
      .Where(x =>
        !typeof(ICompanyOwned).IsAssignableFrom(x)
        && !SharedTables.Reasons.ContainsKey(x.Name)
        && !SharedTables.PartOfAnotherRow.ContainsKey(x.Name)
        && x != typeof(Company)
      )
      .Select(x => x.Name)
      .Order()
      .ToArray();

    Assert.True(
      unclassified.Length == 0,
      "These tables say nothing about who they belong to. Give them "
        + "ICompanyOwned, or put them in SharedTables with the reason "
        + "they belong to nobody, or in PartOfAnotherRow if they are "
        + "only reachable through a row that is already filtered: "
        + string.Join(", ", unclassified)
    );
  }

  [Fact]
  public void NothingIsBothOwnedAndShared()
  {
    foreach (
      var table in Tables.Where(x => SharedTables.Reasons.ContainsKey(x.Name))
    )
      Assert.False(
        typeof(ICompanyOwned).IsAssignableFrom(table),
        $"{table.Name} is listed as shared and also carries ICompanyOwned"
      );
  }

  [Fact]
  public void TheSharedListSaysWhyForEachOfThem()
  {
    Assert.NotEmpty(SharedTables.Reasons);
    foreach (var (table, reason) in SharedTables.Reasons)
    {
      Assert.Contains(Tables, x => x.Name == table);
      Assert.True(
        reason.Length >= 20,
        $"{table} is shared for the reason \"{reason}\", which is not one"
      );
    }
  }

  // A key a carrier chooses or is given - a load number, a unit number, an
  // external id from a broker - is only unique inside that carrier. Two
  // carriers both having load 1399 is normal.
  [Theory]
  [InlineData("Dispatch", "LoadNumber")]
  [InlineData("Truck", "UnitNumber")]
  [InlineData("Trailer", "UnitNumber")]
  [InlineData("Truck", "ExternalId")]
  [InlineData("Driver", "ExternalId")]
  [InlineData("Customer", "NormalizedName")]
  public void AKeyACarrierChoosesIsOnlyUniqueInsideThatCarrier(
    string table,
    string column
  )
  {
    var source = File.ReadAllText(
      Directory
        .GetFiles(
          Path.Combine(
            Support.RepositoryFiles.Root(),
            "Server/Infrastructure/Persistence/Configurations"
          ),
          $"{table}Configuration.cs",
          SearchOption.AllDirectories
        )
        .Single()
    );
    var index = source
      .Split('\n')
      .Single(line =>
        line.Contains($"x.{column} }}") && line.Contains("HasIndex")
      );

    Assert.Contains("x.CompanyId", index);
  }

  // Background work has nobody signed in, so it has to say whose pass it
  // is running. A worker that says nothing reads an empty database and
  // does nothing at all - safe, and silent, which is the worst way for
  // this to break.
  [Fact]
  public void EveryBackgroundWorkerSaysWhichCarrierItsPassIsFor()
  {
    var workers = Directory
      .GetFiles(
        Path.Combine(Support.RepositoryFiles.Root(), "Server/Application"),
        "*Operation.cs",
        SearchOption.AllDirectories
      )
      .Where(file =>
      {
        var source = File.ReadAllText(file);
        // The loop itself, not the interface naming it and not a command
        // that happens to end in the same word.
        return !Path.GetFileName(file).StartsWith('I')
          && (
            source.Contains("RunAsync(CancellationToken")
            || source.Contains("RunOnceAsync(CancellationToken")
          );
      })
      .ToArray();
    Assert.True(
      workers.Length >= 8,
      $"this rule looked at {workers.Length} background workers and "
        + "expected at least 8 - it is no longer reading what it is about"
    );
    var silent = workers
      .Where(file =>
      {
        var source = File.ReadAllText(file);
        return !source.Contains("ForEachCompanyAsync")
          && !source.Contains("ICurrentCompany");
      })
      .Select(Path.GetFileNameWithoutExtension)
      .Order()
      .ToArray();

    Assert.True(
      silent.Length == 0,
      "These background workers never say whose pass they are running, so "
        + "on a server they read nothing and do nothing: "
        + string.Join(", ", silent)
    );
  }
}
