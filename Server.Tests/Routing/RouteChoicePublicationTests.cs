using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteChoicePublicationTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ChangesAtPublicationPreserveThePreviousChoiceAndDraft(
    bool save
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var first = await f.Preview();
    await f.Choices.SaveAsync(f.Load.Id, f.Owner, new(first.Id, 1, 0), default);
    var draft = await f.Preview();
    var before = (
      await f.Db.DispatchRouteChoices.AsNoTracking().SingleAsync()
    ).ChoiceJson;
    f.Publication.BeforeBegin = async () =>
      await f
        .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "final change"));

    await Assert.ThrowsAsync<RoutePlanningException>(async () =>
    {
      if (save)
        await f.Choices.SaveAsync(
          f.Load.Id,
          f.Owner,
          new(draft.Id, 2, draft.Revision),
          default
        );
      else
        await f.Preview();
    });

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(
      draft.Id,
      (await f.Db.DispatchRoutePreviews.AsNoTracking().SingleAsync()).PreviewId
    );
    var choice = await f.Db.DispatchRouteChoices.AsNoTracking().SingleAsync();
    Assert.Equal(before, choice.ChoiceJson);
    Assert.Equal(1, choice.Revision);
  }
}
