using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Client.Services;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;

namespace Client.Tests.Routing;

// The browser's messaging signals: the stream's lines, the channel scoped
// to the signed-in account, the local fallback when the channel cannot be
// loaded, and a fallback wait that a slow tick cannot break.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class MessagingSignalsTests
{
  private const string Channel = "./js/generated/shared/messagingChannel.js";
  private static readonly Guid Ann = Guid.NewGuid();
  private static readonly Guid Session = Guid.NewGuid();

  [Fact]
  public void OnlyADataLineNamingAConversationIsAChange()
  {
    var id = Guid.NewGuid();

    Assert.Equal(
      id.ToString(),
      MessagingSignals.Change(
        $"data: {{\"conversationId\":\"{id}\",\"revision\":4}}"
      )
    );
    Assert.Null(MessagingSignals.Change(": keep-alive"));
    Assert.Null(MessagingSignals.Change("event: change"));
    Assert.Null(MessagingSignals.Change("data: not json"));
    Assert.Null(MessagingSignals.Change("data: {\"revision\":4}"));
    Assert.Null(
      MessagingSignals.Change($"data: {{\"conversationId\":\"{Guid.Empty}\"}}")
    );
    Assert.Null(MessagingSignals.Change("data: {\"conversationId\":7}"));
    Assert.Null(MessagingSignals.Change("data: [1]"));
  }

  [Fact]
  public async Task ATabJoinsUnderItsAccountAndRejoinsWhenTheAccountChanges()
  {
    await using var f = new Fixture();
    var channel = f.Context.JSInterop.SetupModule(Channel);
    var joins = channel.SetupVoid("join", _ => true);
    joins.SetVoidResult();
    channel.SetupVoid("leave").SetVoidResult();

    await f.Signals.JoinAsync();
    f.Auth.SetClaims(
      new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
    );
    await Eventually(() => Assert.Equal(2, joins.Invocations.Count));
    f.Auth.SetNotAuthorized();

    var scopes = joins
      .Invocations.Select(x => (string)x.Arguments[1]!)
      .ToList();
    Assert.Equal($"{Ann:N}:{Session:N}", scopes[0]);
    Assert.NotEqual(scopes[0], scopes[1]);
    await Eventually(() => Assert.Equal(2, channel.Invocations["leave"].Count));
    Assert.Equal(2, joins.Invocations.Count);
  }

  [Theory]
  [InlineData("import")]
  [InlineData("join")]
  public async Task WithoutTheChannelThisTabReadsItsOwnStreamAndPolls(
    string failing
  )
  {
    await using var f = new Fixture();
    var channel = f.Context.JSInterop.SetupModule(Channel);
    channel
      .SetupVoid("join", _ => true)
      .SetException(new JSException("The channel could not start."));
    var signals =
      failing == "import"
        ? new MessagingSignals(
          f.Context.Services.GetRequiredService<ApiService>(),
          new FailingImport(f.Context.JSInterop.JSRuntime),
          f.Context.Services.GetRequiredService<AuthenticationStateProvider>(),
          f.Context.Services.GetRequiredService<TokenStorageService>(),
          f.Time
        )
        : f.Signals;
    var seen = new List<string>();
    signals.Changed += x => seen.Add(x.Kind);

    await signals.JoinAsync();

    await Eventually(() => Assert.Contains("poll", seen));
    Assert.Equal(1, f.Streams);
    Assert.Empty(channel.Invocations["post"]);
    f.Time.Advance(TimeSpan.FromSeconds(2));
    await Eventually(() => Assert.Equal(2, f.Streams));
    await signals.LeaveAsync();
  }

  // A tick that returns after the whole wait has passed must not stop the
  // reader: it reconnects instead of waiting for a negative time.
  [Fact]
  public async Task ASlowTickDoesNotStopTheReader()
  {
    await using var f = new Fixture();
    var channel = f.Context.JSInterop.SetupModule(Channel);
    channel.SetupVoid("join", _ => true).SetVoidResult();
    channel.SetupVoid("leave").SetVoidResult();
    var posts = channel.SetupVoid("post", _ => true);
    await f.Signals.JoinAsync();

    await f.Signals.Lead();
    await Eventually(() => Assert.Single(posts.Invocations));
    f.Time.Advance(TimeSpan.FromSeconds(10));
    posts.SetVoidResult();

    await Eventually(() => Assert.Equal(2, f.Streams));
    await f.Signals.LeaveAsync();
  }

  // A reader stopped by a sign-in change is not awaited; whatever it reads
  // afterwards must not reach the channel the next join opened.
  [Fact]
  public async Task AStoppedReaderPostsNothingIntoTheNextJoin()
  {
    await using var f = new Fixture();
    var held = new TaskCompletionSource<HttpResponseMessage>();
    f.Stream = held.Task;
    var channel = f.Context.JSInterop.SetupModule(Channel);
    var joins = channel.SetupVoid("join", _ => true);
    joins.SetVoidResult();
    channel.SetupVoid("leave").SetVoidResult();
    var posts = channel.SetupVoid("post", _ => true);
    posts.SetVoidResult();
    await f.Signals.JoinAsync();
    await f.Signals.Lead();
    await Eventually(() => Assert.Equal(1, f.Streams));

    f.Auth.SetClaims(
      new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
    );
    await Eventually(() => Assert.Equal(2, joins.Invocations.Count));
    held.SetResult(
      new(HttpStatusCode.OK)
      {
        Content = new StringContent(
          $"data: {{\"conversationId\":\"{Guid.NewGuid()}\"}}\n\n"
        ),
      }
    );
    await Task.Delay(50);

    Assert.Empty(posts.Invocations);
    await f.Signals.LeaveAsync();
  }

  // A proxy in front of the API holds the streamed response back. Without
  // headers in time, or without a line in time, the stream counts as down:
  // this tab polls and reconnects rather than waiting on it.
  [Theory]
  [InlineData("headers")]
  [InlineData("body")]
  public async Task AStreamHeldBackByAProxyCountsAsDown(string buffered)
  {
    await using var f = new Fixture();
    f.Buffered = buffered;
    var channel = f.Context.JSInterop.SetupModule(Channel);
    channel
      .SetupVoid("join", _ => true)
      .SetException(new JSException("No channel in this test."));
    var seen = new List<string>();
    f.Signals.Changed += x => seen.Add(x.Kind);
    await f.Signals.JoinAsync();
    await Eventually(() => Assert.Equal(1, f.Streams));
    Assert.DoesNotContain("poll", seen);

    f.Time.Advance(
      buffered == "headers"
        ? MessagingSignals.HeaderTimeout
        : MessagingSignals.IdleTimeout
    );

    await Eventually(() => Assert.Contains("poll", seen));
    f.Time.Advance(TimeSpan.FromSeconds(2));
    await Eventually(() => Assert.Equal(2, f.Streams));
    await f.Signals.LeaveAsync();
  }

  private static async Task Eventually(Action assertion)
  {
    for (var attempt = 0; ; attempt++)
      try
      {
        assertion();
        return;
      }
      catch (Exception) when (attempt < 200)
      {
        await Task.Delay(10);
      }
  }

  private sealed class FailingImport(IJSRuntime inner) : IJSRuntime
  {
    public ValueTask<TValue> InvokeAsync<TValue>(
      string identifier,
      object?[]? args
    ) => InvokeAsync<TValue>(identifier, default, args);

    public ValueTask<TValue> InvokeAsync<TValue>(
      string identifier,
      CancellationToken cancellationToken,
      object?[]? args
    ) =>
      identifier == "import" && args?[0] as string == Channel
        ? throw new JSException("The module could not be loaded.")
        : inner.InvokeAsync<TValue>(identifier, cancellationToken, args);
  }

  private sealed class Fixture : IAsyncDisposable
  {
    public ClientComponentContext Context { get; }
    public BunitAuthorizationContext Auth { get; }
    public FakeTimeProvider Time { get; } = new();
    public int Streams;
    public Task<HttpResponseMessage>? Stream;

    // A proxy that holds the stream back: headers that never come, or a
    // body that never says anything. Both honour the reader's cancellation.
    public string? Buffered;

    public Fixture()
    {
      Context = new ClientComponentContext(SendAsync);
      Context.JSInterop.Mode = JSRuntimeMode.Strict;
      Auth = Context.AddAuthorization();
      Auth.SetAuthorized("Ann");
      Auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, Ann.ToString()));
      Context
        .JSInterop.SetupModule("./js/generated/shared/authStorage.js")
        .Setup<string?>("readSession")
        .SetResult(
          JsonSerializer.Serialize(
            new
            {
              Id = Session,
              AccessToken = "access",
              RefreshToken = "refresh",
            }
          )
        );
      Context.Services.AddSingleton<TokenStorageService>();
      Context.Services.AddSingleton<TimeProvider>(Time);
      Context.Services.AddSingleton<MessagingSignals>();
    }

    public MessagingSignals Signals =>
      Context.Services.GetRequiredService<MessagingSignals>();

    private Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      if (request.RequestUri!.AbsolutePath == "/api/messaging/events")
      {
        Interlocked.Increment(ref Streams);
        // Held until the test answers; like a slow network, it does not
        // notice the reader's cancellation.
        if (Stream is { } held)
          return held;
        if (Buffered == "headers")
          return Hang(ct);
        if (Buffered == "body")
          return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = new StreamContent(new SilentStream()),
            }
          );
      }
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
      );
    }

    private static async Task<HttpResponseMessage> Hang(CancellationToken ct)
    {
      await Task.Delay(Timeout.Infinite, ct);
      throw new InvalidOperationException("Unreachable");
    }

    public ValueTask DisposeAsync() => Context.DisposeAsync();
  }

  private sealed class SilentStream : Stream
  {
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer,
      CancellationToken ct = default
    )
    {
      await Task.Delay(Timeout.Infinite, ct);
      return 0;
    }

    public override Task<int> ReadAsync(
      byte[] buffer,
      int offset,
      int count,
      CancellationToken ct
    ) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) =>
      throw new NotSupportedException();

    public override void SetLength(long value) =>
      throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
      throw new NotSupportedException();
  }
}
