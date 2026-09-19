using Bunit;
using Client.Components;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class LoadingIndicatorTests
{
  [Fact]
  public void LoadingTextIsOnlyExposedToAssistiveTechnology()
  {
    using var context = new BunitContext();
    var component = context.Render<LoadingIndicator>();
    var status = component.Find("[role=status]");
    Assert.Equal(
      "Loading…",
      status.QuerySelector(".visually-hidden")!.TextContent
    );
    Assert.Equal(
      3,
      status.QuerySelectorAll("[aria-hidden=true] > span").Length
    );
    Assert.Empty(
      status.QuerySelector("[aria-hidden=true]")!.TextContent.Trim()
    );
  }
}
