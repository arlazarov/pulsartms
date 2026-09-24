using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Client.Models.DTO;
using Client.Models.DTO.Messaging;
using Client.Services;
using Client.Shared.MessagesNotice;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;

namespace Client.Tests.Messaging;

// The unread notice on every page: the leading tab reads once per burst
// and relays the count, other tabs only show it, a notification is due
// only when a conversation's latest arrival rises, and nothing from an
// earlier account or a disposed notice is shown, relayed or notified.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class MessagingNoticesTests
{
  private const string ChannelModule =
    "./js/generated/shared/messagingChannel.js";
  private const string NoticesModule =
    "./js/generated/shared/messagingNotices.js";
  private static readonly Guid A = Guid.NewGuid();
  private static readonly Guid B = Guid.NewGuid();

  [Fact]
  public async Task ABurstOnTheLeaderIsOneReadAndOneRelay()
  {
    await using var f = new Fixture();
    await f.Notices.JoinAsync();
    Assert.Equal(1, f.Reads);

    // A follower does not read on signals.
    for (var i = 0; i < 3; i++)
      f.Signals.Receive("change", A.ToString());
    f.Time.Advance(MessagingNotices.Coalesce);
    await Task.Delay(20);
    Assert.Equal(1, f.Reads);

    await f.Signals.Lead();
    for (var i = 0; i < 5; i++)
      f.Signals.Receive("change", A.ToString());
    f.Time.Advance(MessagingNotices.Coalesce);
    await Eventually(() => Assert.Equal(2, f.Reads));
    await Eventually(() => Assert.Single(f.Relayed("unread")));
    await Task.Delay(20);
    Assert.Equal(2, f.Reads);
  }

  // The tab's own first read answers after the leader's newer count: the
  // older answer is dropped.
  [Fact]
  public async Task AFollowerShowsTheLeadersCountWithoutReading()
  {
    await using var f = new Fixture();
    var first = new TaskCompletionSource<UnreadCount>();
    f.Pending = first;
    var page = f.Context.Render<MessagesNotice>();
    await Eventually(() => Assert.Equal(1, f.Reads));
    Assert.Empty(page.FindAll(".sidebar__badge"));

    f.Signals.Unread(2, false, 3);
    first.SetResult(new(0, false, 0));
    await Task.Delay(20);

    page.WaitForAssertion(
      () => Assert.Equal("2", page.Find(".sidebar__badge").TextContent)
    );
    Assert.Equal(
      "2 unread conversations",
      page.Find(".sidebar__badge").GetAttribute("aria-label")
    );
    Assert.Equal(1, f.Reads);
  }

  // Reads, claims and replies change the count but never raise the
  // newest arrival; a new message, including a late one, does.
  [Fact]
  public async Task OnlyARisenArrivalNotifies()
  {
    await using var f = new Fixture();
    f.Answers.Enqueue(new(1, false, 3));
    await f.Notices.JoinAsync();
    await f.Signals.Lead();

    await f.CountAsync(new(1, false, 3));
    await f.CountAsync(new(2, false, 4));
    await f.CountAsync(new(2, false, 6));
    await f.CountAsync(new(1, false, 4));
    await f.CountAsync(new(0, false, 0));

    await Eventually(() => Assert.Equal(2, f.Notified.Invocations.Count));
    await Task.Delay(50);
    Assert.Equal(2, f.Notified.Invocations.Count);
  }

  // More unread conversations than the notice lists: reading one of them
  // brings an older one into view. The count stays and a different
  // conversation is listed, but the newest arrival does not rise, so no
  // notice; a new message does raise it.
  [Fact]
  public async Task AnOlderConversationComingIntoViewDoesNotNotify()
  {
    await using var f = new Fixture();
    f.Answers.Enqueue(new(99, true, 200));
    await f.Notices.JoinAsync();
    await f.Signals.Lead();

    await f.CountAsync(new(99, true, 200));
    await f.CountAsync(new(99, true, 199));
    await f.CountAsync(new(99, false, 150));
    await Task.Delay(50);
    Assert.Empty(f.Notified.Invocations);

    await f.CountAsync(new(99, true, 201));
    await Eventually(() => Assert.Single(f.Notified.Invocations));
  }

  [Theory]
  [InlineData("relay", "account")]
  [InlineData("relay", "dispose")]
  [InlineData("import", "account")]
  [InlineData("import", "dispose")]
  public async Task AnEndedAccountNeverNotifies(string paused, string ending)
  {
    await using var f = new Fixture(pauseImport: paused == "import");
    if (paused == "relay")
      f.HoldRelay();
    f.Answers.Enqueue(new(1, false, 3));
    await f.Notices.JoinAsync();
    await f.Signals.Lead();
    f.Answers.Enqueue(new(2, false, 4));
    f.Signals.Receive("change", B.ToString());
    f.Time.Advance(MessagingNotices.Coalesce);
    await Eventually(() => Assert.Equal(2, f.Reads));
    await Eventually(
      () =>
        Assert.True(
          paused == "relay" ? f.Relayed("unread").Count == 1 : f.Importing
        )
    );

    if (ending == "account")
      f.Auth.SetClaims(
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
      );
    else
      await f.Notices.DisposeAsync();
    f.Release();
    await Task.Delay(50);

    Assert.Empty(f.Notified.Invocations);
  }

  [Fact]
  public async Task AnAnswerForTheOldAccountIsDropped()
  {
    await using var f = new Fixture();
    var late = new TaskCompletionSource<UnreadCount>();
    f.Pending = late;
    await f.Signals.JoinAsync();
    var joining = f.Notices.JoinAsync();
    await Eventually(() => Assert.Equal(1, f.Reads));

    f.Pending = null;
    f.Answers.Enqueue(new(1, false, 1));
    f.Auth.SetClaims(
      new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
    );
    await Eventually(() => Assert.Equal(1, f.Notices.Current?.Conversations));
    late.SetResult(new(5, false, 9));
    await joining;

    Assert.Equal(1, f.Notices.Current!.Conversations);
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

  private sealed class Fixture : IAsyncDisposable
  {
    private readonly TaskCompletionSource _released = new();
    private readonly BunitJSModuleInterop _channel;
    private JSRuntimeInvocationHandler? _posts;
    private int _reads;

    public ClientComponentContext Context { get; }
    public BunitAuthorizationContext Auth { get; }
    public FakeTimeProvider Time { get; } = new();
    public Queue<UnreadCount> Answers { get; } = new();
    public TaskCompletionSource<UnreadCount>? Pending { get; set; }
    public JSRuntimeInvocationHandler<bool> Notified { get; }
    public bool Importing { get; private set; }
    public int Reads => _reads;

    public Fixture(bool pauseImport = false)
    {
      Context = new ClientComponentContext(SendAsync);
      Context.JSInterop.Mode = JSRuntimeMode.Strict;
      Auth = Context.AddAuthorization();
      Auth.SetAuthorized("Ann");
      Auth.SetClaims(
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
      );
      Context
        .JSInterop.SetupModule("./js/generated/shared/authStorage.js")
        .Setup<string?>("readSession")
        .SetResult(
          JsonSerializer.Serialize(
            new
            {
              Id = Guid.NewGuid(),
              AccessToken = "access",
              RefreshToken = "refresh",
            }
          )
        );
      _channel = Context.JSInterop.SetupModule(ChannelModule);
      _channel.SetupVoid("join", _ => true).SetVoidResult();
      _channel.SetupVoid("leave").SetVoidResult();
      _channel.SetupVoid("post", _ => true).SetVoidResult();
      Notified = Context
        .JSInterop.SetupModule(NoticesModule)
        .Setup<bool>("notify", _ => true);
      Notified.SetResult(true);
      Context.Services.AddSingleton<TokenStorageService>();
      Context.Services.AddSingleton<TimeProvider>(Time);
      Context.Services.AddSingleton<MessagingSignals>();
      Context.Services.AddSingleton(sp => new MessagingNotices(
        sp.GetRequiredService<ApiService>(),
        sp.GetRequiredService<MessagingSignals>(),
        sp.GetRequiredService<AuthenticationStateProvider>(),
        pauseImport
          ? new PausedImport(this, sp.GetRequiredService<IJSRuntime>())
          : sp.GetRequiredService<IJSRuntime>(),
        Time
      ));
    }

    public MessagingSignals Signals =>
      Context.Services.GetRequiredService<MessagingSignals>();

    public MessagingNotices Notices =>
      Context.Services.GetRequiredService<MessagingNotices>();

    public void HoldRelay() => _posts = _channel.SetupVoid("post", _ => true);

    public void Release()
    {
      _posts?.SetVoidResult();
      _released.TrySetResult();
    }

    public IReadOnlyList<string> Relayed(string kind) =>
      [
        .. _channel
          .Invocations["post"]
          .Select(x => JsonSerializer.Serialize(x.Arguments[0]))
          .Where(x => x.Contains($"\"kind\":\"{kind}\"")),
      ];

    // One signal on the leader, answered with this count; done when the
    // count has been relayed, which follows the read and precedes nothing
    // but the notice, so the next signal starts a new count.
    public async Task CountAsync(UnreadCount answer)
    {
      var relayed = Relayed("unread").Count;
      Answers.Enqueue(answer);
      Signals.Receive("change", A.ToString());
      Time.Advance(MessagingNotices.Coalesce);
      await Eventually(
        () => Assert.Equal(relayed + 1, Relayed("unread").Count)
      );
    }

    private async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      if (request.RequestUri!.AbsolutePath != "/api/messaging/unread")
        return new(HttpStatusCode.ServiceUnavailable);
      Interlocked.Increment(ref _reads);
      var count =
        Pending is { } pending ? await pending.Task
        : Answers.TryDequeue(out var next) ? next
        : new UnreadCount(0, false, 0);
      return new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<UnreadCount>
          {
            Success = true,
            Response = count,
          }
        ),
      };
    }

    private sealed class PausedImport(Fixture fixture, IJSRuntime inner)
      : IJSRuntime
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
        identifier == "import" && args?[0] as string == NoticesModule
          ? PausedAsync<TValue>(identifier, cancellationToken, args)
          : inner.InvokeAsync<TValue>(identifier, cancellationToken, args);

      private async ValueTask<TValue> PausedAsync<TValue>(
        string identifier,
        CancellationToken cancellationToken,
        object?[]? args
      )
      {
        fixture.Importing = true;
        await fixture._released.Task;
        return await inner.InvokeAsync<TValue>(
          identifier,
          cancellationToken,
          args
        );
      }
    }

    public ValueTask DisposeAsync() => Context.DisposeAsync();
  }
}
