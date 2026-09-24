using System.Security.Cryptography;

namespace Application.Storage;

// The content of one stored file as it is read, on the way in (the provider
// reads an upload) or out (a reader opens a stored file): exactly the
// declared length and the declared SHA-256. Nothing past the declared
// length is ever read, so a reader can size its buffer from it. Both are
// checked on the read that would complete the declared length, before that
// read returns: the hash of the
// whole content, and one byte read ahead from the input, which must be its
// end. A provider that completes an upload only on its full length, and
// stops reading there, therefore never completes one whose content is
// longer, shorter or different.
//
// Mismatched, when given, is told once before a mismatch is thrown, so the
// file's owner can record it.
public sealed class StorageReading(
  Stream inner,
  long length,
  string expectedSha256,
  bool ownsInner = false,
  Func<Task>? mismatched = null
) : Stream
{
  private bool told;
  private readonly IncrementalHash hash = IncrementalHash.CreateHash(
    HashAlgorithmName.SHA256
  );
  private long read;

  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => false;
  public override long Length => length;
  public override long Position
  {
    get => read;
    set => throw new NotSupportedException();
  }

  public override int Read(byte[] buffer, int offset, int count) =>
    ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

  public override async ValueTask<int> ReadAsync(
    Memory<byte> buffer,
    CancellationToken ct = default
  )
  {
    // An empty read asks for nothing and consumes nothing.
    if (buffer.Length == 0 || read == length)
      return 0;
    var room = (int)Math.Min(buffer.Length, length - read);
    var count = await inner.ReadAsync(buffer[..room], ct);
    if (count == 0)
      await MismatchAsync("shorter than declared");
    hash.AppendData(buffer.Span[..count]);
    if (read + count == length)
    {
      if (await inner.ReadAsync(new byte[1], ct) != 0)
        await MismatchAsync("longer than declared");
      if (
        !string.Equals(
          Convert.ToHexStringLower(hash.GetHashAndReset()),
          expectedSha256,
          StringComparison.Ordinal
        )
      )
        await MismatchAsync("different from its hash");
    }
    read += count;
    return count;
  }

  private async Task MismatchAsync(string how)
  {
    if (!told && mismatched is not null)
    {
      told = true;
      await mismatched();
    }
    throw new StorageContentMismatchException(how);
  }

  public override void Flush() { }

  public override long Seek(long offset, SeekOrigin origin) =>
    throw new NotSupportedException();

  public override void SetLength(long value) =>
    throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count) =>
    throw new NotSupportedException();

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      hash.Dispose();
      if (ownsInner)
        inner.Dispose();
    }
    base.Dispose(disposing);
  }

  public override async ValueTask DisposeAsync()
  {
    hash.Dispose();
    if (ownsInner)
      await inner.DisposeAsync();
    await base.DisposeAsync();
  }
}

// The content does not match the upload's fingerprint.
public sealed class StorageContentMismatchException(string how)
  : Exception($"The content is {how}.");

// The same file id was recorded with another fingerprint.
public sealed class StorageConflictException()
  : Exception("This file id belongs to different content.");

// Another attempt holds this upload; retry after its lease.
public sealed class StorageBusyException()
  : Exception("This file is being uploaded by another attempt.");
