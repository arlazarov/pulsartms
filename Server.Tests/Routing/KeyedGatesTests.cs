using Application.Caching;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class KeyedGatesTests
{
  [Fact]
  public async Task ReusesBoundedOwnerLocalGatesWithoutSerializingEveryKey()
  {
    using var gates = new KeyedGates();
    using var otherOwner = new KeyedGates();
    var key = Guid.NewGuid();
    var held = gates.For(key);
    Assert.Same(held, gates.For(key));
    Assert.NotSame(held, otherOwner.For(key));
    var stripes = Enumerable.Range(0, 10000).Select(i => gates.For(i.ToString())).Distinct().ToArray();
    Assert.InRange(stripes.Length, 2, 64);
    var independent = stripes.First(x => !ReferenceEquals(x, held));
    await held.WaitAsync();
    try
    {
      Assert.False(await gates.For(key).WaitAsync(0));
      Assert.True(await independent.WaitAsync(0));
      independent.Release();
    }
    finally { held.Release(); }
  }
}
