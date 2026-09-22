using System.Text.Json;
using Infrastructure.Diagnostics;

namespace Server.Tests.Synchronization;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class ProcessMemoryMapTests
{
  [Fact]
  public void ResidentPagesAreNotConfusedWithVirtualReservations()
  {
    using var input = new StringReader(
      """
      1000-5000 rw-p 00000000 00:00 0
      Rss: 2 kB
      Pss: 1 kB
      Anonymous: 2 kB
      Private_Dirty: 1 kB
      5000-9000 rw-p 00000000 00:00 0
      Rss: 0 kB
      Pss: 0 kB
      Anonymous: 0 kB
      Private_Dirty: 0 kB
      9000-a000 r-xp 00000000 00:01 42 /private/secret/libexample.so.1
      Rss: 1 kB
      Pss: 1 kB
      Anonymous: 0 kB
      Private_Dirty: 0 kB
      """
    );
    var groups = ProcessMemoryMapParser.Parse(input, true);
    var anon = Assert.Single(groups, x => x.Category == "anonymous");
    Assert.Equal(2, anon.Mappings);
    Assert.Equal(32768, anon.VirtualBytes);
    Assert.Equal(2048, anon.ResidentBytes);
    Assert.Equal(1024, anon.ProportionalBytes);
    Assert.DoesNotContain("secret", JsonSerializer.Serialize(groups));
    Assert.DoesNotContain("libexample", JsonSerializer.Serialize(groups));
  }

  [Fact]
  public void MapsFallbackDoesNotInventResidentZeroesAndHandlesHighAddresses()
  {
    using var input = new StringReader(
      """
      1000-2000 rw-p 00000000 00:00 0 [heap]
      ffffffffff600000-ffffffffff601000 --xp 00000000 00:00 0 [vsyscall]
      """
    );
    var groups = ProcessMemoryMapParser.Parse(input, false);
    Assert.Equal(2, groups.Count);
    Assert.All(
      groups,
      x =>
      {
        Assert.Equal(4096, x.VirtualBytes);
        Assert.Null(x.ResidentBytes);
        Assert.Null(x.ProportionalBytes);
      }
    );
  }

  [Fact]
  public void MissingCounterMakesTheGroupUnknownInsteadOfUndercounted()
  {
    using var input = new StringReader(
      """
      1000-2000 rw-p 00000000 00:00 0
      Rss: 1 kB
      2000-3000 rw-p 00000000 00:00 0
      """
    );
    Assert.Null(
      Assert.Single(ProcessMemoryMapParser.Parse(input, true)).ResidentBytes
    );
  }

  [Theory]
  [InlineData("Rss: -1 kB")]
  [InlineData("Rss: 4 MB")]
  [InlineData("Rss: 999999999999999999999 kB")]
  [InlineData("Rss: 1 kB\nRss: 1 kB")]
  public void InvalidCountersRejectTheSample(string counter)
  {
    using var input = new StringReader("1000-2000 rw-p 0 00:00 0\n" + counter);
    Assert.Throws<InvalidDataException>(
      () => ProcessMemoryMapParser.Parse(input, true)
    );
  }

  [Fact]
  public void StatusKeepsMissingAndZeroSeparate()
  {
    using var input = new StringReader(
      """
      Name: private-process
      VmRSS: 100 kB
      RssAnon: 80 kB
      RssFile: 20 kB
      VmSwap: 0 kB
      Threads: 12
      """
    );
    var status = ProcessMemoryMapParser.ParseStatus(input);
    Assert.Equal(102400, status.ResidentBytes);
    Assert.Equal(81920, status.AnonymousResidentBytes);
    Assert.Equal(0, status.SwapBytes);
    Assert.Null(status.SharedResidentBytes);
    Assert.Equal(12, status.Threads);
  }

  [Fact]
  public void CancellationStopsTheScan()
  {
    using var input = new StringReader("1000-2000 rw-p 0 00:00 0");
    Assert.Throws<OperationCanceledException>(
      () =>
        ProcessMemoryMapParser.Parse(input, true, new CancellationToken(true))
    );
  }

  [Theory]
  [InlineData("/memfd:doublemapper (deleted)", "runtime-double-mapped")]
  [InlineData("[stack:42]", "labelled-stack")]
  [InlineData("[anon:private]", "anonymous-executable")]
  [InlineData("/app/API.dll", "assembly-file")]
  [InlineData("/app/libnative.so (deleted)", "native-library-file")]
  [InlineData("/private/other file", "other-file")]
  public void MappingLabelsProduceOnlyFixedCategories(
    string name,
    string category
  )
  {
    using var input = new StringReader("1000-2000 r-xp 0 00:00 0 " + name);
    Assert.Equal(
      category,
      Assert.Single(ProcessMemoryMapParser.Parse(input, false)).Category
    );
  }

  [Fact]
  public void OversizedScanDoesNotReturnPartialTotals()
  {
    using var input = new StringReader(
      "1000-2000 rw-p 0 00:00 0\n" + new string(' ', 8 * 1024 * 1024)
    );
    Assert.Throws<InvalidDataException>(
      () => ProcessMemoryMapParser.Parse(input, true)
    );
  }
}
