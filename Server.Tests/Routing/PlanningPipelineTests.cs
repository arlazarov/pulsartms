using API.Controllers;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class PlanningPipelineTests
{
  [Fact]
  public async Task MediatorValidatesRouteCommandsBeforeCallingTheProvider()
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var sender = fixture.Services.GetRequiredService<ISender>();
    var response = await sender.Send(
      new RecalculateFuelPlanCommand(Guid.Empty)
    );
    Assert.False(response.Success);
    Assert.Equal(400, response.StatusCode);
    Assert.Contains(response.Errors!, error => error.Contains("DispatchId"));
    Assert.Equal(0, fixture.Router.Calls);
    var missing = await sender.Send(new GetRoutePlanningQuery(Guid.NewGuid()));
    Assert.False(missing.Success);
    Assert.Contains("Dispatch not found.", missing.Errors!);
  }

  [Fact]
  public async Task SettingsUseTheSameMediatorPipelineAndKeepConflictStatus()
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var sender = fixture.Services.GetRequiredService<ISender>();
    var defaults = await sender.Send(new GetPlanningSettingsQuery());
    Assert.True(defaults.Success);
    var controller = new SettingsController
    {
      ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext
        {
          RequestServices = fixture.Services,
        },
      },
    };
    var saved = Assert.IsType<ObjectResult>(
      await controller.Save(new(new() { UseIfta = false }, 0), default)
    );
    Assert.Equal(200, saved.StatusCode);
    var conflict = Assert.IsType<ObjectResult>(
      await controller.Save(new(new() { UseIfta = true }, 0), default)
    );
    Assert.Equal(409, conflict.StatusCode);
    var invalid = Assert.IsType<ObjectResult>(
      await controller.Save(new(new() { FillPercent = 101 }, 1), default)
    );
    Assert.Equal(400, invalid.StatusCode);
    var current = await sender.Send(new GetPlanningSettingsQuery());
    Assert.False(current.Response!.Preferences.UseIfta);
    Assert.Equal(1, current.Response.Revision);
  }
}
