using Application.Caching;
using Application.Diagnostics.Consistency;
using Application.Features.Routing.Audit;
using Domain.Entities;
using Domain.Models.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Routing;

// CW1 and CW4 of docs/architecture/current-work.md, through the rules the
// auditor runs, on the truck whose first load planning has passed.
public sealed partial class PlanningSummaryRefreshTests
{
  // CW1 reports the conflict the owner gives the board, the map and the
  // summary - the passed first load, its delivery not recorded - once per
  // load, pages after its key, and only for the company asked about.
  [Fact]
  public async Task TheAuditReportsPassedWorkWithTheOwnersConflict()
  {
    await using var f = await Fixture.CreateAsync();
    using var scope = f.Root.CreateScope();
    var rule = Rule<RoutePassedWorkOpenRule>(scope);

    var page = await rule.ReadAsync(Request(null), default);
    var finding = Assert.Single(page.Observed);
    var after = await rule.ReadAsync(Request(finding.EntityKey), default);
    var other = await rule.ReadAsync(Request(null, Guid.NewGuid()), default);

    Assert.Equal($"{f.Passed.Id:N}:", finding.EntityKey);
    Assert.Equal("route_passed_not_delivered", finding.Evidence["conflict"]);
    Assert.Equal(
      f.Summary().WorkConflicts.Select(x => (x.DispatchId, x.Conflict)),
      [(f.Passed.Id, finding.Evidence["conflict"])]
    );
    Assert.False(page.More);
    Assert.Empty(after.Observed);
    Assert.Empty(other.Observed);
  }

  // CW4: a stored summary agrees with the owner's current work; one stored
  // under the current signature for another load is reported.
  [Fact]
  public async Task TheAuditFindsAStoredSummaryNamingOtherWork()
  {
    await using var f = await Fixture.CreateAsync();
    f.Summary();
    await f.PrepareAsync();
    using var scope = f.Root.CreateScope();
    var rule = Rule<SummaryNamesCurrentWorkRule>(scope);

    var agreeing = await rule.ReadAsync(Request(null), default);
    var work = f.Cache.Capture(f.Key, f.Signature())!;
    f.Cache.Complete(
      work,
      f.Signature(),
      new AutomaticPlanningResult(f.Truck.Id, f.Passed.Id, 1395, null, null)
    );
    var faulty = await rule.ReadAsync(Request(null), default);

    Assert.Empty(agreeing.Observed);
    var finding = Assert.Single(faulty.Observed);
    Assert.Equal($"{f.Truck.Id:N}", finding.EntityKey);
    Assert.Equal(f.Passed.Id.ToString(), finding.Evidence["storedDispatchId"]);
    Assert.Equal(f.Next.Id.ToString(), finding.Evidence["currentDispatchId"]);
  }

  // Tracking passed the stored summary's load on another process: the entry
  // still names it, under the signature it was prepared for. The cache
  // retires it on the next read; it is stale, not a publication fault.
  [Fact]
  public async Task AStoredSummaryUnderAnOlderSignatureIsNotAFault()
  {
    await using var f = await Fixture.CreateAsync(firstPassed: false);
    f.Summary();
    await f.PrepareAsync();
    await f.PassFirstAsync();
    f.Root.GetRequiredService<ReadCache>()
      .InvalidateItem("planning-inputs", f.Truck.Id);
    using var scope = f.Root.CreateScope();

    var page = await Rule<SummaryNamesCurrentWorkRule>(scope)
      .ReadAsync(Request(null), default);

    Assert.Equal(
      f.Passed.Id,
      f.Cache.StoredFor(Company.Amf).Single().DispatchId
    );
    Assert.Empty(page.Observed);
  }

  private static T Rule<T>(IServiceScope scope)
    where T : IConsistencyRule =>
    scope.ServiceProvider.GetServices<IConsistencyRule>().OfType<T>().Single();

  private static ConsistencyPageRequest Request(
    string? after,
    Guid? company = null
  ) =>
    new(
      company ?? Company.Amf,
      DateTime.UtcNow,
      after,
      10,
      TimeSpan.FromMinutes(30)
    );
}
