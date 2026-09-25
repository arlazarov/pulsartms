using Client.Pages.Messages;

namespace Client.Tests.Messaging;

// One read at a time per key; demands during a read are served by one
// more read after it, and a demand's task ends only after a read that
// began after it.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class CoalescedReadsTests
{
  [Fact]
  public async Task ABurstDuringAReadCostsOneMoreRead()
  {
    var reads = new CoalescedReads<string>();
    var gates = new List<TaskCompletionSource>();
    Task Read()
    {
      var gate = new TaskCompletionSource();
      gates.Add(gate);
      return gate.Task;
    }

    var first = reads.RequestAsync("a", Read, () => true);
    var burst = Enumerable
      .Range(0, 5)
      .Select(_ => reads.RequestAsync("a", Read, () => true))
      .ToList();
    Assert.Single(gates);
    gates[0].SetResult();
    await Task.Yield();
    Assert.Equal(2, gates.Count);
    Assert.False(burst[0].IsCompleted);
    gates[1].SetResult();
    await Task.WhenAll([first, .. burst]);
    Assert.Equal(2, gates.Count);
    Assert.False(reads.IsReading("a"));
  }

  [Fact]
  public async Task KeysDoNotShareTheirDemand()
  {
    var reads = new CoalescedReads<string>();
    var counts = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0 };
    var gate = new TaskCompletionSource();
    Func<Task> Read(string key) =>
      async () =>
      {
        counts[key]++;
        await gate.Task;
      };

    var a = reads.RequestAsync("a", Read("a"), () => true);
    var b = reads.RequestAsync("b", Read("b"), () => true);
    _ = reads.RequestAsync("a", Read("a"), () => true);
    gate.SetResult();
    await Task.WhenAll(a, b);

    Assert.Equal((2, 1), (counts["a"], counts["b"]));
  }

  [Fact]
  public async Task ADemandNoLongerWantedIsNotReadAgain()
  {
    var reads = new CoalescedReads<string>();
    var count = 0;
    var gate = new TaskCompletionSource();
    var wanted = true;
    async Task Read()
    {
      count++;
      await gate.Task;
    }

    var first = reads.RequestAsync("a", Read, () => wanted);
    _ = reads.RequestAsync("a", Read, () => wanted);
    wanted = false;
    gate.SetResult();
    await first;

    Assert.Equal(1, count);
  }
}
