using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TomTomMemoryTests(ITestOutputHelper output)
{
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  [Fact]
  public async Task SequentialLargeRouteChecksDoNotRetainAuditPayloadsInTheSharedContext()
  {
    var body = RouteResponse(24000);
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new ByteArrayContent(body),
          }
        ),
      dailyRequestLimit: 11
    );
    var unrelated = new DispatchSettings
    {
      Id = Guid.NewGuid(),
      LoadNumberPrefix = "TEST",
    };
    fixture.Db.DispatchSettings.Add(unrelated);
    await fixture.Db.SaveChangesAsync();
    long legacyPayloadBytes = 0;
    for (var index = 0; index < 11; index++)
    {
      var result = await fixture.CalculateAsync(
        [new(40, -80), new(41, -80), new(42 + index * .001, -80)]
      );
      Assert.Equal(48001, result.Points.Count);
      Assert.Equal(2, result.Legs.Count);
      Assert.Equal(14400, result.Seconds);
      Assert.Single(result.Warnings);
      legacyPayloadBytes +=
        JsonSerializer.Serialize(result, Json).Length * sizeof(char);
      Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
      Assert.Equal(EntityState.Unchanged, fixture.Db.Entry(unrelated).State);
    }
    var saved = await fixture
      .Db.RoutingApiCalls.AsNoTracking()
      .Select(call => call.ResultJson!)
      .ToListAsync();
    Assert.Equal(11, fixture.Calls);
    Assert.Equal(11, saved.Count);
    var compactPayloadBytes = saved.Sum(json =>
      (long)json.Length * sizeof(char)
    );
    Assert.True(compactPayloadBytes * 1.8 < legacyPayloadBytes);
    Assert.All(
      saved,
      json =>
      {
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("points", out _));
        Assert.Equal(
          2,
          document.RootElement.GetProperty("legs").GetArrayLength()
        );
      }
    );
    output.WriteLine(
      $"11 checks / 48,001 points each: legacy serialized payloads={legacyPayloadBytes:N0} B; compact persisted payloads={compactPayloadBytes:N0} B; tracked RoutingApiCall payloads after each check=0 B. Serialized string sizes exclude object overhead and are not process RSS."
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CompactAndLegacyCachesReturnCompleteEquivalentGeometry(
    bool legacy
  )
  {
    string? legacyHash = null;
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (request, _) =>
      {
        legacyHash = LegacyRequestHash(request);
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new ByteArrayContent(RouteResponse(4)),
          }
        );
      }
    );
    var points = new RoutePoint[] { new(40, -80), new(41, -80), new(42, -80) };
    var fresh = await fixture.CalculateAsync(points);
    var compactHash = await fixture
      .Db.RoutingApiCalls.AsNoTracking()
      .Select(call => call.RequestHash)
      .SingleAsync();
    Assert.NotEqual(legacyHash, compactHash);
    if (legacy)
    {
      await using var observer = fixture.CreateObserver();
      var row = await observer.RoutingApiCalls.SingleAsync();
      row.RequestHash = legacyHash!;
      row.ResultJson = JsonSerializer.Serialize(fresh, Json);
      await observer.SaveChangesAsync();
    }
    var cached = await fixture.CalculateAsync(points);
    Assert.Equal(
      JsonSerializer.Serialize(fresh, Json),
      JsonSerializer.Serialize(cached, Json)
    );
    Assert.Equal(9, cached.Points.Count);
    Assert.Equal(2, cached.Legs.Count);
    Assert.Single(cached.Points, point => point.Latitude == 41);
    Assert.Equal(fresh.Warnings, cached.Warnings);
    Assert.Equal(1, fixture.Calls);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
  }

  [Fact]
  public async Task LegacyCooldownIsReusedWithoutAnotherReservationOrRequest()
  {
    string? legacyHash = null;
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (request, _) =>
      {
        legacyHash = LegacyRequestHash(request);
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        );
      }
    );
    var initial = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    await using (var observer = fixture.CreateObserver())
    {
      var row = await observer.RoutingApiCalls.SingleAsync();
      row.RequestHash = legacyHash!;
      await observer.SaveChangesAsync();
    }
    var cached = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    Assert.Equal(initial.Message, cached.Message);
    Assert.Equal(initial.RetryAfter, cached.RetryAfter);
    Assert.Equal(1, fixture.Calls);
    Assert.Equal(1, await fixture.Db.RoutingApiCalls.CountAsync());
  }

  [Theory]
  [InlineData("invalid-json")]
  [InlineData("null-legs")]
  [InlineData("invalid-point")]
  [InlineData("negative-miles")]
  [InlineData("too-many-flat-points")]
  [InlineData("too-many-leg-points")]
  [InlineData("oversized-text")]
  [InlineData("oversized-utf8")]
  public async Task UnsafeCachePayloadsFailBeforeReuseWithoutAnotherProviderCall(
    string scenario
  )
  {
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new ByteArrayContent(RouteResponse(4)),
          }
        )
    );
    var points = new RoutePoint[] { new(40, -80), new(41, -80), new(42, -80) };
    var route = await fixture.CalculateAsync(points);
    string replacement;
    switch (scenario)
    {
      case "invalid-json":
        replacement = "{invalid-cache";
        break;
      case "null-legs":
        replacement = "{\"legs\":null}";
        break;
      case "invalid-point":
        route.Legs[0].Points[0] = new(0, 0);
        replacement = JsonSerializer.Serialize(route, Json);
        break;
      case "negative-miles":
        route.Miles = -1;
        replacement = JsonSerializer.Serialize(route, Json);
        break;
      case "too-many-flat-points":
        route.Points = Enumerable
          .Repeat(new RoutePoint(40, -80), 200001)
          .ToList();
        replacement = JsonSerializer.Serialize(route, Json);
        break;
      case "too-many-leg-points":
        route.Legs =
        [
          new(
            200,
            14400,
            Enumerable.Repeat(new RoutePoint(40, -80), 200001).ToList()
          ),
        ];
        replacement = JsonSerializer.Serialize(route, Json);
        break;
      case "oversized-text":
        replacement = new string(' ', 32 * 1024 * 1024 + 1);
        break;
      case "oversized-utf8":
        replacement = new string('é', 16 * 1024 * 1024 + 1);
        break;
      default:
        throw new InvalidOperationException();
    }
    await using (var observer = fixture.CreateObserver())
    {
      var row = await observer.RoutingApiCalls.SingleAsync();
      row.ResultJson = replacement;
      await observer.SaveChangesAsync();
    }
    var failure = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync(points)
    );
    Assert.Contains("safe geometry limits", failure.Message);
    Assert.DoesNotContain(scenario, failure.Message);
    Assert.Equal(1, fixture.Calls);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    Assert.Equal(1, await fixture.Db.RoutingApiCalls.CountAsync());
  }

  [Fact]
  public async Task SuccessfulResponsesUseTheStreamWithoutBufferingAndDisposeIt()
  {
    using var content = new StreamOnlyContent(RouteResponse(4));
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK) { Content = content }
        )
    );
    var result = await fixture.CalculateAsync(
      [new(40, -80), new(41, -80), new(42, -80)]
    );
    Assert.Equal(9, result.Points.Count);
    Assert.Equal(0, content.BufferRequests);
    Assert.Equal(1, content.StreamRequests);
    Assert.True(content.StreamDisposed);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
  }

  [Theory]
  [InlineData(400)]
  [InlineData(429)]
  [InlineData(503)]
  public async Task FailedResponsesDoNotReadTheBodyOrRetainTrackedReservations(
    int status
  )
  {
    using var content = new StreamOnlyContent(RouteResponse(4));
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage((HttpStatusCode)status) { Content = content }
        )
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    Assert.Equal(0, content.BufferRequests);
    Assert.Equal(0, content.StreamRequests);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    var saved = await fixture.Db.RoutingApiCalls.AsNoTracking().SingleAsync();
    Assert.Null(saved.ResultJson);
    Assert.NotNull(saved.ErrorMessage);
    Assert.True(saved.ExpiresAt > DateTime.UtcNow);
    if (status == 400)
      Assert.Equal(DateTime.MaxValue, saved.ExpiresAt);
  }

  [Fact]
  public async Task OversizedDeclaredResponseIsRejectedBeforeOpeningTheBody()
  {
    using var content = new StreamOnlyContent(RouteResponse(4));
    content.Headers.ContentLength = 16 * 1024 * 1024 + 1;
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK) { Content = content }
        )
    );
    var failure = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    Assert.Contains("too large", failure.Message);
    Assert.Equal(0, content.BufferRequests);
    Assert.Equal(0, content.StreamRequests);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    Assert.Equal(1, await fixture.Db.RoutingApiCalls.CountAsync());
  }

  [Fact]
  public async Task UnknownLengthOversizedBodyStopsAtTheByteLimitAndKeepsCooldown()
  {
    using var stream = new WhitespaceStream();
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new UnbufferedContent(stream),
          }
        )
    );
    var failure = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    Assert.Contains("too large", failure.Message);
    Assert.Equal(16 * 1024 * 1024 + 1, stream.BytesRead);
    Assert.True(stream.Disposed);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    var saved = await fixture.Db.RoutingApiCalls.AsNoTracking().SingleAsync();
    Assert.Equal(failure.Message, saved.ErrorMessage);
    Assert.Null(saved.ResultJson);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync()
    );
    Assert.Equal(1, fixture.Calls);
  }

  [Fact]
  public async Task TooManyLegPointsFailBeforeCoordinatesAreMaterialized()
  {
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new ByteArrayContent(RouteResponse(100000)),
          }
        )
    );
    var failure = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync([new(40, -80), new(41, -80), new(42, -80)])
    );
    Assert.Contains("more route geometry", failure.Message);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    var saved = await fixture.Db.RoutingApiCalls.AsNoTracking().SingleAsync();
    Assert.Null(saved.ResultJson);
    Assert.Equal(failure.Message, saved.ErrorMessage);
  }

  [Fact]
  public async Task HttpTimeoutStillBoundsTheBodyAfterHeadersArrive()
  {
    using var stream = new WaitingStream();
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new UnbufferedContent(stream),
          }
        ),
      timeout: TimeSpan.FromSeconds(1)
    );
    var failure = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync().WaitAsync(TimeSpan.FromSeconds(5))
    );
    Assert.True(stream.ReadStarted);
    Assert.True(stream.Disposed);
    Assert.True(failure.RetryAfter > DateTime.UtcNow.AddMinutes(3));
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    Assert.Equal(1, await fixture.Db.RoutingApiCalls.CountAsync());
  }

  [Fact]
  public async Task CallerCancellationDuringBodyReadKeepsCancellationAndDisposesTheStream()
  {
    using var cancellation = new CancellationTokenSource();
    using var stream = new WaitingStream();
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new UnbufferedContent(stream),
          }
        )
    );
    var calculation = fixture.CalculateAsync(ct: cancellation.Token);
    await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => calculation);
    Assert.True(stream.Disposed);
    Assert.Empty(fixture.Db.ChangeTracker.Entries<RoutingApiCall>());
    var saved = await fixture.Db.RoutingApiCalls.AsNoTracking().SingleAsync();
    Assert.Contains("cancelled", saved.ErrorMessage);
    Assert.Null(saved.ResultJson);
    Assert.Equal(1, fixture.Calls);
  }

  private static string LegacyRequestHash(HttpRequestMessage request)
  {
    var query = request.RequestUri!.PathAndQuery.TrimStart('/');
    query = query[..query.IndexOf("&key=", StringComparison.Ordinal)];
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));
  }

  private static byte[] RouteResponse(int segmentsPerLeg) =>
    JsonSerializer.SerializeToUtf8Bytes(
      new
      {
        routes = new[]
        {
          new
          {
            summary = new
            {
              lengthInMeters = 200 * 1609.344,
              travelTimeInSeconds = 14400,
            },
            legs = Enumerable
              .Range(0, 2)
              .Select(leg => new
              {
                summary = new
                {
                  lengthInMeters = 100 * 1609.344,
                  travelTimeInSeconds = 7200,
                },
                points = Enumerable
                  .Range(0, segmentsPerLeg + 1)
                  .Select(point => new
                  {
                    latitude = 40 + leg + point / (double)segmentsPerLeg,
                    longitude = -80,
                  }),
              }),
            sections = new[]
            {
              new
              {
                sectionType = "TRUCK",
                startPointIndex = 0,
                endPointIndex = 1,
              },
            },
          },
        },
      },
      Json
    );

  private sealed class StreamOnlyContent(byte[] bytes) : HttpContent
  {
    private readonly ObservedStream stream = new(bytes);
    public int BufferRequests { get; private set; }
    public int StreamRequests { get; private set; }
    public bool StreamDisposed => stream.Disposed;

    protected override bool TryComputeLength(out long length)
    {
      length = bytes.Length;
      return true;
    }

    protected override Task SerializeToStreamAsync(
      Stream target,
      TransportContext? context
    )
    {
      BufferRequests++;
      throw new InvalidOperationException(
        "The routing response must not be buffered by HttpClient."
      );
    }

    protected override Task<Stream> CreateContentReadStreamAsync() =>
      OpenStream();

    protected override Task<Stream> CreateContentReadStreamAsync(
      CancellationToken ct
    ) => OpenStream();

    private Task<Stream> OpenStream()
    {
      StreamRequests++;
      return Task.FromResult<Stream>(stream);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        stream.Dispose();
      base.Dispose(disposing);
    }
  }

  private sealed class ObservedStream(byte[] bytes)
    : MemoryStream(bytes, writable: false)
  {
    public bool Disposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        Disposed = true;
      base.Dispose(disposing);
    }
  }

  private sealed class UnbufferedContent(Stream stream) : HttpContent
  {
    protected override bool TryComputeLength(out long length)
    {
      length = 0;
      return false;
    }

    protected override Task SerializeToStreamAsync(
      Stream target,
      TransportContext? context
    ) =>
      throw new InvalidOperationException(
        "The routing response must not be buffered by HttpClient."
      );

    protected override Task<Stream> CreateContentReadStreamAsync() =>
      Task.FromResult(stream);

    protected override Task<Stream> CreateContentReadStreamAsync(
      CancellationToken ct
    ) => Task.FromResult(stream);

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        stream.Dispose();
      base.Dispose(disposing);
    }
  }

  private class WhitespaceStream : Stream
  {
    public int BytesRead { get; private set; }
    public bool Disposed { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      buffer.AsSpan(offset, count).Fill((byte)' ');
      BytesRead += count;
      return count;
    }

    public override ValueTask<int> ReadAsync(
      Memory<byte> buffer,
      CancellationToken ct = default
    )
    {
      ct.ThrowIfCancellationRequested();
      buffer.Span.Fill((byte)' ');
      BytesRead += buffer.Length;
      return ValueTask.FromResult(buffer.Length);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        Disposed = true;
      base.Dispose(disposing);
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
      throw new NotSupportedException();

    public override void SetLength(long value) =>
      throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();
  }

  private sealed class WaitingStream : WhitespaceStream
  {
    public TaskCompletionSource Started { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool ReadStarted => Started.Task.IsCompleted;

    public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer,
      CancellationToken ct = default
    )
    {
      Started.TrySetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, ct);
      return 0;
    }
  }
}
