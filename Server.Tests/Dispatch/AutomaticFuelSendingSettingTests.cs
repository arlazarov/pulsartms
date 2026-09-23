using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Queries;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Dispatch;

// Sending a fuel plan is manual unless a company turns it on. A company
// with no settings row is off, a client that does not know the setting
// cannot turn it on or off by accident, and turning it on is a saved,
// explicit choice.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class AutomaticFuelSendingSettingTests
{
  [Fact]
  public async Task ItIsOffUntilACompanyTurnsItOnAndStaysWhereItWasPut()
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var sender = fixture.Services.GetRequiredService<IMediator>();

    // No settings row at all.
    var initial = (await sender.Send(new GetDispatchSettingsQuery())).Response!;
    Assert.False(initial.AutomaticFuelSending);

    // Saving other settings does not turn it on.
    var units = await sender.Send(
      new UpdateDispatchSettingsCommand("AMF", 0, "celsius", "miles")
    );
    Assert.False(units.Response!.AutomaticFuelSending);

    var on = await sender.Send(
      new UpdateDispatchSettingsCommand(
        "AMF",
        units.Response.Revision,
        AutomaticFuelSending: true
      )
    );
    Assert.True(on.Response!.AutomaticFuelSending);

    // An older client saving the prefix leaves it as it is.
    var legacy = await sender.Send(
      new UpdateDispatchSettingsCommand("NEW", on.Response.Revision)
    );
    Assert.True(legacy.Response!.AutomaticFuelSending);

    var off = await sender.Send(
      new UpdateDispatchSettingsCommand(
        "NEW",
        legacy.Response.Revision,
        AutomaticFuelSending: false
      )
    );
    Assert.False(off.Response!.AutomaticFuelSending);

    await using var another = new AppDbContext(
      fixture.Services.GetRequiredService<DbContextOptions<AppDbContext>>()
    );
    Assert.False(
      (await new GetDispatchSettingsHandler(another).Handle(new(), default))
        .Response!.AutomaticFuelSending
    );
  }
}
