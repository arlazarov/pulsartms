using System.Security.Cryptography;

namespace Application.Storage;

// The content of one upload as the provider reads it: exactly the declared
// length and the declared SHA-256. Both are checked on the read that would
// complete the declared length, before that read returns: the hash of the
// whole content, and one byte read ahead from the input, which must be its
// end. A provider that completes an upload only on its full length, and
// stops reading there, therefore never completes one whose content is
// longer, shorter or different.
public sealed class StorageReading(
  Stream inner,
  long length,
  string expectedSha256
) : Stream
{
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
      throw new StorageContentMismatchException("shorter than declared");
    hash.AppendData(buffer.Span[..count]);
    if (read + count == length)
    {
      if (await inner.ReadAsync(new byte[1], ct) != 0)
        throw new StorageContentMismatchException("longer than declared");
      if (
        !string.Equals(
          Convert.ToHexStringLower(hash.GetHashAndReset()),
          expectedSha256,
          StringComparison.Ordinal
        )
      )
        throw new StorageContentMismatchException("different from its hash");
    }
    read += count;
    return count;
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
      hash.Dispose();
    base.Dispose(disposing);
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
