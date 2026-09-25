using System.Web;
using Client.Services;

namespace Client.Tests.Dispatch;

// A load page returns to the page that opened it, with that page's place,
// and only ever to a page of this app: a crafted link must not be able to
// send the reader to another site.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ReturnNavigationTests
{
  private static readonly Guid Load = Guid.Parse(
    "9b0c9c1e-6a47-4c58-9d0a-0d4c5f3e2a11"
  );
  private static readonly Guid Truck = Guid.Parse(
    "1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b"
  );

  [Theory]
  [InlineData("/fleet/map", "Back to map")]
  [InlineData("/fleet/map?truckId=1&dispatchId=2", "Back to map")]
  [InlineData("/dispatch", "Back to Dispatch")]
  [InlineData("/dispatch?scope=completed&q=11006&page=3", "Back to Dispatch")]
  [InlineData("/messages", "Back to Messages")]
  [InlineData(
    "/messages/1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b",
    "Back to conversation"
  )]
  public void AnOwnPageIsReturnedToAsItWas(string from, string label) =>
    Assert.Equal(new ReturnLink(from, label), ReturnNavigation.Resolve(from));

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("https://example.com/")]
  [InlineData("//example.com/fleet/map")]
  [InlineData("/\\example.com")]
  [InlineData("\\\\example.com")]
  [InlineData("javascript:alert(1)")]
  [InlineData("fleet/map")]
  [InlineData("/fleet/map#top")]
  [InlineData("/fleet/map\n")]
  [InlineData("/settings")]
  [InlineData("/dispatch/9b0c9c1e-6a47-4c58-9d0a-0d4c5f3e2a11")]
  [InlineData("/messages/not-a-conversation")]
  [InlineData("/messages/1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b/x")]
  [InlineData("/fleet/map/../../settings")]
  public void AnythingElseReturnsToDispatch(string? from) =>
    Assert.Equal(ReturnNavigation.Fallback, ReturnNavigation.Resolve(from));

  [Fact]
  public void AnOverlongAddressReturnsToDispatch() =>
    Assert.Equal(
      ReturnNavigation.Fallback,
      ReturnNavigation.Resolve("/dispatch?q=" + new string('x', 2048))
    );

  [Fact]
  public void TheOriginTravelsEscapedAndComesBackWhole()
  {
    var origin = ReturnNavigation.DispatchList(null, true, " A&B ", 2);
    Assert.Equal("/dispatch?scope=completed&q=A%26B&page=2", origin);

    var url = ReturnNavigation.Load(Load, origin, Truck);

    Assert.Equal(
      $"/dispatch/{Load}?stopId={Truck}&from=" + Uri.EscapeDataString(origin),
      url
    );
    var query = HttpUtility.ParseQueryString(
      new Uri(new Uri("https://app.test"), url).Query
    );
    Assert.Equal(origin, query[ReturnNavigation.Parameter]);
  }

  [Fact]
  public void ARefusedOriginIsNotCarried() =>
    Assert.Equal(
      $"/dispatch/{Load}",
      ReturnNavigation.Load(Load, "https://example.com/")
    );

  [Fact]
  public void EachOriginWritesOnlyWhatItHas()
  {
    Assert.Equal(
      "/dispatch",
      ReturnNavigation.DispatchList(null, false, "", 1)
    );
    Assert.Equal(
      $"/fleet/map?truckId={Truck}&dispatchId={Load}",
      ReturnNavigation.FleetMap(Truck, Load)
    );
    Assert.Equal("/fleet/map", ReturnNavigation.FleetMap(null, null));
    Assert.Equal($"/messages/{Truck}", ReturnNavigation.Conversation(Truck));
  }
}
