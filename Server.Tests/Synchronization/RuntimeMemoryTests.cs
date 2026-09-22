using Infrastructure.Diagnostics;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class RuntimeMemoryTests
{
  [Fact]
  public void ContainerCountersKeepMissingValuesDistinctFromZero()
  {
    var result = RuntimeMemoryReader.ParseContainer(
      "12345\n",
      "max\n",
      "anon 8192\nfile 0\ninvalid x\n",
      true
    );
    Assert.Equal(12345, result.UsageBytes);
    Assert.Null(result.LimitBytes);
    Assert.Equal(8192, result.AnonymousBytes);
    Assert.Equal(0, result.FileBytes);
    Assert.Null(result.KernelBytes);
  }

  [Fact]
  public void LegacyContainerUsesHierarchicalCountersAndUnlimitedSentinel()
  {
    var result = RuntimeMemoryReader.ParseContainer(
      "100",
      "9223372036854771712",
      "rss 1\ntotal_rss 42\ncache 2\ntotal_cache 18\n",
      false
    );
    Assert.Equal(42, result.AnonymousBytes);
    Assert.Equal(18, result.FileBytes);
    Assert.Null(result.LimitBytes);
    Assert.Null(result.KernelBytes);
  }

  [Fact]
  public void InvalidCountersDoNotBecomeMeasuredZeroes()
  {
    var result = RuntimeMemoryReader.ParseContainer(
      "-1",
      "overflow999999999999999999999",
      null,
      true
    );
    Assert.Null(result.UsageBytes);
    Assert.Null(result.LimitBytes);
    Assert.Null(result.AnonymousBytes);
  }
}
