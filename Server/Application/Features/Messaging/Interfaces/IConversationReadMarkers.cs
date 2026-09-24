namespace Application.Features.Messaging.Interfaces;

// One dispatcher's read marker for one conversation. Advancing it is a
// single atomic statement that keeps the higher revision, so two writers,
// in any order, never move it back.
public interface IConversationReadMarkers
{
  Task AdvanceAsync(
    Guid company,
    Guid conversation,
    Guid user,
    long revision,
    CancellationToken ct
  );
}
