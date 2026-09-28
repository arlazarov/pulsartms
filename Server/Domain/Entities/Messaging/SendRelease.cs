namespace Domain.Entities.Messaging;

// An administrator's record that a deployed revision may send to the
// messaging provider: the revision before it has been shown drained by the
// platform, so no webhook can reach a binary that would drop a status of
// what this one sends (audit F27, the release overlap). Not carrier data:
// one record per revision of the service.
public sealed class SendRelease
{
  public Guid Id { get; set; }
  public string Revision { get; set; } = "";
  public DateTime ReleasedAt { get; set; }
  public string ReleasedBy { get; set; } = "";
}
