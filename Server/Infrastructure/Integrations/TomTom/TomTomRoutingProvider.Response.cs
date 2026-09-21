using System.Buffers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.TomTom;

// A response is read through a stream that stops at a size limit, because
// the Content-Length header is a claim, not a fact.
public sealed partial class TomTomRoutingProvider
{
  private static RoutePlanningException ResponseTooLarge() =>
    new(
      "TomTom returned a route response that is too large to process safely. The saved plan has been kept.",
      DateTime.UtcNow.AddMinutes(5)
    );

  private sealed class BoundedResponseStream(Stream inner) : Stream
  {
    private int read;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
      Count(
        inner.Read(
          buffer,
          offset,
          Math.Min(count, MaximumResponseBytes - read + 1)
        )
      );

    public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer,
      CancellationToken ct = default
    ) =>
      Count(
        await inner.ReadAsync(
          buffer[..Math.Min(buffer.Length, MaximumResponseBytes - read + 1)],
          ct
        )
      );

    private int Count(int count)
    {
      read += count;
      if (read > MaximumResponseBytes)
        throw ResponseTooLarge();
      return count;
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        inner.Dispose();
      base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => inner.DisposeAsync();

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
      throw new NotSupportedException();

    public override void SetLength(long value) =>
      throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();
  }
}
