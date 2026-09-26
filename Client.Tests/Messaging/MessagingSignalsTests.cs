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

namespace Client.Tests.Messaging;

// The browser's messaging signals: the server's answers, the channel scoped
// to the signed-in account, the local fallback when the channel cannot be
// loaded, and a fallback wait that a slow tick cannot break. Firebase
// Hosting held the old server-sent stream back, so an inbound message
// showed only on the next 30-second poll; the reader now asks with
// requests that end, and a change reaches the views as soon as one answers.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class MessagingSignalsTests
{
  private const string Channel = "./js/generated/shared/messagingChannel.js";
  private static readonly Guid Ann = Guid.NewGuid();
  private static readonly Guid Session = Guid.NewGuid();

  // The owner's case: a driver's message commits while the reader waits.
  // The view hears of it when the answer comes, not on a poll tick: the
  // clock does not move at all.
  [Fact]
  public async Task AChangeReachesTheViewsWithoutWaitingForAPoll()
  {
    await using var f = new Fixture();
    var server = f.Server();
    var seen = f.LocalOnly();
    await f.Signals.JoinAsync();
    await Eventually(() => Assert.Contains("resync", Kinds(seen)));
    var waiting = await server.NextAsync();
    Assert.Equal(server.Mailbox, waiting.Mailbox);

    var chat = Guid.NewGuid();
    waiting.Answer(chat);

    await Eventually(
      () => Assert.Contains(("change", (Guid?)chat), Seen(seen))
    );
    Assert.DoesNotContain("poll", Kinds(seen));
    await f.Signals.LeaveAsync();
  }

  // Resync when the server says so, or when it answers with a mailbox other
  // than the one asked with; and a repair tick once a minute of answers.
  [Fact]
  public async Task AnswersResyncWhenToldAndRepairEachMinute()
  {
    await using var f = new Fixture();
    var server = f.Server();
    var seen = f.LocalOnly();
    await f.Signals.JoinAsync();
    await Eventually(() => Assert.Single(Kinds(seen), "resync"));

    (await server.NextAsync()).Answer(resync: true);
    await Eventually(
      () => Assert.Equal(2, Kinds(seen).Count(x => x == "resync"))
    );
    var replaced = await server.NextAsync();
    replaced.Answer(mailbox: Guid.NewGuid());
    await Eventually(
      () => Assert.Equal(3, Kinds(seen).Count(x => x == "resync"))
    );
    // The next request names the mailbox the server answered with.
    var next = await server.NextAsync();
    Assert.Equal(replaced.AnsweredWith, next.Mailbox);

    // Time moves only while a request is waiting, as on the server.
    f.Time.Advance(TimeSpan.FromSeconds(20));
    next.Answer();
    var later = await server.NextAsync();
    f.Time.Advance(TimeSpan.FromSeconds(20));
    later.Answer();
    var last = await server.NextAsync();
    Assert.DoesNotContain("poll", Kinds(seen));
    f.Time.Advance(TimeSpan.FromSeconds(21));
    last.Answer();
    await Eventually(() => Assert.Contains("poll", Kinds(seen)));
    await f.Signals.LeaveAsync();
  }

  // A mailbox lost on every request - each reaching another instance -
  // must not become a loop of full reads: the second replacement in a row
  // waits out the backoff before asking again.
  [Fact]
  public async Task AMailboxReplacedAgainAndAgainBacksOff()
  {
    await using var f = new Fixture();
    var server = f.Server();
    var seen = f.LocalOnly();
    await f.Signals.JoinAsync();
    (await server.NextAsync()).Answer(mailbox: Guid.NewGuid());
    (await server.NextAsync()).Answer(mailbox: Guid.NewGuid());

    await Eventually(() => Assert.Contains("poll", Kinds(seen)));
    await Task.Delay(50);
    Assert.Equal(3, server.Requests);
    f.Time.Advance(TimeSpan.FromSeconds(2));
    // Replaced a third time: the wait doubles rather than starting over.
    (await server.NextAsync()).Answer(mailbox: Guid.NewGuid());
    await Eventually(
      () => Assert.Equal(2, Kinds(seen).Count(x => x == "poll"))
    );
    // The wait starts just after the tick; the clock moves once it has.
    await Task.Delay(50);
    f.Time.Advance(TimeSpan.FromSeconds(2));
    await Task.Delay(50);
    Assert.Equal(4, server.Requests);
    f.Time.Advance(TimeSpan.FromSeconds(2));
    await server.NextAsync();
    Assert.Equal(5, server.Requests);
    await f.Signals.LeaveAsync();
  }

  // Idle for five minutes: about three requests a minute that each return
  // after the server's wait, one repair tick a minute, and nothing else.
  // The stream this replaced left each view polling every 30 seconds.
  [Fact]
  public async Task AnIdleBrowserAsksThreeTimesAMinuteAndRepairsOnce()
  {
    await using var f = new Fixture();
    var server = f.Server(answerAfter: TimeSpan.FromSeconds(20));
    var seen = f.LocalOnly();
    await f.Signals.JoinAsync();
    await Eventually(() => Assert.Equal(2, server.Requests));

    for (var second = 0; second < 300; second++)
    {
      f.Time.Advance(TimeSpan.FromSeconds(1));
      await Task.Delay(2);
    }
    await Task.Delay(50);

    Assert.InRange(server.Requests, 15, 17);
    Assert.InRange(Kinds(seen).Count(x => x == "poll"), 4, 5);
    Assert.Single(Kinds(seen), "resync");
    await f.Signals.LeaveAsync();
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
  public async Task WithoutTheChannelThisTabAsksForItselfAndPolls(
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
    Assert.Equal(1, f.Requests);
    Assert.Empty(channel.Invocations["post"]);
    f.Time.Advance(TimeSpan.FromSeconds(2));
    await Eventually(() => Assert.Equal(2, f.Requests));
    await signals.LeaveAsync();
  }

  // A tick that returns after the whole wait has passed must not stop the
  // reader: it asks again instead of waiting for a negative time.
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

    await Eventually(() => Assert.Equal(2, f.Requests));
    await f.Signals.LeaveAsync();
  }

  // A reader stopped by a sign-in change is not awaited; whatever its last
  // request answers afterwards must not reach the channel the next join
  // opened.
  [Fact]
  public async Task AStoppedReaderPostsNothingIntoTheNextJoin()
  {
    await using var f = new Fixture();
    var held = new TaskCompletionSource<HttpResponseMessage>();
    f.Held = held.Task;
    var channel = f.Context.JSInterop.SetupModule(Channel);
    var joins = channel.SetupVoid("join", _ => true);
    joins.SetVoidResult();
    channel.SetupVoid("leave").SetVoidResult();
    var posts = channel.SetupVoid("post", _ => true);
    posts.SetVoidResult();
    await f.Signals.JoinAsync();
    await f.Signals.Lead();
    await Eventually(() => Assert.Equal(1, f.Requests));

    f.Auth.SetClaims(
      new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
    );
    await Eventually(() => Assert.Equal(2, joins.Invocations.Count));
    held.SetResult(Fixture.Json(Guid.NewGuid(), true, [Guid.NewGuid()]));
    await Task.Delay(50);

    Assert.Empty(posts.Invocations);
    await f.Signals.LeaveAsync();
  }

  // An answer that never comes - a network or proxy that holds it - counts
  // as the connection being down once the server's wait and a margin have
  // passed: this tab polls and asks again.
  [Fact]
  public async Task AnAnswerThatDoesNotComeCountsAsDown()
  {
    await using var f = new Fixture();
    f.Silent = true;
    var seen = f.LocalOnly();
    await f.Signals.JoinAsync();
    await Eventually(() => Assert.Equal(1, f.Requests));
    Assert.DoesNotContain("poll", Kinds(seen));

    f.Time.Advance(MessagingSignals.AnswerTimeout);

    await Eventually(() => Assert.Contains("poll", Kinds(seen)));
    f.Time.Advance(TimeSpan.FromSeconds(2));
    await Eventually(() => Assert.Equal(2, f.Requests));
    await f.Signals.LeaveAsync();
  }

  private static List<(string Kind, Guid? Id)> Seen(List<(string, Guid?)> seen)
  {
    lock (seen)
      return [.. seen];
  }

  private static List<string> Kinds(List<(string, Guid?)> seen) =>
    [.. Seen(seen).Select(x => x.Kind)];

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
    public int Requests;

    // Held until the test answers; like a slow network, it does not notice
    // the reader's cancellation.
    public Task<HttpResponseMessage>? Held;

    // An answer that never comes; it honours the reader's cancellation.
    public bool Silent;

    private FakeServer? _server;

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

    public FakeServer Server(TimeSpan? answerAfter = null) =>
      _server = new FakeServer(Time, answerAfter);

    // No channel: this tab reads for itself and every signal comes here.
    public List<(string, Guid?)> LocalOnly()
    {
      Context
        .JSInterop.SetupModule(Channel)
        .SetupVoid("join", _ => true)
        .SetException(new JSException("No channel in this test."));
      var seen = new List<(string, Guid?)>();
      Signals.Changed += x =>
      {
        lock (seen)
          seen.Add((x.Kind, x.ConversationId));
      };
      return seen;
    }

    private Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      if (request.RequestUri!.AbsolutePath == "/api/messaging/changes")
      {
        Interlocked.Increment(ref Requests);
        if (Held is { } held)
          return held;
        if (Silent)
          return Hang(ct);
        if (_server is { } server)
          return server.Receive(request.RequestUri, ct);
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

    public static HttpResponseMessage Json(
      Guid mailbox,
      bool resync,
      IReadOnlyList<Guid> conversations
    ) =>
      new(HttpStatusCode.OK)
      {
        Content = new StringContent(
          JsonSerializer.Serialize(
            new
            {
              success = true,
              response = new
              {
                mailbox,
                resync,
                conversations,
              },
              errors = Array.Empty<string>(),
            }
          ),
          System.Text.Encoding.UTF8,
          "application/json"
        ),
      };

    public ValueTask DisposeAsync() => Context.DisposeAsync();
  }

  // The server's side of the conversation: the first request (no mailbox)
  // opens one and is told to resync; every other waits for the test, or
  // answers empty after answerAfter on the test's clock.
  private sealed class FakeServer(TimeProvider time, TimeSpan? answerAfter)
  {
    public Guid Mailbox { get; } = Guid.NewGuid();
    public int Requests;
    private readonly System.Threading.Channels.Channel<Pending> _pending =
      System.Threading.Channels.Channel.CreateUnbounded<Pending>();

    public async Task<HttpResponseMessage> Receive(
      Uri uri,
      CancellationToken ct
    )
    {
      Interlocked.Increment(ref Requests);
      var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
      Guid? asked = Guid.TryParse(query["mailbox"], out var id) ? id : null;
      if (asked is null)
        return Fixture.Json(Mailbox, true, []);
      if (answerAfter is { } after)
      {
        await Task.Delay(after, time, ct);
        return Fixture.Json(asked.Value, false, []);
      }
      var pending = new Pending(asked);
      _pending.Writer.TryWrite(pending);
      return await pending.Response.Task.WaitAsync(ct);
    }

    public async Task<Pending> NextAsync() =>
      await _pending
        .Reader.ReadAsync()
        .AsTask()
        .WaitAsync(TimeSpan.FromSeconds(5));
  }

  private sealed class Pending(Guid? mailbox)
  {
    public Guid? Mailbox => mailbox;
    public Guid? AnsweredWith { get; private set; }
    public TaskCompletionSource<HttpResponseMessage> Response { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Answer(
      Guid? change = null,
      bool resync = false,
      Guid? mailbox = null
    )
    {
      AnsweredWith = mailbox ?? Mailbox!.Value;
      Response.TrySetResult(
        Fixture.Json(AnsweredWith.Value, resync, change is { } id ? [id] : [])
      );
    }
  }
}
