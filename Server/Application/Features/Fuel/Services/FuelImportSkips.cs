namespace Application.Features.Fuel.Services;

// Messages the fuel import skipped, remembered by this process so that
// each is reported once, not on every push that meets it again (audit F20).
// A skipped message is not marked imported: it is met again until it
// leaves the mailbox window, and a corrected parser still imports it.
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
