using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Messaging;

// An administrator records the templates Meta approved for the number the
// carrier sends from now. They belong to that carrier and that number:
// another carrier, or the same carrier after moving to another number, has
// none of them.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class ApprovedTemplateTests
{
  private static readonly Guid Other = new(
    "b0f0b0f0-0000-4000-8000-000000000002"
  );

  [Fact]
  public async Task ATemplateIsRecordedForThisCarriersCurrentNumberOnly()
  {
    await using var f = await ReplyFixture.CreateAsync();

    var added = await Handlers(f).Handle(Approve("fuel_plan_ready"), default);
    var again = await Handlers(f).Handle(Approve("fuel_plan_ready"), default);

    Assert.True(added.Success);
    Assert.Equal(409, again.StatusCode);
    var row = await f.Db.ApprovedTemplates.AsNoTracking().SingleAsync();
    Assert.Equal(
      (Domain.Entities.Company.Amf, "123456", 1),
      (row.CompanyId, row.BusinessNumberId, row.Parameters)
    );
    Assert.Single(await ListAsync(f));

    // The carrier moves to another number: nothing is approved for it.
    f.Messaging.BusinessNumber = "999999";
    var moved = (
      await Handlers(f).Handle(new GetApprovedTemplatesQuery(), default)
    ).Response!;
    Assert.Equal(
      ("999999", 0),
      (moved.BusinessNumberId, moved.Templates.Count)
    );

    // Another carrier's template for the same number id is not this one's.
    f.Messaging.BusinessNumber = "123456";
    f.Db.ApprovedTemplates.Add(
      new ApprovedTemplate
      {
        Id = Guid.NewGuid(),
        CompanyId = Other,
        Channel = row.Channel,
        BusinessNumberId = "123456",
        Name = "theirs",
        Language = "en_US",
        Text = "Theirs.",
      }
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal("fuel_plan_ready", Assert.Single(await ListAsync(f)).Name);
  }

  [Theory]
  [InlineData("Fuel Plan", "en_US", 1, "Load {{1}}")]
  [InlineData("fuel_plan", "english", 1, "Load {{1}}")]
  [InlineData("fuel_plan", "en_US", 2, "Load {{1}}")]
  [InlineData("fuel_plan", "en_US", 1, "Load {{2}}")]
  [InlineData("fuel_plan", "en_US", 0, "Load {{1}}")]
  [InlineData("fuel_plan", "en_US", 11, "Load")]
  [InlineData("fuel_plan", "en_US", 0, "")]
  public async Task WhatMetaWouldNotAcceptIsRefused(
    string name,
    string language,
    int parameters,
    string text
  )
  {
    await using var f = await ReplyFixture.CreateAsync();

    var result = await Handlers(f)
      .Handle(
        new ApproveTemplateCommand(name, language, parameters, text),
        default
      );

    Assert.Equal(400, result.StatusCode);
    Assert.Empty(await f.Db.ApprovedTemplates.ToListAsync());
  }

  [Fact]
  public async Task OnlyAnAdministratorManagesTemplatesAndNothingWithoutANumber()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var dispatcher = Handlers(f, "Dispatch");

    Assert.Equal(
      403,
      (await dispatcher.Handle(Approve("fuel_plan_ready"), default)).StatusCode
    );
    Assert.Equal(
      403,
      (
        await dispatcher.Handle(new GetApprovedTemplatesQuery(), default)
      ).StatusCode
    );
    var id = (await Handlers(f).Handle(Approve("fuel_plan_ready"), default))
      .Response!
      .Id;
    Assert.Equal(
      403,
      (
        await dispatcher.Handle(new WithdrawTemplateCommand(id), default)
      ).StatusCode
    );
    Assert.True(
      (
        await Handlers(f).Handle(new WithdrawTemplateCommand(id), default)
      ).Response
    );
    Assert.Empty(await ListAsync(f));

    f.Messaging.Configured = false;
    Assert.Equal(
      409,
      (await Handlers(f).Handle(Approve("fuel_plan_ready"), default)).StatusCode
    );
  }

  private static ApproveTemplateCommand Approve(string name) =>
    new(name, "en_US", 1, "Your fuel plan for load {{1}} is ready.");

  private static async Task<IReadOnlyList<ApprovedTemplateView>> ListAsync(
    ReplyFixture f
  ) =>
    (await Handlers(f).Handle(new GetApprovedTemplatesQuery(), default))
      .Response!
      .Templates;

  private static ApprovedTemplateHandlers Handlers(
    ReplyFixture f,
    string role = "Admin"
  )
  {
    f.Db.ChangeTracker.Clear();
    return new(
      f.Db,
      new ReplyFixture.Caller("me"),
      new Role(role),
      f.Messaging,
      new ApprovedTemplates(f.Db, f.Messaging),
      f.Clock
    );
  }

  private sealed class Role(string role) : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult<string?>(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string role,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
