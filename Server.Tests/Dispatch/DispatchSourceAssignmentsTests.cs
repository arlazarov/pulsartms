using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchSourceAssignmentsTests
{
  [Fact]
  public void UniformVisitAdditionDoesNotInventAResourceChange()
  {
    var source = Source("A", "A");
    var before = Signature(source);
    source.Stops.Add(new() { Sequence = 3, TruckNumber = "A" });
    Assert.Equal(before, Signature(source));
  }

  [Fact]
  public void MovingResourceBoundaryCannotReuseEarlierProposalIdentity()
  {
    var source = Source("A", "A", "B");
    var before = Signature(source);
    source.Stops[1].TruckNumber = "B";
    Assert.NotEqual(before, Signature(source));
  }

  [Fact]
  public void CatalogResolutionChangesProposalEvenWhenNamesAreUnchanged()
  {
    var source = Source("A", "A");
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    Assert.NotEqual(Signature(source, first), Signature(source, second));
    Assert.NotEqual(Signature(source, first), Signature(source));
  }

  private static string Signature(ExternalDispatch source, Guid? id = null) =>
    DispatchSourceAssignments.Fingerprint(
      source,
      _ => id,
      _ => null,
      _ => null
    );

  private static ExternalDispatch Source(params string[] trucks) =>
    new()
    {
      Stops = trucks
        .Select(
          (truck, index) =>
            new ExternalDispatchStop
            {
              Sequence = index + 1,
              TruckNumber = truck,
            }
        )
        .ToList(),
    };
}
