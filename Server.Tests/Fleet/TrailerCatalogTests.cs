using Application.Features.Fleet.Services;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Fleet;

// Which trailers exist, whoever reports them. A telemetry provider gives
// ids; an imported load gives only a number. One number is one trailer per
// carrier: a row is taken over by another identity only when that cannot
// join two different trailers, and nothing is duplicated.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class TrailerCatalogTests
{
  // Not any real provider: the catalog must not care which one it is.
  private const string Telemetry = "acme-telematics";
  private const string Loads = "load-source";

  [Fact]
  public async Task ATrailerOnlyALoadNamesIsCatalogued()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var found = await TrailerCatalog.EnsureAsync(
      f.Db,
      Loads,
      ["55904", " 55904 ", "TBD", "N/A", "", null, "12345678901"],
      default
    );
    await f.Db.SaveChangesAsync();

    var trailer = Assert.Single(await f.Db.Trailers.ToListAsync());
    Assert.Same(trailer, found["55904"]);
    Assert.Equal("55904", trailer.UnitNumber);
    Assert.Equal("", trailer.ExternalId);
    Assert.Equal(Loads, trailer.Source);
    Assert.True(trailer.IsActive);
  }

  [Fact]
  public async Task BothSourcesNamingOneTrailerMakeOneRowInEitherOrder()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    // The load first, then the provider.
    await TrailerCatalog.EnsureAsync(f.Db, Loads, ["55904"], default);
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      0,
      await TrailerCatalog.ApplyAsync(
        f.Db,
        Telemetry,
        [Reported("t-1", "55904", "1UYVS2539GU000001")],
        default
      )
    );
    await f.Db.SaveChangesAsync();
    // The provider first, then another load.
    await TrailerCatalog.ApplyAsync(
      f.Db,
      Telemetry,
      [Reported("t-2", "60001", "")],
      default
    );
    await f.Db.SaveChangesAsync();
    await TrailerCatalog.EnsureAsync(f.Db, Loads, ["60001", "55904"], default);
    await f.Db.SaveChangesAsync();

    var rows = await f.Db.Trailers.OrderBy(x => x.UnitNumber).ToListAsync();
    Assert.Equal(["55904", "60001"], rows.Select(x => x.UnitNumber));
    Assert.Equal(["t-1", "t-2"], rows.Select(x => x.ExternalId));
    Assert.All(rows, x => Assert.Equal(Telemetry, x.Source));
    Assert.Equal("1UYVS2539GU000001", rows[0].Vin);
  }

  [Fact]
  public async Task DifferentVinsOrALiveIdAreNeverMerged()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    await TrailerCatalog.ApplyAsync(
      f.Db,
      Telemetry,
      [
        Reported("t-1", "55904", "1UYVS2539GU000001"),
        Reported("t-2", "60001", ""),
      ],
      default
    );
    await f.Db.SaveChangesAsync();

    // Another id with the same number: a different VIN, or an id the
    // provider still reports for the first one.
    Assert.Equal(
      2,
      await TrailerCatalog.ApplyAsync(
        f.Db,
        Telemetry,
        [
          Reported("t-1", "55904", "1UYVS2539GU000001"),
          Reported("t-9", "55904", "1UYVS2539GU000009"),
          Reported("t-2", "60001", ""),
          Reported("t-8", "60001", ""),
        ],
        default
      )
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      ["t-1", "t-2"],
      await f
        .Db.Trailers.OrderBy(x => x.ExternalId)
        .Select(x => x.ExternalId)
        .ToListAsync()
    );

    // The provider replaced its record: the old id is gone from the feed.
    Assert.Equal(
      0,
      await TrailerCatalog.ApplyAsync(
        f.Db,
        Telemetry,
        [Reported("t-3", "60001", "")],
        default
      )
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      "t-3",
      (await f.Db.Trailers.SingleAsync(x => x.UnitNumber == "60001")).ExternalId
    );
  }

  [Fact]
  public async Task ALaterProviderTakesOverAnotherProvidersRowsByNumber()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    f.Db.Trailers.Add(
      new Trailer
      {
        Id = Guid.NewGuid(),
        ExternalId = "legacy-7",
        UnitNumber = "55904",
        IsActive = true,
      }
    );
    await f.Db.SaveChangesAsync();
    // A row from before sources were recorded belongs to the configured
    // provider and keeps its id with it.
    await TrailerCatalog.ApplyAsync(
      f.Db,
      Telemetry,
      [Reported("legacy-7", "55904", "")],
      default
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal(Telemetry, (await f.Db.Trailers.SingleAsync()).Source);

    // Another provider replaces it: the same number moves to its identity.
    await TrailerCatalog.ApplyAsync(
      f.Db,
      "other-telematics",
      [Reported("o-1", "55904", "")],
      default
    );
    await f.Db.SaveChangesAsync();
    var row = await f.Db.Trailers.SingleAsync();
    Assert.Equal(("o-1", "other-telematics"), (row.ExternalId, row.Source));
  }

  [Fact]
  public async Task AnotherCarriersTrailersAreNeitherMatchedNorTouched()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    await using var scope = f.NewScope();
    using (
      scope
        .ServiceProvider.GetRequiredService<ICurrentCompany>()
        .As(Guid.NewGuid())
    )
    {
      var theirs = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
      await TrailerCatalog.EnsureAsync(theirs, Loads, ["55904"], default);
      await theirs.SaveChangesAsync(default);
    }

    await TrailerCatalog.EnsureAsync(f.Db, Loads, ["55904"], default);
    await f.Db.SaveChangesAsync();

    Assert.Single(await f.Db.Trailers.ToListAsync());
    Assert.Equal(2, await f.Db.Trailers.IgnoreQueryFilters().CountAsync());
  }

  private static ExternalTrailer Reported(string id, string unit, string vin) =>
    new()
    {
      ExternalId = id,
      UnitNumber = unit,
      Vin = vin,
      IsActive = true,
    };
}
