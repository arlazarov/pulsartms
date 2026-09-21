using Domain.Models.Routing;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
public sealed class NextLoadRoutesResponseTests
{
  [Fact]
  public void CompanyRenameChangesMetadataWithoutInvalidatingGeometry()
  {
    var id = Guid.NewGuid();
    var before = NextLoadRoutesResponse.MetadataRevision(
      "geometry",
      [new(id, ["Warehouse"])]
    );
    var after = NextLoadRoutesResponse.MetadataRevision(
      "geometry",
      [new(id, ["Renamed warehouse"])]
    );
    Assert.NotEqual(before, after);
    Assert.True(NextLoadRoutesResponse.HasGeometry(before, "geometry"));
    Assert.False(
      NextLoadRoutesResponse.HasGeometry(before, "changed-geometry")
    );
    Assert.False(NextLoadRoutesResponse.HasGeometry(null, "geometry"));
  }

  [Fact]
  public void UnchangedResponseOmitsRoutesAndChangedGeometryInvalidatesRevision()
  {
    var truck = Guid.NewGuid();
    var current = Guid.NewGuid();
    var points = new List<RoutePoint> { new(40, -80), new(41, -79) };
    NextLoadRoute load = new(
      Guid.NewGuid(),
      12,
      "ready",
      [new(10, 100, points)],
      []
    );
    var first = NextLoadRoutesResponse.Create(truck, current, [load], null);
    Assert.False(first.Unchanged);
    Assert.NotNull(first.Routes);
    var repeated = NextLoadRoutesResponse.Create(
      truck,
      current,
      [load],
      first.Revision
    );
    Assert.True(repeated.Unchanged);
    Assert.Null(repeated.Routes);
    points[1] = new(42, -79);
    Assert.False(
      NextLoadRoutesResponse
        .Create(truck, current, [load], first.Revision)
        .Unchanged
    );
  }

  [Fact]
  public void RevisionIncludesSelectionOrderStatusAndEmptyMileage()
  {
    var truck = Guid.NewGuid();
    var current = Guid.NewGuid();
    NextLoadRoute a = new(Guid.NewGuid(), 12, "pending", [], []);
    NextLoadRoute b = new(Guid.NewGuid(), 13, "pending", [], []);
    var first = NextLoadRoutesResponse
      .Create(truck, current, [a, b], null)
      .Revision;
    Assert.False(
      NextLoadRoutesResponse.Create(truck, current, [b, a], first).Unchanged
    );
    Assert.False(
      NextLoadRoutesResponse
        .Create(truck, Guid.NewGuid(), [a, b], first)
        .Unchanged
    );
    Assert.False(
      NextLoadRoutesResponse
        .Create(truck, current, [a with { Status = "ready" }, b], first)
        .Unchanged
    );
    Assert.False(
      NextLoadRoutesResponse
        .Create(
          truck,
          current,
          [a with { Deadhead = new(10, [new(40, -80), new(41, -79)]) }, b],
          first
        )
        .Unchanged
    );
    Assert.False(
      NextLoadRoutesResponse.Create(truck, current, [], first).Unchanged
    );
  }
}
