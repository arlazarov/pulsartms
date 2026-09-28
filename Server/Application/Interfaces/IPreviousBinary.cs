namespace Application.Interfaces;

// Whether a binary released before audit F27 still runs beside this one,
// as during a release. Such a binary neither keeps a delivery status that
// arrives before its provider id is saved nor applies one kept for it, so
// while it still receives webhooks a status of a message this binary sends
// can be dropped there. It is recognised by the synchronization loop's
// lease: every binary's loop holds it while it runs and releases it when
// it stops (a dead one's runs out within three minutes), and every binary
// from this one on marks its owner.
public interface IPreviousBinary
{
  const string Marker = "keeps-early-statuses:";

  Task<bool> RunsAsync(CancellationToken ct);
}
