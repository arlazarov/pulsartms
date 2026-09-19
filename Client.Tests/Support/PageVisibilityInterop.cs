using Bunit;
using Client.Services;
using Microsoft.JSInterop;

namespace Client.Tests.Support;

internal sealed class PageVisibilityInterop
{
  private PageVisibility? owner;
  public bool IsVisible { get; private set; } = true;

  public void SetVisible(bool visible)
  {
    IsVisible = visible;
    owner?.VisibilityChanged(visible);
  }

  public void Configure(BunitJSInterop js)
  {
    var module = js.SetupModule("./js/generated/shared/pageVisibility.js");
    var observer = module.SetupModule(
      "observeVisibility",
      invocation =>
      {
        owner = (
          (DotNetObjectReference<PageVisibility>)invocation.Arguments[0]!
        ).Value;
        return true;
      }
    );
    observer.Setup<bool?>("isVisible").SetResult(IsVisible);
    observer
      .SetupVoid(
        "dispose",
        _ =>
        {
          owner = null;
          return true;
        }
      )
      .SetVoidResult();
  }
}
