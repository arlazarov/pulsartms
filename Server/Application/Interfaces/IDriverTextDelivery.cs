using Domain.Entities.Messaging;

namespace Application.Interfaces;

// One text to one driver, delivered by Messaging for a module that decides
// what to say (fuel planning). Messaging owns the channel, the driver's
// reply window, the attempt protocol and the provider's statuses; the
// requester owns the words, the key that makes the same instruction one
// message, and its own references on the attempt row. Nothing is retried
// here: an answer that did not come is unknown, and sending it again is the
// requester's explicit choice.
public interface IDriverTextDelivery
{
  string Channel { get; }

  // Whether the serving company can send now, and until when the driver's
  // reply window under its current business number stays open (null when
  // it is closed, or when no phone is given).
  Task<DriverTextReadiness> ReadinessAsync(string? phone, CancellationToken ct);

  // Sends request.Text to request.Recipient as the next attempt under
  // request.IdempotencyKey, unless an earlier attempt settles it.
  // StillWanted is asked after the attempt is committed and before the
  // provider is called; false withdraws it.
  Task<DriverTextOutcome> SendAsync(
    DriverMessage request,
    bool sendAgain,
    Func<CancellationToken, Task<bool>> stillWanted,
    CancellationToken ct
  );
}

public sealed record DriverTextReadiness(bool Configured, DateTime? WindowEnds);

public enum DriverTextResult
{
  NotConfigured,

  // An earlier attempt under the key was taken by the provider.
  AlreadyTaken,

  // An earlier attempt is being sent now, or won the race for this one.
  InProgress,

  // An earlier attempt has no answer; only sendAgain repeats it.
  Uncertain,
  Withdrawn,
  Accepted,
  Unknown,
  Rejected,
}

// Attempt: the row this call recorded, when it recorded one.
public sealed record DriverTextOutcome(
  DriverTextResult Result,
  DriverMessage? Attempt
);

// Told after commit which attempts a provider notification moved, so the
// requester can refresh what it shows. Not told of anything else.
public interface IDriverTextObserver
{
  void Changed(Guid company, IReadOnlyCollection<DriverMessage> attempts);
}
