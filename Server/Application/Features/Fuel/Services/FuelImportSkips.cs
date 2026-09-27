namespace Application.Features.Fuel.Services;

// Messages the fuel import skipped, remembered by this process so that
// each is reported once, not on every push that meets it again (audit F20).
// A skipped message is not marked imported: it is met again, and a
// corrected parser imports it, only while it is in the mailbox window -
// about two days once later messages import. After that nothing retries
// it; its one warning is the only trace.
// Bounded; a message forgotten is reported once more, which is all it
// costs.
public sealed class FuelImportSkips
{
  public const int Capacity = 256;
  private readonly object gate = new();
  private readonly HashSet<string> seen = [];
  private readonly Queue<string> order = [];

  // True the first time this process meets the message.
  public bool First(string messageId)
  {
    lock (gate)
    {
      if (!seen.Add(messageId))
        return false;
      order.Enqueue(messageId);
      if (order.Count > Capacity)
        seen.Remove(order.Dequeue());
      return true;
    }
  }
}
