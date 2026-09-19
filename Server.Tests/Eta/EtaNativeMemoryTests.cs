using Application.Features.Eta.Services;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaNativeMemoryTests
{
  private static readonly DateTime Now = new(
    2026,
    9,
    13,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public void DifferentLegsOfOneLoadHaveSeparateResultsAndCommercialIdentity()
  {
    using var memory = new EtaMemory();
    var load = Guid.NewGuid();
    var firstLeg = Guid.NewGuid();
    var secondLeg = Guid.NewGuid();
    var legacy = memory.Scope(load, null);
    var first = memory.Scope(load, firstLeg);
    var second = memory.Scope(load, secondLeg);
    var legacyResult = Entry("legacy");
    var firstResult = Entry("first");
    var secondResult = Entry("second");
    memory.Results[legacy] = legacyResult;
    memory.Results[first] = firstResult;
    memory.Results[second] = secondResult;

    Assert.Equal(load, legacy);
    Assert.Equal(firstLeg, first);
    Assert.Equal(secondLeg, second);
    Assert.Equal(3, memory.Results.Count);
    Assert.Same(legacyResult, memory.Results[legacy]);
    Assert.Same(firstResult, memory.Results[first]);
    Assert.Same(secondResult, memory.Results[second]);
    Assert.Equal(
      new EtaMemory.ScopeIdentity(load, null),
      memory.Resolve(legacy)
    );
    Assert.Equal(
      new EtaMemory.ScopeIdentity(load, firstLeg),
      memory.Resolve(first)
    );
    Assert.Equal(
      new EtaMemory.ScopeIdentity(load, secondLeg),
      memory.Resolve(second)
    );
    Assert.Equal(first, memory.Scope(load, firstLeg));
  }

  [Fact]
  public void ChangedDemandInvalidatesOnlyItsOwnLeg()
  {
    using var memory = new EtaMemory();
    var load = Guid.NewGuid();
    var first = memory.Scope(load, Guid.NewGuid());
    var second = memory.Scope(load, Guid.NewGuid());
    var other = Entry("second") with { ChainInputHash = "other-inputs" };
    memory.Results[first] = Entry("first") with
    {
      ChainInputHash = "prior-assignment",
    };
    memory.Results[second] = other;
    memory.Results[load] = Entry("legacy");

    memory.Demand(first, "new-assignment", Now);

    Assert.False(memory.Results.ContainsKey(first));
    Assert.Same(other, memory.Results[second]);
    Assert.True(memory.Results.ContainsKey(load));
    Assert.Contains(first, memory.Due(Now));
    Assert.DoesNotContain(second, memory.Due(Now));
    Assert.Equal(load, memory.Resolve(first).DispatchId);
  }

  [Fact]
  public void ExpiredViewForgetsOnlyItsLegAndRetainsTheOtherLegsMapping()
  {
    using var memory = new EtaMemory();
    var load = Guid.NewGuid();
    var first = memory.Scope(load, Guid.NewGuid());
    var secondLeg = Guid.NewGuid();
    var second = memory.Scope(load, secondLeg);
    memory.View(first, Now.AddMinutes(-11));
    memory.View(second, Now);
    memory.Results[first] = Entry("first");
    memory.Results[second] = Entry("second");

    Assert.Empty(memory.Due(Now));

    Assert.False(memory.Results.ContainsKey(first));
    Assert.False(memory.Viewed.ContainsKey(first));
    Assert.True(memory.Results.ContainsKey(second));
    Assert.True(memory.Viewed.ContainsKey(second));
    Assert.Equal(
      new EtaMemory.ScopeIdentity(load, secondLeg),
      memory.Resolve(second)
    );
  }

  private static EtaMemory.Entry Entry(string signature) =>
    new(signature, new(Now, Now.AddMinutes(2), [], null, []));
}
