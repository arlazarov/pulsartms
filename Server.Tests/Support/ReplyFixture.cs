using Application.Features.Routing.Background;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Options;
using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Messaging;
using Application.Interfaces;
using Application.Models;
using Application.Storage;
using Domain.Entities;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Tests.Storage;

namespace Server.Tests.Support;

// Conversations, the reply queue and the outbox worker over one SQLite
// database, with the development file store, a scripted provider and a
// manual clock.
internal sealed class ReplyFixture : IAsyncDisposable
{
  private DispatchSyncFixture sync = null!;
  private ServiceProvider services = null!;
  public FakeDriverMessaging Messaging { get; } = new();
  public List<IInterceptor> Interceptors { get; } = [];
  public MessagingEvents Events { get; } = new();
  public OutboxSignal Signal { get; } = new();
  public ManualTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
  public AppDbContext Db => sync.Db;
  public OutboundMessageOperation Worker =>
    new(
      services.GetRequiredService<IServiceScopeFactory>(),
      Events,
      Signal,
      Clock,
      NullLogger<OutboundMessageOperation>.Instance
    );

  public static async Task<ReplyFixture> CreateAsync()
  {
    var f = new ReplyFixture { sync = await DispatchSyncFixture.CreateAsync() };
    var collection = new ServiceCollection();
    collection.AddScoped<IAppDbContext>(_ =>
      f.sync.NewContext([.. f.Interceptors])
    );
    collection.AddSingleton<IDriverMessaging>(f.Messaging);
    collection.AddSingleton<ICurrentCompany>(new TestCompany());
    collection.AddSingleton(FileStorageTests.Configuration());
    collection.AddSingleton<TimeProvider>(f.Clock);
    collection.AddSingleton<IStorageSecrets>(
      new StorageSecrets(new EphemeralDataProtectionProvider())
    );
    collection.AddSingleton(Options.Create(new StorageOptions()));
    collection.AddSingleton<StorageUploadGate>();
    collection.AddScoped(sp =>
      (AppDbContext)sp.GetRequiredService<IAppDbContext>()
    );
    collection.AddScoped<IFileStorageProvider, DatabaseFileStorage>();
    collection.AddScoped<FileStore>();
    f.services = collection.BuildServiceProvider();
    foreach (var name in new[] { "me", "colleague" })
      f.Db.Users.Add(
        new User
        {
          Id = Guid.NewGuid(),
          IdentityUserId = name,
          Name = name,
          Email = $"{name}@example.invalid",
        }
      );
    await f.Db.SaveChangesAsync();
    return f;
  }

  public ConversationReplies Replies(string who = "me")
  {
    Db.ChangeTracker.Clear();
    return new(Db, new Caller(who), new TestCompany(), Events, Queue(), Clock);
  }

  public Task<RequestResponse<MessageView>> SendAsync(
    SendConversationMessageCommand command
  ) => Replies().Handle(command, default);

  public async Task<(Guid, Guid)> ConversationAsync(
    string phone = "+15558234327",
    int hoursAgo = 1
  )
  {
    var id = await InboundAsync(
      phone,
      $"wamid.{phone}",
      "Where do I fuel?",
      hoursAgo
    );
    var conversation = await Db
      .Conversations.AsNoTracking()
      .SingleAsync(x => x.Participant == phone);
    return (conversation.Id, id);
  }

  public async Task<Guid> InboundAsync(
    string phone,
    string providerId,
    string text,
    int hoursAgo = 0
  )
  {
    Db.ChangeTracker.Clear();
    var at = Clock
      .GetUtcNow()
      .UtcDateTime.AddHours(-hoursAgo)
      .AddSeconds(hoursAgo == 0 ? 1 : 0);
    await new InboxRecorder(Db, Clock).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        new DriverMessageInboundEvent(phone, at)
        {
          ProviderMessageId = providerId,
          Text = text,
        },
      ],
      default
    );
    await Db.SaveChangesAsync();
    Db.ChangeTracker.Clear();
    return await Db
      .ConversationMessages.AsNoTracking()
      .Where(x => x.ProviderMessageId == providerId)
      .Select(x => x.Id)
      .SingleAsync();
  }

  public AppDbContext Context(params IInterceptor[] interceptors) =>
    sync.NewContext(interceptors);

  public Task<long> RevisionAsync(Guid conversation) =>
    Db
      .Conversations.AsNoTracking()
      .Where(x => x.Id == conversation)
      .Select(x => x.Revision)
      .SingleAsync();

  public Task<ConversationMessage> MessageAsync(Guid id)
  {
    Db.ChangeTracker.Clear();
    return Db.ConversationMessages.AsNoTracking().SingleAsync(x => x.Id == id);
  }

  public async ValueTask DisposeAsync()
  {
    await services.DisposeAsync();
    await sync.DisposeAsync();
  }

  public ReplyQueue Queue() =>
    new(Db, new TestCompany(), Events, Signal, Clock);

  public ConversationFilesAndTemplates Files(
    IEnumerable<MessageTemplate>? templates = null
  )
  {
    Db.ChangeTracker.Clear();
    var store = FileStorageTests.Store(Db, clock: Clock);
    return new(
      Db,
      new Caller("me"),
      Queue(),
      store,
      new StoredFileCheck(store),
      Options.Create(new MessagingOptions { Templates = [.. templates ?? []] }),
      Clock
    );
  }

  public sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => identity;
  }
}
