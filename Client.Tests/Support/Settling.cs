using Bunit;
using Microsoft.AspNetCore.Components;

namespace Client.Tests.Support;

// A page whose reads land independently renders again as each one arrives,
// and a control found before the last of those renders may be replaced.
// Waits until the page has not rendered for a short while.
internal static class Settling
{
  public static void Settle<T>(
    this IRenderedComponent<T> page,
    int quietMilliseconds = 60
  )
    where T : IComponent
  {
    var deadline = DateTime.UtcNow.AddSeconds(5);
    var renders = page.RenderCount;
    var since = DateTime.UtcNow;
    while (DateTime.UtcNow < deadline)
    {
      Thread.Sleep(10);
      if (page.RenderCount != renders)
      {
        renders = page.RenderCount;
        since = DateTime.UtcNow;
      }
      else if ((DateTime.UtcNow - since).TotalMilliseconds >= quietMilliseconds)
        return;
    }
  }
}
