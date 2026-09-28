using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchBoardRequestTests
{
  private static readonly DateOnly Day = new(2026, 9, 28);

  // Cards (0) and Papers (2) ask the server for an active search; the
  // Table (1), the Completed history and an empty search do not.
  [Theory]
  [InlineData(0, "1385", false, true)]
  [InlineData(2, "1385", false, true)]
  [InlineData(1, "1385", false, false)]
  [InlineData(0, "1385", true, false)]
  [InlineData(0, " ", false, false)]
  public void OnlyCardsAndPapersSearchActiveLoads(
    int view,
    string search,
    bool completed,
    bool active
  )
  {
    var request = new DispatchBoardRequest(
      1,
      search,
      null,
      view,
      Day,
      completed
    );
    Assert.Equal(active, request.ActiveSearch);
    Assert.Equal(active, request.Url.Contains("&activeSearch=true"));
  }
}
