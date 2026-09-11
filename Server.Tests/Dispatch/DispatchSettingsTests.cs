using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Domain.Entities.Dispatch;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchSettingsTests
{
  [Fact]
  public async Task DefaultReadDoesNotWriteAndBlankPrefixSurvivesAnotherContext()
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var sender = fixture.Services.GetRequiredService<IMediator>();
    var db = fixture.Services.GetRequiredService<AppDbContext>();
    var initial = await sender.Send(new GetDispatchSettingsQuery());
    Assert.Equal(new("AMF", 0, null), initial.Response);
    Assert.Empty(await db.DispatchSettings.ToListAsync());

    var saved = await sender.Send(new UpdateDispatchSettingsCommand("   ", 0));
    Assert.True(saved.Success);
    Assert.Equal(string.Empty, saved.Response!.LoadNumberPrefix);
    Assert.Equal(1, saved.Response.Revision);
    Assert.NotNull(saved.Response.UpdatedAt);

    await using var reloaded = new AppDbContext(fixture.Services.GetRequiredService<DbContextOptions<AppDbContext>>());
    var actual = await new GetDispatchSettingsHandler(reloaded).Handle(new(), default);
    Assert.Equal(saved.Response, actual.Response);
    Assert.Empty(await reloaded.FleetPlanningSettings.ToListAsync());
    Assert.Empty(await reloaded.DispatchRoutePlans.ToListAsync());
    Assert.Empty(await reloaded.SynchronizationCheckpoints.ToListAsync());
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task NormalizationPreservesCaseAndAnUnchangedSaveKeepsRevision()
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var sender = fixture.Services.GetRequiredService<IMediator>();
    var saved = await sender.Send(new UpdateDispatchSettingsCommand("  Ab-  ", 0));
    Assert.True(saved.Success);
    Assert.Equal("Ab-", saved.Response!.LoadNumberPrefix);
    var repeated = await sender.Send(new UpdateDispatchSettingsCommand("Ab- ", saved.Response.Revision));
    Assert.Equal(saved.Response, repeated.Response);
    var changed = await sender.Send(new UpdateDispatchSettingsCommand("X", saved.Response.Revision));
    Assert.True(changed.Success);
    Assert.Equal(saved.Response.Revision + 1, changed.Response!.Revision);
    var stale = await sender.Send(new UpdateDispatchSettingsCommand("Ab-", saved.Response.Revision));
    Assert.False(stale.Success);
    Assert.Equal(409, stale.StatusCode);
    Assert.Equal("X", (await sender.Send(new GetDispatchSettingsQuery())).Response!.LoadNumberPrefix);
  }

  [Theory]
  [InlineData(null, 0L)]
  [InlineData("12345678901234567", 0L)]
  [InlineData("AMF\n", 0L)]
  [InlineData("\t", 0L)]
  [InlineData("A\0B", 0L)]
  [InlineData("AMF", -1L)]
  [InlineData("AMF", long.MaxValue)]
  public async Task InvalidUpdatesAreRejectedBeforeStorage(string? prefix, long revision)
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var result = await fixture.Services.GetRequiredService<IMediator>().Send(new UpdateDispatchSettingsCommand(prefix, revision));
    Assert.False(result.Success);
    Assert.Equal(400, result.StatusCode);
    Assert.Empty(await fixture.Services.GetRequiredService<AppDbContext>().DispatchSettings.ToListAsync());
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task SixteenCharactersAreAllowedAfterTrimming()
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var result = await fixture.Services.GetRequiredService<IMediator>()
      .Send(new UpdateDispatchSettingsCommand("  1234567890123456  ", 0));
    Assert.True(result.Success);
    Assert.Equal("1234567890123456", result.Response!.LoadNumberPrefix);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ConcurrentInsertOrUpdateCannotOverwriteTheWinningSession(bool existing)
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var options = fixture.Services.GetRequiredService<DbContextOptions<AppDbContext>>();
    await using var winner = new AppDbContext(options);
    var winnerHandler = new UpdateDispatchSettingsHandler(winner, TimeProvider.System);
    if (existing) Assert.True((await winnerHandler.Handle(new("Original", 0), default)).Success);
    var revision = existing ? 1 : 0;
    var interceptor = new BeforeSave(async () =>
      Assert.True((await winnerHandler.Handle(new("Winner", revision), default)).Success));
    await using var losing = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(interceptor).Options);

    var result = await new UpdateDispatchSettingsHandler(losing, TimeProvider.System).Handle(new("Loser", revision), default);

    Assert.False(result.Success);
    Assert.Equal(409, result.StatusCode);
    Assert.Empty(losing.ChangeTracker.Entries<DispatchSettings>());
    var saved = (await new GetDispatchSettingsHandler(losing).Handle(new(), default)).Response!;
    Assert.Equal("Winner", saved.LoadNumberPrefix);
    Assert.Equal(revision + 1, saved.Revision);
  }

  private sealed class BeforeSave(Func<Task> action) : SaveChangesInterceptor
  {
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      await action();
      return result;
    }
  }
}
