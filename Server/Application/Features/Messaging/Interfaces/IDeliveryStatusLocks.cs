namespace Application.Features.Messaging.Interfaces;

// Orders, per message, the webhook that finds no provider id and the
// sender that saves it (audit F27): whichever takes the lock second sees
// what the first committed. Held to the end of the caller's transaction.
// Order: a webhook takes its carrier's admission lock first, then message
// locks sorted by provider id; a sender takes one message lock only - so no
// two holders wait on each other in a cycle.
public interface IDeliveryStatusLocks
{
  // Serializes, per carrier, the webhooks that keep statuses, so the count
  // that bounds them is not read by two at once.
  Task LockAdmissionAsync(Guid company, CancellationToken ct);

  Task LockAsync(
    Guid company,
    string channel,
    string businessNumber,
    string providerMessageId,
    CancellationToken ct
  );
}
