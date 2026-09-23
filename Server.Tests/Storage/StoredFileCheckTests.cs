using System.Text;
using Application.Storage;
using Domain.Entities.Storage;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Storage;

// A quarantined file is released only when its first bytes are the kind it
// was declared as, and that kind is one PulsR shows and sends; anything
// else is refused and never served.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class StoredFileCheckTests
{
  [Theory]
  [InlineData("application/pdf", "%PDF-1.7", true)]
  [InlineData("image/jpeg", "\xFF\xD8\xFF\xE0", true)]
  [InlineData("image/jpg", "\xFF\xD8\xFF\xE0", true)]
  [InlineData("image/png", "\x89PNG\r\n\x1A\n", true)]
  [InlineData("image/webp", "RIFF\0\0\0\0WEBPVP8 ", true)]
  [InlineData("audio/ogg; codecs=opus", "OggS\0", true)]
  [InlineData("video/mp4", "\0\0\0\x18" + "ftypmp42", true)]
  [InlineData("audio/mp4", "\0\0\0\x18" + "ftypM4A ", true)]
  [InlineData("audio/mpeg", "ID3\x04", true)]
  [InlineData("audio/aac", "\xFF\xF1\x50", true)]
  [InlineData("application/pdf", "\xFF\xD8\xFF\xE0", false)]
  [InlineData("image/png", "%PDF-1.7", false)]
  [InlineData("text/html", "<html>", false)]
  [InlineData("application/zip", "PK\x03\x04", false)]
  [InlineData("application/pdf", "", false)]
  public void OnlyAKindThatMatchesItsDeclarationIsAccepted(
    string declared,
    string head,
    bool accepted
  ) => Assert.Equal(accepted, StoredFileCheck.Matches(declared, Latin1(head)));

  [Fact]
  public async Task ARefusedFileIsNeverServedAndAnAcceptedOneIs()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var store = FileStorageTests.Store(f.Db);
    var check = new StoredFileCheck(store);
    var pdf = await Put(store, "application/pdf", "%PDF-1.7 ok");
    var disguised = await Put(store, "application/pdf", "<html>not a pdf");

    Assert.Equal(
      StoredFileStates.Available,
      await check.CheckAsync(pdf, default)
    );
    Assert.Equal(
      StoredFileStates.Rejected,
      await check.CheckAsync(disguised, default)
    );

    Assert.NotNull(await store.OpenAsync(pdf, quarantined: false, default));
    Assert.Null(await store.OpenAsync(disguised, quarantined: false, default));
    Assert.Null(await store.OpenAsync(disguised, quarantined: true, default));
    Assert.Equal(
      StoredFileStates.Rejected,
      await check.CheckAsync(disguised, default)
    );
  }

  private static async Task<Guid> Put(FileStore store, string type, string text)
  {
    var bytes = Encoding.UTF8.GetBytes(text);
    return (
      await store.PutAsync(
        new(
          Guid.NewGuid(),
          "file",
          type,
          bytes.Length,
          FileStorageTests.Sha(bytes)
        ),
        new MemoryStream(bytes),
        default
      )
    ).Id;
  }

  private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);
}
