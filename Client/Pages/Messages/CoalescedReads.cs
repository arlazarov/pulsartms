namespace Client.Pages.Messages;

// One read at a time per key. A demand that comes while a read for its key
// is on its way is kept, and served by exactly one more read after it,
// however many demands came meanwhile: a change committed after the
// running read's snapshot is never lost, and a burst costs two reads, not
// one per demand. Every demand's task completes only after a read that
// began after it, unless Wanted says the key no longer matters (the
// conversation was closed). Runs on the renderer's single thread.
internal sealed class CoalescedReads<TKey>
  where TKey : notnull
{
  private readonly Dictionary<TKey, Run> _runs = [];

  private sealed class Run
  {
    public Task Task = Task.CompletedTask;
    public bool Dirty;
  }

  public bool IsReading(TKey key) => _runs.ContainsKey(key);

  public Task RequestAsync(TKey key, Func<Task> read, Func<bool> wanted)
  {
    if (_runs.TryGetValue(key, out var running))
    {
      running.Dirty = true;
      return running.Task;
    }
    var run = new Run();
    _runs[key] = run;
    run.Task = LoopAsync(key, run, read, wanted);
    return run.Task;
  }

  private async Task LoopAsync(
    TKey key,
    Run run,
    Func<Task> read,
    Func<bool> wanted
  )
  {
    try
    {
      do
      {
        run.Dirty = false;
        await read();
      } while (run.Dirty && wanted());
    }
    finally
    {
      if (_runs.TryGetValue(key, out var current) && current == run)
        _runs.Remove(key);
    }
  }
}
