using Bunit;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Messaging;

// A reply kept on leaving a conversation is held until the conversation
// opens again, and never shown to whoever signs in next.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class ReturnPlacesTests
{
  [Fact]
  public void AReplyIsHeldUntilReopenedAndDroppedForTheNextUser()
  {
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(new HttpResponseMessage())
    );
    var places = context.Services.GetRequiredService<ReturnPlaces>();
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();

    places.KeepDraft(first, "Running late");
    places.KeepDraft(second, "   ");
    Assert.Equal("", places.TakeDraft(second));
    Assert.Equal("Running late", places.TakeDraft(first));
    Assert.Equal("", places.TakeDraft(first));

    // A page closing late with nothing written does not erase a reply
    // kept since.
    places.KeepDraft(first, "Kept");
    places.KeepDraft(first, "");
    Assert.Equal("Kept", places.TakeDraft(first));

    places.KeepDraft(first, "Not for the next user");
    context.Authorization.SetAuthorized("Another Dispatcher");
    Assert.Equal("", places.TakeDraft(first));
  }
}
