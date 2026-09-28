using Bunit;
using Client.Shared.CopyValue;
using Microsoft.JSInterop;

namespace Client.Tests.Fleet;

// A shown value whose words copy it (Location, Appointment): "Copied" only
// once the browser took the text, and never for a value or selection that
// has changed since the copy began.
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class CopyValueTests : IDisposable
{
  private const string Window = "Sep 28 · 08:00 AM – 01:00 PM EDT";
  private readonly BunitContext _context = new();

  public void Dispose() => _context.Dispose();

  private IRenderedComponent<CopyValue> Render(object scope) =>
    _context.Render<CopyValue>(p =>
      p.Add(x => x.Value, Window)
        .Add(x => x.Scope, scope)
        .AddChildContent("Sep 28 · 08:00 AM – 01:00 PM")
    );

  [Fact]
  public void ItSaysCopiedOnlyAfterTheBrowserTookTheWholeValue()
  {
    var copy = _context.JSInterop.SetupVoid(
      "navigator.clipboard.writeText",
      _ => true
    );
    var component = Render(1);
    component.Find("button.copy-value").Click();
    Assert.Empty(component.FindAll(".copy-value__status"));
    Assert.Equal(Window, copy.Invocations.Single().Arguments[0]);
    copy.SetVoidResult();
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "Copied",
          component.Find(".copy-value__status").TextContent
        )
    );
  }

  [Fact]
  public void ARefusedCopySaysSo()
  {
    _context
      .JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true)
      .SetException(new JSException("Denied"));
    var component = Render(1);
    component.Find("button.copy-value").Click();
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "Could not copy",
          component.Find(".copy-value__status").TextContent
        )
    );
  }

  [Fact]
  public void ACopyFinishingAfterTheSelectionChangedSaysNothing()
  {
    var copy = _context.JSInterop.SetupVoid(
      "navigator.clipboard.writeText",
      _ => true
    );
    var component = Render(1);
    component.Find("button.copy-value").Click();
    component.Render(p => p.Add(x => x.Scope, 2));
    copy.SetVoidResult();
    Assert.Empty(component.FindAll(".copy-value__status"));
  }
}
