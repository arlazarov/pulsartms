using Server.Tests.Support;

namespace Server.Tests.Fuel;

// What the fuel preview costs in round trips. Counted rather than timed: a
// read that happens twice shows up here whatever the machine is doing, and a
// wall-clock number on an in-memory database would say nothing about a server.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelInputsQueryCountTests
{
  // Reading the work inputs afresh costs these statements, plus the BEGIN and
  // COMMIT of its snapshot transaction, which a command interceptor does not
  // see. Raising either number needs a reason.
  private const int StatementsPerFreshRead = 3;
  private const int StatementsPerPreview = 15;

  [Fact]
  public async Task AFreshInputsReadCostsTheSameEveryTime()
  {
    var probe = new QueryColumnProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      queryColumns: probe
    );
    var inputs = f.Services.FuelInputs;
    var truck = f.State.Plan!.TruckId;

    probe.Clear();
    await inputs.ReadFreshAsync(truck, default);
    var first = probe.Statements.Count;

    probe.Clear();
    await inputs.ReadFreshAsync(truck, default);

    Assert.Equal(StatementsPerFreshRead, first);
    Assert.Equal(first, probe.Statements.Count);
  }

  [Fact]
  public async Task APreviewReadsEachSavedRowOnce()
  {
    var probe = new QueryColumnProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      queryColumns: probe
    );
    var profile = await f.PrepareCalculationAsync();
    var built = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );

    probe.Clear();
    await f.Services.Fuel.EditAsync(
      f.Current.Id,
      new(built.Plan!.CalculatedAt, null),
      false,
      default
    );

    // No statement is sent twice.
    var repeated = probe
      .Statements.GroupBy(x => x)
      .Where(x => x.Count() > 1)
      .Select(x => x.Key.Split('\n')[0])
      .ToArray();
    Assert.Empty(repeated);
    Assert.Equal(StatementsPerPreview, probe.Statements.Count);

    // Here the current load has no execution leg, so each connection is
    // captured rather than re-read, and that branch never consults the
    // prefetch. Reading the saved connections ahead would fetch rows nobody
    // looks at, so the batched connection query must not appear at all.
    // The batched read is the one whose parameters the pair builder names.
    Assert.DoesNotContain(
      probe.Statements,
      x => x.Contains("DispatchDeadheads") && x.Contains("@Dispatch")
    );
    // The base routes are still wanted, and are read together.
    Assert.True(
      probe.Matching("\"DispatchBaseRoutes\"") <= 2,
      "the base routes were read more times than the current load and the "
        + "batched follower read"
    );
  }
}
