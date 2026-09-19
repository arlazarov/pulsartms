using Bunit;
using Client.Models.DTO;
using Client.Shared;
using Client.Shared.Dispatch;
using Client.Shared.Dispatch.LoadNumber;
using Microsoft.AspNetCore.Components;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class LoadNumberComponentTests
{
  [Fact]
  public void MissingSettingsDoesNotInventTheDefaultPrefix()
  {
    using var context = new BunitContext();
    var component = context.Render<LoadNumber>(parameters =>
      parameters.Add(number => number.Value, 1373)
    );

    component.MarkupMatches("1373");
  }

  [Theory]
  [InlineData("AMF", "AMF1373")]
  [InlineData("TMS-", "TMS-1373")]
  [InlineData("", "1373")]
  public void UsesTheCascadedPrefixWithoutRequests(
    string prefix,
    string expected
  )
  {
    using var context = new BunitContext();
    var component = Render(context, new(prefix, 1, null));

    component.MarkupMatches(expected);
  }

  [Fact]
  public void CascadeChangesRefreshExistingLabelIncludingAnExplicitBlankPrefix()
  {
    using var context = new BunitContext();
    var component = Render(context, new("AMF", 1, null));
    component.MarkupMatches("AMF1373");

    component.Render(parameters =>
      parameters
        .Add(
          cascade => cascade.Value,
          new DispatchSettingsState("TMS-", 2, null)
        )
        .AddChildContent<LoadNumber>(number =>
          number.Add(value => value.Value, 1373)
        )
    );
    component.MarkupMatches("TMS-1373");

    component.Render(parameters =>
      parameters
        .Add(cascade => cascade.Value, new DispatchSettingsState("", 3, null))
        .AddChildContent<LoadNumber>(number =>
          number.Add(value => value.Value, 1373)
        )
    );
    component.MarkupMatches("1373");
  }

  [Fact]
  public void UnknownNumberDoesNotRenderAPrefixWithoutANumber()
  {
    using var context = new BunitContext();
    var component = context.Render<CascadingValue<DispatchSettingsState>>(
      parameters =>
        parameters
          .Add(cascade => cascade.Value, new("AMF", 1, null))
          .AddChildContent<LoadNumber>(number =>
            number.Add(value => value.Value, null)
          )
    );

    component.MarkupMatches("—");
  }

  [Fact]
  public void PrefixIsRenderedAsTextNotMarkup()
  {
    using var context = new BunitContext();
    var component = Render(context, new("<img>", 1, null));

    Assert.Empty(component.FindAll("img"));
    Assert.Contains("&lt;img&gt;1373", component.Markup);
  }

  private static IRenderedComponent<
    CascadingValue<DispatchSettingsState>
  > Render(BunitContext context, DispatchSettingsState settings) =>
    context.Render<CascadingValue<DispatchSettingsState>>(parameters =>
      parameters
        .Add(cascade => cascade.Value, settings)
        .AddChildContent<LoadNumber>(number =>
          number.Add(value => value.Value, 1373)
        )
    );
}
