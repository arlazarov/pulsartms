using Bunit;
using Microsoft.AspNetCore.Components;

namespace Client.Tests.Support;

// A read a page makes on its own after it has shown what a test waits for:
// the WhatsApp webhook address after the provider cards, the list read
// again after a chat opens or a read is acknowledged. Its answer renders
// the page once more, and a render replaces the handlers of controls such
// as `@bind` inputs and per-item buttons. A test that finds a control while
// that render is on its way can dispatch to a handler the render has just
// removed (UnknownEventHandlerIdException), depending only on timing.
//
// The fake holds the request here; the test releases it and waits until
// the page shows what that answer carries. Once every such read has been
// applied, the page has nothing more on its way and a control found then
// is the one that stays.
internal sealed class HeldRequest
{
  private readonly TaskCompletionSource _asked = new(
    TaskCreationOptions.RunContinuationsAsynchronously
  );
  private readonly TaskCompletionSource _answer = new(
    TaskCreationOptions.RunContinuationsAsynchronously
  );

  public bool Asked => _asked.Task.IsCompleted;

  // Called by the fake when the page makes the request.
  public Task HoldAsync()
  {
    _asked.TrySetResult();
    return _answer.Task;
  }

  // Answers the request once the page has asked it; the test then waits
  // for what that answer, and nothing else, puts on the page.
  public void Release()
  {
    WaitAsked();
    _answer.TrySetResult();
  }

  // Lets a request go without waiting for anything, for a test that ends
  // with it still held.
  public void Answer() => _answer.TrySetResult();

  // Waited on directly: a held request renders nothing, and bUnit checks a
  // waited-for assertion only when the page renders.
  public void WaitAsked() =>
    Assert.True(
      _asked.Task.Wait(BunitContext.DefaultWaitTimeout),
      "The page never asked."
    );

  // Answers it and waits until `applied` holds: something only this answer
  // shows. Any render would not do, since another may come first.
  public void Release<T>(IRenderedComponent<T> page, Action applied)
    where T : IComponent
  {
    Release();
    page.WaitForAssertion(applied);
  }
}
