namespace Application.Features.Messaging.Options;

public sealed class SendHoldOptions
{
  // True where a release can overlap an older binary (production): each
  // revision sends nothing to the provider until an administrator records
  // that the revision before it has been drained.
  public bool RequireRelease { get; set; }
}
